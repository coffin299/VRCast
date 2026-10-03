"""VRCast 用 MediaPipe トラッカー。

Web カメラの映像から顔（頭の向き・表情）・腕・手を推定し、JSON を UDP で VRCast へ送る。
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
    leftHand    本人の左手 21 点の world 座標（映っていなければ空）
    rightHand   本人の右手 21 点の world 座標（映っていなければ空）
"""

import argparse
import json
import os
import socket
import sys
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

# 送信する BlendShape の並び（VRCast 側 MediaPipePacket.BlendShapeNames と一致させる）
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

# カメラの既定の解像度・フレームレート
DEFAULT_WIDTH = 640
DEFAULT_HEIGHT = 480
DEFAULT_FPS = 30

# 連続でこの回数フレームを読めなければ終了する（VRCast が再起動する）
MAX_READ_FAILURES = 30

# 送信する値の小数点以下の桁数（パケットを小さくする）
DIGITS = 5


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
    # 親プロセス（VRCast）の PID。終了したらトラッカーも終了する
    parser.add_argument("--parent-pid", type=int, default=0)
    return parser.parse_args()


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


def configure_output():
    """標準出力・エラー出力を UTF-8 にする（日本語のカメラ名を VRCast へ正しく渡す）。"""
    for stream in (sys.stdout, sys.stderr):
        # reconfigure が無い環境（出力無し等）はそのまま
        if hasattr(stream, "reconfigure"):
            stream.reconfigure(encoding="utf-8", errors="replace")


def list_cameras():
    """DirectShow のカメラ名を「番号: 名前」の形式で表示する。"""
    # OpenCV の CAP_DSHOW と同じ列挙順の名前を取得
    from pygrabber.dshow_graph import FilterGraph

    names = FilterGraph().get_input_devices()
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
    """手の左右ラベルから本人の左右を返す。

    MediaPipe は鏡像（自撮り）入力を前提にラベルを付けるため、反転しない
    カメラ映像では逆になる。
    """
    label = handedness[0].category_name if handedness else "Left"
    return "rightHand" if label == "Left" else "leftHand"


def assign_hands(hand_result, pose_result):
    """検出した手を本人の左手・右手へ割り当てる。

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


def build_packet(face_result, pose_result, hand_result):
    """推定結果から送信する JSON のバイト列を作る。"""
    packet = {"v": PROTOCOL_VERSION}
    packet.update(face_fields(face_result))
    packet.update(pose_fields(pose_result))
    packet.update(assign_hands(hand_result, pose_result))
    # 区切りの空白を省いて小さくする
    return json.dumps(packet, separators=(",", ":")).encode("utf-8")


def run(arguments):
    """カメラを開き、推定と送信を繰り返す。"""
    capture = open_camera(arguments.capture, arguments.width,
                          arguments.height, arguments.fps)
    # カメラが開けなければ VRCast に理由を表示させて終了
    if capture is None:
        print(f"Failed to open camera {arguments.capture}", file=sys.stderr)
        return 1

    # OpenCV の OpenCL（T-API）を明示的に無効化
    cv2.ocl.setUseOpenCL(False)

    # 親プロセスの指定があれば監視する（手動実行時は監視しない）
    watch = (ParentWatch(arguments.parent_pid)
             if arguments.parent_pid > 0 else None)

    face, pose, hands = create_landmarkers(not arguments.no_hands)
    sender = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (arguments.ip, arguments.port)
    print(f"Tracking camera {arguments.capture} -> "
          f"{arguments.ip}:{arguments.port}")
    sys.stdout.flush()

    failures = 0
    last_timestamp = -1
    try:
        while True:
            # 親プロセスが終了していればカメラを解放して終了
            if watch is not None and not watch.alive():
                print("Parent process exited", file=sys.stderr)
                return 0

            ok, frame = capture.read()
            # 読めないフレームが続いたらカメラ切断とみなして終了
            if not ok:
                failures += 1
                if failures >= MAX_READ_FAILURES:
                    print("Camera stopped delivering frames",
                          file=sys.stderr)
                    return 2
                continue
            failures = 0

            # 動画モードはタイムスタンプ（ms）が単調増加である必要がある
            timestamp = max(last_timestamp + 1,
                            int(time.perf_counter() * 1000))
            last_timestamp = timestamp

            # OpenCV の BGR を MediaPipe の RGB 画像へ
            rgb = cv2.cvtColor(frame, cv2.COLOR_BGR2RGB)
            image = mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb)

            # 顔・体・（有効なら）手を推定
            face_result = face.detect_for_video(image, timestamp)
            pose_result = pose.detect_for_video(image, timestamp)
            hand_result = (hands.detect_for_video(image, timestamp)
                           if hands is not None else None)

            try:
                sender.sendto(
                    build_packet(face_result, pose_result, hand_result),
                    target)
            except OSError:
                # 受信側が未起動などの送信エラーは無視して続ける
                pass
    finally:
        # カメラ・推定器・ソケット・監視ハンドルを解放
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
    # 一覧表示モード
    if arguments.list_cameras > 0:
        list_cameras()
        return 0
    try:
        return run(arguments)
    except KeyboardInterrupt:
        # 手動実行時の Ctrl+C は正常終了
        return 0
    except (OSError, RuntimeError, ValueError) as error:
        # モデル欠け・推定器の初期化失敗などは理由を表示して終了
        print(f"Tracker error: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
