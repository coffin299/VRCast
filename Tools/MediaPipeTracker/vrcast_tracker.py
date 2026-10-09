"""VRCast 用 MediaPipe トラッカー。

Web カメラの映像から顔（頭の向き・表情）・腕・手を推定し、JSON を UDP で VRCast へ送る。
担当: 顔 = Face Landmarker、体（肩・肘・手首）= Pose Landmarker、手・指 = Hand Landmarker（MediaPipe Hands）。
引数は OpenSeeFace の facetracker と同じ形（-l / -c / -i / -p）にしてあり、
VRCast から同じ手順で起動・カメラ一覧取得ができる。

送信形式（1 パケット 1 フレーム、UTF-8 JSON。VRCast 側は MediaPipePacket.cs）:
    v           プロトコル番号（PROTOCOL_VERSION）
    face        顔が映っているか
    matrix      顔の変換行列（4x4 行優先、MediaPipe の右手系、cm）
    blendshapes BLENDSHAPE_NAMES 順のスコア（0〜1）
    pose        体が映っているか
    arms        左肩・右肩・左肘・右肘・左手首・右手首の world 座標（m）
    visibility  arms の各点の可視度
    leftHand    左手 21 点の world 座標（映っていなければ空）
    rightHand   右手 21 点の world 座標（映っていなければ空）

左右は MediaPipe の体のラベル（本人基準）、world 座標の x は MediaPipe の出力どおり（映像の右向き）。
本人基準への変換は VRCast 側で行う。

頭の行列・腕・手・可視度は One Euro フィルターで平滑化してから送る
（止まっているときの細かい揺れを消し、速い動きでは遅れを抑える。CPU 負荷はほぼ無い）。

負荷を抑える仕組み（VRCast のトラッカーの動作「ぬるぬる」「なめらか」「エコ」で値を変える）:
    --max-fps            推定の回数を毎秒その回数までに間引く
    --pose-every         体・手の推定を N フレームに 1 回にする（顔は毎フレーム。間のフレームは前回の結果を送る）
    --hand-search-every  手が映っていない間、手を探すのを体の推定 N 回に 1 回にする
                         （手が 2 本そろわない間は毎回手のひらの検出からやり直すため重い）
    --hand-every-frame   手が映っている間は、体を間引いたフレームでも手を推定する（指の動きを細かく送る）
カメラの読み取りは別スレッドで続け、推定は常に最新のフレームで行う（カメラ待ちで推定を止めない）。

状態ログ（VRCast のデバッグログタブで行頭から重要度を判定する）:
    INFO: ...   起動・カメラ・モデルの状態（標準出力）
    WARN: ...   読み取り失敗・送信失敗など（エラー出力）
    ERROR: ...  終了につながる失敗（エラー出力）
    STATS: ...  --status-interval 秒ごとの統計（標準出力。VRCast では DEBUG 扱い）

終了コード（VRCast 側 TrackerProcess.DescribeExitCode と一致させる）:
    0 正常終了 / 1 カメラが開けない・エラー / 2 カメラからフレームが届かなくなった
"""

import argparse
import json
import math
import os
import platform
import socket
import sys
import threading
import time

# 実行時に __pycache__ を作らない
sys.dont_write_bytecode = True

# OpenCV の OpenCL を使わず、カーネルのキャッシュ（%TEMP%\opencv\...）も書かせない（cv2 の import 前に設定する）
os.environ["OPENCV_OPENCL_DEVICE"] = "disabled"
os.environ["OPENCV_OPENCL_CACHE_ENABLE"] = "0"

import cv2  # noqa: E402
import mediapipe as mp  # noqa: E402
from mediapipe.tasks.python import BaseOptions  # noqa: E402
from mediapipe.tasks.python import vision  # noqa: E402

# VRCast 側（MediaPipePacket.ProtocolVersion）と一致させるプロトコル番号
PROTOCOL_VERSION = 1

# 送信する BlendShape の並び（VRCast 側 ArKitFace.BlendShapeNames と一致させる）
BLENDSHAPE_NAMES = (
    "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft",
    "browOuterUpRight",
    "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
    "eyeBlinkLeft", "eyeBlinkRight", "eyeLookDownLeft", "eyeLookDownRight",
    "eyeLookInLeft", "eyeLookInRight", "eyeLookOutLeft", "eyeLookOutRight",
    "eyeLookUpLeft", "eyeLookUpRight", "eyeSquintLeft", "eyeSquintRight",
    "eyeWideLeft", "eyeWideRight",
    "jawForward", "jawLeft", "jawOpen", "jawRight",
    "mouthClose", "mouthDimpleLeft", "mouthDimpleRight", "mouthFrownLeft",
    "mouthFrownRight", "mouthFunnel", "mouthLeft", "mouthLowerDownLeft",
    "mouthLowerDownRight", "mouthPressLeft", "mouthPressRight",
    "mouthPucker", "mouthRight", "mouthRollLower", "mouthRollUpper",
    "mouthShrugLower", "mouthShrugUpper", "mouthSmileLeft",
    "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight",
    "mouthUpperUpLeft", "mouthUpperUpRight", "noseSneerLeft",
    "noseSneerRight",
)

# Pose Landmarker の点番号（左肩・右肩・左肘・右肘・左手首・右手首）
POSE_ARM_POINTS = (11, 12, 13, 14, 15, 16)
POSE_LEFT_WRIST = 15
POSE_RIGHT_WRIST = 16

# models フォルダ内のモデルファイル名
FACE_MODEL = "face_landmarker.task"
HAND_MODEL = "hand_landmarker.task"
POSE_MODEL = "pose_landmarker_lite.task"

# Hand Landmarker の閾値（MediaPipe の既定はすべて 0.5）。
# 新しく手を見つけるときは誤検出を避けて既定のまま、一度見つけた手は低めの閾値で追い続ける
# （見失うと毎回手のひらの検出からやり直すため、重くなり指の動きも途切れる）
HAND_DETECTION_CONFIDENCE = 0.5  # 手のひらの検出（新しく見つけるとき）
HAND_PRESENCE_CONFIDENCE = 0.3  # 追跡中の手らしさ（下回ると検出からやり直す）
HAND_TRACKING_CONFIDENCE = 0.3  # 前のフレームとの手の位置の一致度

# カメラの既定の解像度・フレームレート
DEFAULT_WIDTH = 640
DEFAULT_HEIGHT = 480
DEFAULT_FPS = 30

# 連続でこの回数フレームを読めなければ終了する（VRCast が再起動する）
MAX_READ_FAILURES = 30

# 統計の状態ログを出す既定の間隔（秒）
DEFAULT_STATUS_INTERVAL = 5.0

# 新しいフレームを待つ最長時間（秒。この間隔で親プロセスの終了も確認する）
FRAME_WAIT_TIMEOUT = 0.5

# 終了時にカメラの読み取りスレッドを待つ最長時間（秒）
GRABBER_JOIN_TIMEOUT = 2.0

# 同じ種類の警告を出す最短間隔（秒。出力が増えすぎないように）
WARNING_INTERVAL = 10.0

# 顔も体も検出されない状態がこの秒数続いたら、映像の明るさを添えて警告する（以後も同じ間隔で）
NO_PERSON_SECONDS = 10.0

# 映像の明るさ・ばらつきを測るときの間引き（縦横この画素ごと。負荷を抑える）
FRAME_SAMPLE_STEP = 8

# 0〜255 の明るさの平均がこれ未満なら暗すぎ、標準偏差がこれ未満ならほぼ単色（黒画面・レンズを覆っている等）
DARK_BRIGHTNESS = 20.0
FLAT_CONTRAST = 4.0

# --save-frame で保存するのは、カメラを開いてからこの秒数後のフレーム（露出が安定してから）
SAVE_FRAME_DELAY = 2.0

# --save-frame の容量対策: 1 回の起動で 1 枚だけ・同じファイルに上書きし、
# 長辺をこの画素数までに縮小して JPEG 品質を抑える（1 枚あたり数十 KB 程度）
SAVE_FRAME_MAX_SIZE = 640
SAVE_FRAME_QUALITY = 80

# 送信する値の小数点以下の桁数（パケットを小さくする）
DIGITS = 5

# One Euro フィルターの設定（最小カットオフ Hz、速度への追従係数、速度のカットオフ Hz）。
# 最小カットオフが低いほど静止時の揺れが消え、追従係数が大きいほど速い動きで遅れにくい
HEAD_ROTATION_FILTER = (1.5, 0.5, 1.0)  # 行列の回転成分（単位なし）
HEAD_POSITION_FILTER = (1.0, 0.05, 1.0)  # 行列の平行移動（cm）
ARM_FILTER = (0.8, 2.0, 1.0)  # 肩・肘・手首（m）
HAND_FILTER = (1.5, 5.0, 1.0)  # 手の 21 点（手の中心基準の m）
VISIBILITY_FILTER = (1.0, 0.0, 1.0)  # 可視度（0.5 付近のちらつきを抑える）

# 4x4 行列（行優先）の平行移動成分の位置
MATRIX_TRANSLATION = (3, 7, 11)


def parse_arguments():
    """コマンドライン引数を解析する（facetracker と同じ短縮名を使う）。"""
    parser = argparse.ArgumentParser(description="VRCast MediaPipe tracker")
    # 1 以上ならカメラ一覧を表示して終了
    parser.add_argument("-l", "--list-cameras", type=int, default=0)
    # 使うカメラの番号（DirectShow の列挙順）
    parser.add_argument("-c", "--capture", type=int, default=0)
    # 送信先の IP アドレスとポート
    parser.add_argument("-i", "--ip", default="127.0.0.1")
    parser.add_argument("-p", "--port", type=int, default=11573)
    # カメラの解像度・フレームレート
    parser.add_argument("--width", type=int, default=DEFAULT_WIDTH)
    parser.add_argument("--height", type=int, default=DEFAULT_HEIGHT)
    parser.add_argument("--fps", type=int, default=DEFAULT_FPS)
    # 手の推定を止める（腕・手を使わないときの CPU 負荷軽減）
    parser.add_argument("--no-hands", action="store_true")
    # 推定の回数の上限（毎秒、0 以下で無制限）。VRCast の軽量モードで CPU 負荷を下げる
    parser.add_argument("--max-fps", type=float, default=0.0)
    # 体・手の推定を何フレームに 1 回にするか（1 で毎フレーム）
    parser.add_argument("--pose-every", type=int, default=1)
    # 手が映っていない間、体の推定何回に 1 回手を探すか（1 で毎回）
    parser.add_argument("--hand-search-every", type=int, default=1)
    # 手が映っている間は体の間引きに関係なく毎フレーム手を推定する
    parser.add_argument("--hand-every-frame", action="store_true")
    # 親プロセス（VRCast）の PID。終了したらトラッカーも終了する
    parser.add_argument("--parent-pid", type=int, default=0)
    # 統計の状態ログを出す間隔（秒、0 以下で出さない）
    parser.add_argument("--status-interval", type=float,
                        default=DEFAULT_STATUS_INTERVAL)
    # カメラの映像を 1 枚 JPEG で保存する先（トラッカーに何が映っているかの確認用。
    # 手動実行専用で VRCast からは渡さない。1 回の起動で 1 枚・同じファイルに上書き）
    parser.add_argument("--save-frame", default="")
    return parser.parse_args()


def log(level, message):
    """重要度付きの状態ログを 1 行出す（INFO / STATS は標準出力、それ以外はエラー出力）。"""
    stream = sys.stdout if level in ("INFO", "STATS") else sys.stderr
    print(f"{level}: {message}", file=stream)
    # VRCast がすぐ受け取れるよう行ごとに書き出す
    stream.flush()


class Stats:
    """推定の統計を集計し、一定間隔で STATS 行を出す（加算だけなので負荷はほぼ無い）。"""

    def __init__(self, interval, use_hands):
        """interval 秒ごとに出す（0 以下なら出さない）。use_hands は手の推定の有無。"""
        self._interval = interval
        self._use_hands = use_hands
        self._start = time.perf_counter()
        self._clear()

    def _clear(self):
        """集計を 0 に戻す。"""
        # 読み取り・間引き・読み取り失敗・推定・送信失敗の回数
        self.read = 0
        self.skipped = 0
        self.read_failures = 0
        self.inferred = 0
        self.send_errors = 0
        # 体・手の推定を実際に行った回数（間引いたフレームは数えない）
        self.pose_runs = 0
        self.hand_runs = 0
        # 顔・体が見つかった回数と、見つかった手の本数の合計
        self.faces = 0
        self.poses = 0
        self.hands = 0
        # モデルごとの推定時間の合計（ms）
        self.face_ms = 0.0
        self.pose_ms = 0.0
        self.hand_ms = 0.0

    def report(self, now):
        """間隔が過ぎていれば STATS 行を出して集計を戻す。"""
        # 無効、または間隔が過ぎていなければ何もしない
        elapsed = now - self._start
        if self._interval <= 0 or elapsed < self._interval:
            return
        # 0 除算を避けた推定回数（時間はモデルごとに実際に推定した回数で割る）
        inferred = max(self.inferred, 1)
        pose_runs = max(self.pose_runs, 1)
        hand_runs = max(self.hand_runs, 1)
        hands = (f"hands {self.hand_ms / hand_runs:.1f} ms x "
                 f"{self.hand_runs / elapsed:.1f}/s"
                 if self._use_hands else "hands off")
        log("STATS",
            f"camera {self.read / elapsed:.1f} fps, "
            f"inference {self.inferred / elapsed:.1f} fps "
            f"(face {self.face_ms / inferred:.1f} ms, "
            f"pose {self.pose_ms / pose_runs:.1f} ms x "
            f"{self.pose_runs / elapsed:.1f}/s, {hands}), "
            f"face found {self.faces * 100 // inferred}%, "
            f"pose found {self.poses * 100 // inferred}%, "
            f"hands per frame {self.hands / inferred:.2f}, "
            f"skipped {self.skipped}, read failures {self.read_failures}, "
            f"send errors {self.send_errors}")
        self._start = now
        self._clear()


class FrameGrabber:
    """カメラを別スレッドで読み続け、最新のフレームだけを保持する。

    推定中もカメラのバッファを空にし続けるため、推定は常に最新のフレームで行え、
    カメラのフレーム待ちで推定が止まらない（cv2 の読み取り中は GIL を手放す）。
    """

    def __init__(self, capture):
        """capture（開いた cv2.VideoCapture）の読み取りを準備する。"""
        self._capture = capture
        self._condition = threading.Condition()
        # 最新のフレームとその通し番号（0 = まだ無い）
        self._frame = None
        self._sequence = 0
        # 連続の読み取り失敗回数と、前回の取り出し以降の読み取り・失敗の回数
        self._failures = 0
        self._reads = 0
        self._read_failures = 0
        self._stopped = False
        self._thread = threading.Thread(
            target=self._run, name="camera", daemon=True)

    def start(self):
        """読み取りを始める。"""
        self._thread.start()

    def stop(self):
        """読み取りを止めてスレッドの終了を待つ（カメラの解放は呼び出し側）。"""
        with self._condition:
            self._stopped = True
        self._thread.join(GRABBER_JOIN_TIMEOUT)

    @property
    def gave_up(self):
        """連続で読めずに読み取りを止めたら True。"""
        return self._failures >= MAX_READ_FAILURES

    def take_counts(self):
        """前回以降の (読み取り回数, 失敗回数, 連続の失敗回数) を返して数え直す。"""
        with self._condition:
            counts = (self._reads, self._read_failures, self._failures)
            self._reads = 0
            self._read_failures = 0
            return counts

    def wait(self, last_sequence, timeout):
        """last_sequence より新しいフレームを待って (フレーム, 通し番号) を返す。

        時間内に来なければ、または読み取りを止めていれば (None, last_sequence)。
        """
        with self._condition:
            self._condition.wait_for(
                lambda: self._sequence != last_sequence or self.gave_up,
                timeout)
            # 新しいフレームが無ければ無し
            if self._sequence == last_sequence:
                return None, last_sequence
            return self._frame, self._sequence

    def _run(self):
        """止めるか、連続で読めなくなるまで読み続ける。"""
        while True:
            # 止める指示・読み取りの断念を確認する
            with self._condition:
                if self._stopped or self.gave_up:
                    self._condition.notify_all()
                    return
            ok, frame = self._capture.read()
            with self._condition:
                if ok:
                    # 最新のフレームに置き換え、待っている推定側へ知らせる
                    self._frame = frame
                    self._sequence += 1
                    self._reads += 1
                    self._failures = 0
                    self._condition.notify_all()
                else:
                    # 失敗は連続回数で判定する（推定側がカメラの切断として終了する）
                    self._failures += 1
                    self._read_failures += 1


class ParentWatch:
    """親プロセスの終了を検出する（VRCast が異常終了してもトラッカーを残さない）。

    プロセスハンドルを保持して待つため、PID が再利用されても誤判定しない。
    """

    # OpenProcess のアクセス権と WaitForSingleObject の戻り値
    SYNCHRONIZE = 0x00100000
    WAIT_TIMEOUT = 0x00000102

    def __init__(self, pid):
        """pid のプロセスを開く（開けなければ既に終了しているとみなす）。"""
        import ctypes

        self._kernel32 = ctypes.windll.kernel32
        self._handle = self._kernel32.OpenProcess(
            self.SYNCHRONIZE, False, pid)

    def alive(self):
        """親プロセスが動作中なら True。"""
        # 開けなかった親は終了済み
        if not self._handle:
            return False
        # 待たずに状態だけ確認（タイムアウト = まだ動作中）
        return (self._kernel32.WaitForSingleObject(self._handle, 0)
                == self.WAIT_TIMEOUT)

    def close(self):
        """プロセスハンドルを閉じる。"""
        if self._handle:
            self._kernel32.CloseHandle(self._handle)
            self._handle = None


class OneEuroFilter:
    """値の列をまとめて平滑化する One Euro フィルター。

    動きが遅いときはカットオフを下げて揺れを消し、速いときは上げて遅れを抑える。
    """

    def __init__(self, settings):
        """settings は (最小カットオフ Hz, 追従係数, 速度のカットオフ Hz)。"""
        self._min_cutoff, self._beta, self._derivative_cutoff = settings
        self._values = None
        self._derivatives = None
        self._time = 0.0

    def reset(self):
        """見失ったときに呼び、次の値をそのまま採用させる。"""
        self._values = None

    def apply(self, values, now):
        """values（float の列）を平滑化して返す。now は秒。"""
        # 初回・要素数が変わったときはそのまま採用する
        if self._values is None or len(values) != len(self._values):
            self._values = list(values)
            self._derivatives = [0.0] * len(values)
            self._time = now
            return list(self._values)

        # 経過時間（同時刻・逆行は最小値で扱う）
        elapsed = max(now - self._time, 1e-3)
        self._time = now
        # 速度の平滑化係数
        derivative_alpha = self._alpha(self._derivative_cutoff, elapsed)
        for index, value in enumerate(values):
            # 速度を求めて平滑化する
            derivative = (value - self._values[index]) / elapsed
            derivative = (self._derivatives[index]
                          + derivative_alpha
                          * (derivative - self._derivatives[index]))
            self._derivatives[index] = derivative
            # 速いほどカットオフを上げて追従させる
            cutoff = self._min_cutoff + self._beta * abs(derivative)
            alpha = self._alpha(cutoff, elapsed)
            self._values[index] += alpha * (value - self._values[index])
        return list(self._values)

    @staticmethod
    def _alpha(cutoff, elapsed):
        """カットオフ周波数と経過時間から平滑化係数を求める。"""
        time_constant = 1.0 / (2.0 * math.pi * cutoff)
        return 1.0 / (1.0 + time_constant / elapsed)


class Smoother:
    """送信フィールドごとの One Euro フィルターをまとめて持つ。"""

    def __init__(self):
        """頭（回転・平行移動）・腕・可視度・左右の手のフィルターを作る。"""
        self._head_rotation = OneEuroFilter(HEAD_ROTATION_FILTER)
        self._head_position = OneEuroFilter(HEAD_POSITION_FILTER)
        self._arms = OneEuroFilter(ARM_FILTER)
        self._visibility = OneEuroFilter(VISIBILITY_FILTER)
        self._hands = {
            "leftHand": OneEuroFilter(HAND_FILTER),
            "rightHand": OneEuroFilter(HAND_FILTER),
        }

    def apply(self, packet, now):
        """送信フィールド（dict）を平滑化して書き換える。"""
        self._smooth_head(packet, now)
        self._smooth_arms(packet, now)
        for key, smoother in self._hands.items():
            # 映っていない手はフィルターを初期化する
            if not packet[key]:
                smoother.reset()
                continue
            packet[key] = rounded(smoother.apply(packet[key], now))

    def _smooth_head(self, packet, now):
        """頭の行列を回転成分と平行移動成分に分けて平滑化する。"""
        # 顔を見失ったら初期化する
        if not packet["face"]:
            self._head_rotation.reset()
            self._head_position.reset()
            return
        matrix = packet["matrix"]
        # 平行移動（cm）と、それ以外（回転・スケール）を分ける
        rotation_indices = [i for i in range(len(matrix))
                            if i not in MATRIX_TRANSLATION]
        position = self._head_position.apply(
            [matrix[i] for i in MATRIX_TRANSLATION], now)
        rotation = self._head_rotation.apply(
            [matrix[i] for i in rotation_indices], now)
        # 元の位置へ書き戻す
        for value, index in zip(position, MATRIX_TRANSLATION):
            matrix[index] = value
        for value, index in zip(rotation, rotation_indices):
            matrix[index] = value
        packet["matrix"] = rounded(matrix)

    def _smooth_arms(self, packet, now):
        """腕の座標と可視度を平滑化する。"""
        # 体を見失ったら初期化する
        if not packet["pose"]:
            self._arms.reset()
            self._visibility.reset()
            return
        packet["arms"] = rounded(self._arms.apply(packet["arms"], now))
        packet["visibility"] = rounded(
            self._visibility.apply(packet["visibility"], now))


def configure_output():
    """標準出力・エラー出力を UTF-8 にする（日本語のカメラ名を VRCast へ正しく渡す）。"""
    for stream in (sys.stdout, sys.stderr):
        # reconfigure が無い環境（出力無し等）はそのまま
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")


def camera_names():
    """DirectShow のカメラ名を OpenCV の CAP_DSHOW と同じ列挙順で返す。"""
    from pygrabber.dshow_graph import FilterGraph

    return FilterGraph().get_input_devices()


def camera_name(index):
    """カメラ番号の名前を返す（取得できなければ "unknown"。状態ログ用）。"""
    try:
        names = camera_names()
    except Exception:  # noqa: BLE001 - COM のエラー等。名前はログ用なので続行する
        return "unknown"
    # 範囲外の番号は名前なし
    return names[index] if 0 <= index < len(names) else "unknown"


def describe_image(frame):
    """映像の明るさの平均と標準偏差（0〜255、間引いて計算）を返す。"""
    sample = frame[::FRAME_SAMPLE_STEP, ::FRAME_SAMPLE_STEP]
    return float(sample.mean()), float(sample.std())


def diagnose_no_person(frame, seconds):
    """顔も体も検出されないときの、映像の状態に応じた警告文を返す。"""
    brightness, contrast = describe_image(frame)
    measured = f"brightness {brightness:.0f}, contrast {contrast:.1f}"
    # ほぼ単色: 仮想カメラの黒画面・赤外線カメラ・レンズカバー・他アプリが使用中など
    if contrast < FLAT_CONTRAST:
        return (f"no face or body for {seconds:.0f} s and the image is almost "
                f"a single color ({measured}): wrong camera (virtual or "
                "infrared camera), lens covered or privacy shutter closed, "
                "or the camera is used by another app")
    # 暗すぎる
    if brightness < DARK_BRIGHTNESS:
        return (f"no face or body for {seconds:.0f} s and the image is too "
                f"dark ({measured}): turn on a light")
    # 映像は普通: カメラの向き・距離・別のカメラの可能性
    return (f"no face or body for {seconds:.0f} s although the image looks "
            f"normal ({measured}): make sure this camera faces you "
            "(try --save-frame to see what it captures)")


def save_frame(path, frame):
    """フレームを縮小した JPEG で保存する（日本語を含むパスでも書けるようバイト列で書き込む）。"""
    # フォルダを指定された場合は書かない（連番で増やさず、常に 1 ファイルだけにする）
    if os.path.isdir(path):
        log("WARN", f"--save-frame needs a file path, not a folder: {path}")
        return
    # 長辺が上限を超えていれば縦横比を保って縮小する
    height, width = frame.shape[:2]
    scale = SAVE_FRAME_MAX_SIZE / max(width, height)
    if scale < 1.0:
        frame = cv2.resize(frame, (int(width * scale), int(height * scale)),
                           interpolation=cv2.INTER_AREA)
    ok, data = cv2.imencode(".jpg", frame,
                            [cv2.IMWRITE_JPEG_QUALITY, SAVE_FRAME_QUALITY])
    # エンコードできなければ失敗
    if not ok:
        log("WARN", f"failed to encode the frame for {path}")
        return
    try:
        with open(path, "wb") as file:
            file.write(data.tobytes())
        log("INFO", f"saved a camera frame to {path} ({len(data) // 1024} KB)")
    except OSError as error:
        # 書き込み先が無い・権限が無い等は警告だけして続ける
        log("WARN", f"failed to save the frame to {path}: {error}")


def list_cameras():
    """DirectShow のカメラ名を「番号: 名前」の形式で表示する。"""
    names = camera_names()
    print("Available cameras:")
    for index, name in enumerate(names):
        print(f"{index}: {name}")
    sys.stdout.flush()


def base_directory():
    """実行ファイル（exe 化時）またはスクリプトのあるフォルダを返す。"""
    # PyInstaller で exe 化した場合は exe の場所
    if getattr(sys, "frozen", False):
        return os.path.dirname(sys.executable)
    return os.path.dirname(os.path.abspath(__file__))


def load_model(name):
    """models フォルダのモデルをバイト列で読む（日本語を含むパスでも読めるように）。"""
    path = os.path.join(base_directory(), "models", name)
    with open(path, "rb") as file:
        return file.read()


def create_landmarkers(use_hands):
    """顔・体・手の推定器を動画モードで作成する（手は無効なら None）。"""
    face = vision.FaceLandmarker.create_from_options(
        vision.FaceLandmarkerOptions(
            base_options=BaseOptions(
                model_asset_buffer=load_model(FACE_MODEL)),
            running_mode=vision.RunningMode.VIDEO,
            num_faces=1,
            output_face_blendshapes=True,
            output_facial_transformation_matrixes=True,
        )
    )
    pose = vision.PoseLandmarker.create_from_options(
        vision.PoseLandmarkerOptions(
            base_options=BaseOptions(
                model_asset_buffer=load_model(POSE_MODEL)),
            running_mode=vision.RunningMode.VIDEO,
            num_poses=1,
        )
    )
    # 手を使わない場合はモデルを読み込まない
    hands = None
    if use_hands:
        hands = vision.HandLandmarker.create_from_options(
            vision.HandLandmarkerOptions(
                base_options=BaseOptions(
                    model_asset_buffer=load_model(HAND_MODEL)),
                running_mode=vision.RunningMode.VIDEO,
                num_hands=2,
                min_hand_detection_confidence=HAND_DETECTION_CONFIDENCE,
                min_hand_presence_confidence=HAND_PRESENCE_CONFIDENCE,
                min_tracking_confidence=HAND_TRACKING_CONFIDENCE,
            )
        )
    return face, pose, hands


def open_camera(index, width, height, fps):
    """DirectShow でカメラを開く。開けなければ None。"""
    capture = cv2.VideoCapture(index, cv2.CAP_DSHOW)
    # 開けなければ失敗
    if not capture.isOpened():
        return None
    # 解像度・フレームレートを要求（非対応の値はカメラ側で近い値になる）
    capture.set(cv2.CAP_PROP_FRAME_WIDTH, width)
    capture.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
    capture.set(cv2.CAP_PROP_FPS, fps)
    return capture


def describe_camera(capture):
    """カメラの実際のバックエンド・解像度・フレームレートを文字列にする。"""
    # 古い OpenCV には getBackendName が無い
    backend = (capture.getBackendName()
               if hasattr(capture, "getBackendName") else "unknown")
    width = int(capture.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(capture.get(cv2.CAP_PROP_FRAME_HEIGHT))
    fps = capture.get(cv2.CAP_PROP_FPS)
    return f"{backend} {width}x{height} @ {fps:.0f} fps"


def rounded(values):
    """float の列を丸めたリストにする。"""
    return [round(float(value), DIGITS) for value in values]


def flatten_points(landmarks):
    """点の列を x, y, z の繰り返しの 1 次元リストにする。"""
    values = []
    for landmark in landmarks:
        values.extend((landmark.x, landmark.y, landmark.z))
    return rounded(values)


def face_fields(result):
    """顔の推定結果を送信フィールドにする（映っていなければ face = False）。"""
    # 変換行列と BlendShape の両方がそろったときだけ顔あり
    if not result.facial_transformation_matrixes or not result.face_blendshapes:
        return {"face": False, "matrix": [], "blendshapes": []}
    # 行列は numpy の行優先で 16 要素へ
    matrix = result.facial_transformation_matrixes[0]
    # 名前 → スコアの対応から決まった並びで取り出す（無い名前は 0）
    scores = {
        category.category_name: category.score
        for category in result.face_blendshapes[0]
    }
    return {
        "face": True,
        "matrix": rounded(matrix.flatten()),
        "blendshapes": rounded(scores.get(name, 0.0)
                               for name in BLENDSHAPE_NAMES),
    }


def pose_fields(result):
    """体の推定結果を腕の送信フィールドにする（映っていなければ pose = False）。"""
    # world 座標と可視度の両方がそろったときだけ体あり
    if not result.pose_world_landmarks or not result.pose_landmarks:
        return {"pose": False, "arms": [], "visibility": []}
    world = result.pose_world_landmarks[0]
    image = result.pose_landmarks[0]
    points = [world[index] for index in POSE_ARM_POINTS]
    return {
        "pose": True,
        "arms": flatten_points(points),
        "visibility": rounded(image[index].visibility
                              for index in POSE_ARM_POINTS),
    }


def distance(a, b):
    """画像上の 2 点（正規化座標）の距離を返す。"""
    return ((a.x - b.x) ** 2 + (a.y - b.y) ** 2) ** 0.5


def side_from_label(handedness):
    """手の左右ラベルから送信キーを返す。

    手のラベルは鏡像の入力を前提とする（反転していない映像では本人と逆）ため、
    本人基準の体のラベルに合わせて入れ替える。
    """
    # ラベルが無ければ "Left"（= 本人の右手）とみなす
    label = handedness[0].category_name if handedness else "Left"
    # 鏡像基準の "Left" は本人の右手
    return "rightHand" if label == "Left" else "leftHand"


def assign_hands(hand_result, pose_result):
    """検出した手を体のラベルと同じ基準の左手・右手へ割り当てる。

    体が映っていれば手首の位置が近い方、映っていなければ左右ラベルで決める。
    """
    assigned = {"leftHand": [], "rightHand": []}
    # 手を推定していない・映っていなければ両方空
    if hand_result is None or not hand_result.hand_world_landmarks:
        return assigned

    count = len(hand_result.hand_world_landmarks)
    sides = []
    if pose_result.pose_landmarks:
        # 体の左右の手首と、各手の手首（0 番）との距離で決める
        pose = pose_result.pose_landmarks[0]
        left_wrist = pose[POSE_LEFT_WRIST]
        right_wrist = pose[POSE_RIGHT_WRIST]
        wrists = [hand[0] for hand in hand_result.hand_landmarks]
        if count >= 2:
            # 2 本なら距離の合計が小さい組み合わせ
            straight = (distance(wrists[0], left_wrist)
                        + distance(wrists[1], right_wrist))
            crossed = (distance(wrists[0], right_wrist)
                       + distance(wrists[1], left_wrist))
            first = "leftHand" if straight <= crossed else "rightHand"
            second = "rightHand" if first == "leftHand" else "leftHand"
            sides = [first, second]
        else:
            # 1 本なら近い方の手首
            near_left = (distance(wrists[0], left_wrist)
                         <= distance(wrists[0], right_wrist))
            sides = ["leftHand" if near_left else "rightHand"]
    else:
        # 体が映っていなければ左右ラベル
        sides = [side_from_label(hand_result.handedness[i])
                 for i in range(count)]

    for index, side in enumerate(sides[:2]):
        # 同じ側に 2 本割り当たった場合（ラベルの誤り）は後の方を反対側へ
        if assigned[side]:
            side = "rightHand" if side == "leftHand" else "leftHand"
        assigned[side] = flatten_points(
            hand_result.hand_world_landmarks[index])
    return assigned


class Inference:
    """顔は毎フレーム、体・手は間引いて推定する（間引いたフレームは前回の結果を使う）。

    手は映っている間は体と同じ頻度（hand_every_frame なら毎フレーム）で追い、
    映っていない間は探す回数をさらに減らす。
    """

    def __init__(self, landmarkers, pose_every, hand_search_every, stats,
                 hand_every_frame=False):
        """landmarkers は (顔, 体, 手 or None)。頻度は 1 以上に丸める。"""
        self._face, self._pose, self._hands = landmarkers
        self._pose_every = max(1, pose_every)
        self._hand_search_every = max(1, hand_search_every)
        self._hand_every_frame = hand_every_frame
        self._stats = stats
        # 推定したフレーム数・体を推定した回数と、体・手の前回の結果
        self._frames = 0
        self._pose_runs = 0
        self._pose_result = None
        self._hand_result = None

    def detect(self, image, timestamp):
        """1 フレームを推定して (顔, 体, 手) の結果を返す（最初のフレームは必ず全部推定する）。"""
        stats = self._stats
        started = time.perf_counter()
        face_result = self._face.detect_for_video(image, timestamp)
        stats.face_ms += (time.perf_counter() - started) * 1000.0

        # 体（と手）は N フレームに 1 回
        if self._frames % self._pose_every == 0:
            self._detect_body(image, timestamp)
        # 体を間引いたフレームでも、指定があれば映っている手だけは追う
        elif self._hand_every_frame and self._hands_visible():
            self._detect_hands(image, timestamp)
        self._frames += 1
        stats.inferred += 1
        return face_result, self._pose_result, self._hand_result

    def _hands_visible(self):
        """前回の推定で手が映っていれば True。"""
        return (self._hands is not None
                and self._hand_result is not None
                and bool(self._hand_result.hand_world_landmarks))

    def _detect_body(self, image, timestamp):
        """体を推定し、必要なら手も推定する。"""
        stats = self._stats
        started = time.perf_counter()
        self._pose_result = self._pose.detect_for_video(image, timestamp)
        stats.pose_ms += (time.perf_counter() - started) * 1000.0
        stats.pose_runs += 1

        # 手は映っていれば毎回、映っていなければ N 回に 1 回だけ探す
        if self._hands is not None:
            searching = self._pose_runs % self._hand_search_every == 0
            if self._hands_visible() or searching:
                self._detect_hands(image, timestamp)
        self._pose_runs += 1

    def _detect_hands(self, image, timestamp):
        """手を推定して結果を更新する。"""
        stats = self._stats
        started = time.perf_counter()
        self._hand_result = self._hands.detect_for_video(image, timestamp)
        stats.hand_ms += (time.perf_counter() - started) * 1000.0
        stats.hand_runs += 1


def build_packet(face_result, pose_result, hand_result, smoother, now):
    """推定結果を平滑化して、送信する JSON のバイト列を作る。"""
    packet = {"v": PROTOCOL_VERSION}
    packet.update(face_fields(face_result))
    packet.update(pose_fields(pose_result))
    packet.update(assign_hands(hand_result, pose_result))
    # 頭・腕・手・可視度の揺れを抑える
    smoother.apply(packet, now)
    # 区切りの空白を省いて小さくする
    return json.dumps(packet, separators=(",", ":")).encode("utf-8")


def run(arguments):
    """カメラを開き、推定と送信を繰り返す。"""
    # カメラ名は OpenCV がカメラを開く前に取る（COM の初期化を先に済ませて衝突を避ける）
    name = camera_name(arguments.capture)
    # カメラを開く（開くまでの時間も調査用に記録する）
    opening = time.perf_counter()
    capture = open_camera(arguments.capture, arguments.width,
                          arguments.height, arguments.fps)
    # カメラが開けなければ VRCast に理由を表示させて終了
    if capture is None:
        log("ERROR",
            f"Failed to open camera {arguments.capture} (in use by another "
            "app, disconnected, or blocked by Windows camera privacy "
            "settings?)")
        return 1
    log("INFO",
        f"camera {arguments.capture} ({name}) "
        f"opened in {time.perf_counter() - opening:.1f} s: "
        f"{describe_camera(capture)} "
        f"(requested {arguments.width}x{arguments.height} @ "
        f"{arguments.fps} fps)")

    # OpenCV の OpenCL（T-API）を明示的に無効化
    cv2.ocl.setUseOpenCL(False)

    # 親プロセスの指定があれば監視する（手動実行時は監視しない）
    watch = (ParentWatch(arguments.parent_pid)
             if arguments.parent_pid > 0 else None)

    # 推定器を作る（モデルの読み込み時間を記録する）
    use_hands = not arguments.no_hands
    loading = time.perf_counter()
    face, pose, hands = create_landmarkers(use_hands)
    log("INFO",
        f"models loaded in {time.perf_counter() - loading:.1f} s "
        f"(hands {'on' if use_hands else 'off'})")
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (arguments.ip, arguments.port)
    log("INFO", f"Tracking camera {arguments.capture} -> "
                f"{arguments.ip}:{arguments.port}")

    last_timestamp = -1
    smoother = Smoother()
    stats = Stats(arguments.status_interval, use_hands)
    inference = Inference((face, pose, hands), arguments.pose_every,
                          arguments.hand_search_every, stats,
                          arguments.hand_every_frame)
    # カメラは別スレッドで読み続ける（推定は最新のフレームだけを使う）
    grabber = FrameGrabber(capture)
    grabber.start()
    last_sequence = 0
    # 最初のフレームを記録したか・次に読み取り失敗 / 送信失敗を警告してよい時刻
    first_frame = True
    next_read_warning = 0.0
    next_send_warning = 0.0
    # 人（顔か体）を最後に検出した時刻・一度でも検出したか・次に未検出を警告してよい時刻
    last_person = time.perf_counter()
    person_found = False
    next_person_warning = last_person + NO_PERSON_SECONDS
    # 映像の保存（指定時のみ。保存したら空にする）
    save_path = arguments.save_frame
    # 推定の間隔（秒、0 なら全フレーム）と次に推定してよい時刻
    interval = 1.0 / arguments.max_fps if arguments.max_fps > 0 else 0.0
    next_due = 0.0
    try:
        while True:
            # 親プロセスが終了していればカメラを解放して終了
            if watch is not None and not watch.alive():
                log("INFO", "Parent process exited")
                return 0

            # 上限を超えないよう、次に推定してよい時刻まで待つ（その間もカメラは読み続ける）
            wait = next_due - time.perf_counter()
            if wait > 0:
                time.sleep(wait)

            # まだ推定していない新しいフレームを待つ
            frame, sequence = grabber.wait(last_sequence, FRAME_WAIT_TIMEOUT)
            now = time.perf_counter()

            # 読み取りスレッドの回数を集計する
            reads, read_failures, failures = grabber.take_counts()
            stats.read += reads
            stats.read_failures += read_failures
            stats.report(now)

            # 失敗が始まったことを間隔を空けて警告し、続いたらカメラ切断とみなして終了
            if failures > 0 and now >= next_read_warning:
                next_read_warning = now + WARNING_INTERVAL
                log("WARN", "camera read failed; retrying "
                            f"(exits after {MAX_READ_FAILURES} in a row)")
            if grabber.gave_up:
                log("ERROR", "Camera stopped delivering frames")
                return 2

            # 時間内に新しいフレームが来なければ待ち直す
            if frame is None:
                continue
            # 推定に使わずに置き換わったフレームの数（推定がカメラに追いつかない分）
            stats.skipped += sequence - last_sequence - 1
            last_sequence = sequence

            # 最初のフレームだけ、届くまでの時間と実際の大きさを記録する
            if first_frame:
                first_frame = False
                brightness, contrast = describe_image(frame)
                log("INFO",
                    f"first frame after {now - opening:.1f} s: "
                    f"{frame.shape[1]}x{frame.shape[0]}, "
                    f"brightness {brightness:.0f}, contrast {contrast:.1f}")

            # 指定があれば露出が安定した頃のフレームを 1 枚保存する
            if save_path and now - opening >= SAVE_FRAME_DELAY:
                save_frame(save_path, frame)
                save_path = ""

            # 次の推定時刻。大きく遅れたら今を起点にしてまとめて推定しない
            # （半間隔までの遅れは持ち越し、カメラのフレーム間隔とのずれで回数が減りすぎないようにする）
            next_due = max(next_due, now - interval / 2) + interval

            # 動画モードはタイムスタンプ（ms）が単調増加である必要がある
            timestamp = max(last_timestamp + 1,
                            int(time.perf_counter() * 1000))
            last_timestamp = timestamp

            # OpenCV の BGR を MediaPipe の RGB 画像へ
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

            # 顔・体・（有効なら）手を推定する（体・手は間引き、モデルごとの時間は Inference が集計）
            face_result, pose_result, hand_result = inference.detect(
                image, timestamp)
            # 見つかった顔・体・手の数
            has_face = bool(face_result.face_blendshapes)
            has_pose = bool(pose_result.pose_landmarks)
            stats.faces += 1 if has_face else 0
            stats.poses += 1 if has_pose else 0
            if hand_result is not None:
                stats.hands += len(hand_result.hand_world_landmarks)

            # 人の検出状況: 初めて見つけたら記録し、長く見つからなければ映像の状態を添えて警告
            if has_face or has_pose:
                if not person_found:
                    person_found = True
                    log("INFO",
                        f"person detected after {now - opening:.1f} s "
                        f"(face {'yes' if has_face else 'no'}, "
                        f"body {'yes' if has_pose else 'no'})")
                last_person = now
                next_person_warning = now + NO_PERSON_SECONDS
            elif now >= next_person_warning:
                next_person_warning = now + NO_PERSON_SECONDS
                log("WARN", diagnose_no_person(frame, now - last_person))

            try:
                sender.sendto(
                    build_packet(face_result, pose_result, hand_result,
                                 smoother, timestamp / 1000.0),
                    target)
            except OSError as error:
                # 受信側が未起動などの送信エラーは続行し、間隔を空けて警告する
                stats.send_errors += 1
                if now >= next_send_warning:
                    next_send_warning = now + WARNING_INTERVAL
                    log("WARN", f"failed to send to "
                                f"{arguments.ip}:{arguments.port}: {error}")
    finally:
        # 読み取りスレッドを止めてから、カメラ・推定器・ソケット・監視ハンドルを解放
        grabber.stop()
        capture.release()
        sender.close()
        if watch is not None:
            watch.close()
        for landmarker in (face, pose, hands):
            if landmarker is not None:
                landmarker.close()


def main():
    """エントリーポイント。"""
    configure_output()
    arguments = parse_arguments()
    # 一覧表示モード（出力は VRCast が「番号: 名前」として読むため状態ログは出さない）
    if arguments.list_cameras > 0:
        list_cameras()
        return 0
    # 調査用にトラッカー・ライブラリの版と引数を記録する
    log("INFO",
        f"VRCast MediaPipe tracker (protocol {PROTOCOL_VERSION}), "
        f"Python {platform.python_version()}, OpenCV {cv2.__version__}, "
        f"MediaPipe {getattr(mp, '__version__', 'unknown')}")
    log("INFO", f"arguments: {' '.join(sys.argv[1:])}")
    try:
        return run(arguments)
    except KeyboardInterrupt:
        # 手動実行時の Ctrl+C は正常終了
        return 0
    except (OSError, RuntimeError, ValueError) as error:
        # モデル欠け・推定器の初期化失敗などは理由を表示して終了
        log("ERROR", f"Tracker error: {error}")
        return 1


if __name__ == "__main__":
    sys.exit(main())
