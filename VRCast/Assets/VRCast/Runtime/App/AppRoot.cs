using System;
using System.IO;
using UnityEngine;
using VRCast.Animations;
using VRCast.Audio;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Dynamics;
using VRCast.Output;
using VRCast.Platform;
using VRCast.Remote;
using VRCast.Rendering;
using VRCast.Tracking;
using VRCast.UI;

namespace VRCast.App
{
    /// <summary>
    /// 起動時に各機能を生成して結線する。機能のロジック自体は持たない。
    /// </summary>
    public class AppRoot : MonoBehaviour
    {
        // コマンドライン引数でアバターを指定するオプション
        private const string AvatarArgument = "--avatar";

        private AvatarSession _session;
        private OrbitCameraController _orbit;
        private AppSettings _settings;
        private MicrophoneInput _microphone;
        private TrackingReceiver _tracker;
        private TrackingSkeletonView _skeleton;
        private RenderingController _rendering;
        private string _initialAvatarPath;

        // カメラの視点・見た目を記録する対象のアバターのパスと、最後に記録した視点・見た目
        private string _cameraAvatarPath;
        private CameraPose _recordedPose;
        private AvatarLook _recordedLook;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            // 既に存在する、または別の GPU で起動し直して終了する途中なら生成しない（トラッカー・カメラを二重に開かない）
            if (FindObjectOfType<AppRoot>() != null || GpuSelection.IsRelaunching)
            {
                return;
            }

            // シーン遷移でも破棄されないルートを生成
            var root = new GameObject("VRCast");
            DontDestroyOnLoad(root);
            root.AddComponent<AppRoot>();
        }

        private void Awake()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // ウィンドウのタイトルにバージョンを付ける（productName を変えると保存先のフォルダまで変わるため実行時に書き換える）
            UnityWindow.SetTitle($"{Application.productName} {Application.version}");
#endif

            // アバター管理
            _session = gameObject.AddComponent<AvatarSession>();
            _session.AvatarLoaded += OnAvatarLoaded;

            // カメラ操作（シーンにカメラが無ければ生成）
            UnityEngine.Camera mainCamera = UnityEngine.Camera.main;
            if (mainCamera == null)
            {
                mainCamera = new GameObject("Main Camera").AddComponent<UnityEngine.Camera>();
                mainCamera.tag = "MainCamera";
            }

            // UnityEngine.Object は ?? で偽 null を判定できないため明示的に確認
            _orbit = mainCamera.GetComponent<OrbitCameraController>();
            if (_orbit == null)
            {
                _orbit = mainCamera.gameObject.AddComponent<OrbitCameraController>();
            }

            // 背景・解像度・ライト（Bootstrap 未実行時は既定値で動かす）
            _settings = AppBootstrap.Settings ?? new AppSettings();
            _rendering = gameObject.AddComponent<RenderingController>();
            _rendering.Initialize(mainCamera, _settings);

            // 本体のプロセスの優先度・電力調整（トラッカーは TrackerProcess が同じ設定で扱う）
            gameObject.AddComponent<ProcessTuner>().Initialize(_settings);

            // 仮想カメラ出力（描画結果を受け取るためカメラに付ける）
            var virtualCamera = mainCamera.gameObject.AddComponent<VirtualCameraOutput>();
            virtualCamera.Initialize(_settings);

            // Spout2 出力（同じくカメラに付ける）
            var spout = mainCamera.gameObject.AddComponent<SpoutOutput>();
            spout.Initialize(_settings);

            // リップシンク用のマイク入力（アプリ全体で 1 つ）
            _microphone = gameObject.AddComponent<MicrophoneInput>();
            _microphone.Initialize(_settings);

            // トラッキング受信（顔・腕・手、アプリ全体で 1 つ）
            _tracker = gameObject.AddComponent<TrackingReceiver>();
            _tracker.Initialize(_settings);

            // 受信値をそのまま描く確認用の表示（アバターの代わりに表示）
            _skeleton = gameObject.AddComponent<TrackingSkeletonView>();
            _skeleton.Initialize(mainCamera, _tracker, _tracker, _settings);

            // 同梱トラッカー（MediaPipe / OpenSeeFace）の起動・停止（任意。外部で起動したものも受信できる）
            var trackerProcess = gameObject.AddComponent<TrackerProcess>();
            trackerProcess.Initialize(_settings);

            // ウィンドウへのファイルのドロップ（読み込みは操作パネルが行う）
            var fileDrop = gameObject.AddComponent<FileDropReceiver>();

            // 新しいバージョンの確認（起動時に 1 回。設定で OFF にできる）
            var updates = gameObject.AddComponent<UpdateChecker>();
            updates.Initialize(_settings);

            // 外部（Stream Deck・OSC アプリ等）からの表情の操作（設定で ON のときだけ待ち受ける）
            var remote = gameObject.AddComponent<RemoteControl>();
            remote.Initialize(_session, _settings);

            // 操作パネル
            _initialAvatarPath = ResolveInitialAvatarPath();
            gameObject.AddComponent<MainPanel>().Initialize(
                _session, _orbit, _rendering, _microphone, _tracker, trackerProcess, _skeleton, virtualCamera, spout,
                fileDrop, updates, remote, _settings, _initialAvatarPath);
        }

        private void Start()
        {
            // 起動時に指定または前回のアバターがあれば自動で読み込む
            if (!string.IsNullOrEmpty(_initialAvatarPath) && File.Exists(_initialAvatarPath))
            {
                _session.Load(_initialAvatarPath);
            }
        }

        private void LateUpdate()
        {
            // 表示中のアバターの視点として記録できる状態か（読込中・アンロード後は前のアバターのまま記録しない）
            LoadedAvatar current = _session.Current;
            if (_cameraAvatarPath == null || current == null || current.SourcePath != _cameraAvatarPath)
            {
                return;
            }

            // 視点が変わったときだけ記録する（保存は終了時）
            CameraPose pose = _orbit.Pose;
            if (!pose.SameAs(_recordedPose))
            {
                _recordedPose = pose;
                _settings.SetAvatarCamera(_cameraAvatarPath, pose);
            }

            // ライト・待機ポーズ等も変わったときだけ記録する
            AvatarLook look = AvatarLook.From(_settings);
            if (!look.SameAs(_recordedLook))
            {
                _recordedLook = look;
                _settings.SetAvatarLook(_cameraAvatarPath, look);
            }
        }

        private void OnDestroy()
        {
            // イベント購読を解除
            if (_session != null)
            {
                _session.AvatarLoaded -= OnAvatarLoaded;
            }
        }

        private void OnAvatarLoaded(LoadedAvatar avatar)
        {
            // このアバターで前回使ったライト・待機ポーズ・向きに戻す（待機ポーズは次の PoseController が読む）。
            // 未記録のアバターは今の設定のまま使い、以降の記録で覚える
            if (_settings.TryGetAvatarLook(avatar.SourcePath, out AvatarLook look))
            {
                look.ApplyTo(_settings);
                _rendering.ApplyAll();
            }

            // 待機ポーズ・表情・まばたき・リップシンク・トラッキング・揺れもの（アバターと一緒に破棄されるよう本体に付ける）
            Transform root = avatar.Instance.transform;
            avatar.Instance.AddComponent<PoseController>().Initialize(avatar.Animator, _settings);

            // BlendShape の上限（このアバターで前回付けたもの）。表情・まばたき等は書き込む前にこれを通し、
            // 初期化時に自分の BlendShape を「顔」として登録するので、それらより先に作る
            avatar.Instance.AddComponent<BlendShapeLimiter>().Initialize(
                root, _settings.GetBlendShapeLimits(avatar.SourcePath));
            var expressions = avatar.Instance.AddComponent<ExpressionController>();
            expressions.Initialize(root, avatar.Expressions);
            expressions.LoadHotkeys(_settings.GetExpressionHotkeys(avatar.SourcePath), _settings);
            var blink = avatar.Instance.AddComponent<BlinkController>();
            blink.Initialize(root, avatar.Descriptor.eyelids, _settings, expressions);
            var lipSync = avatar.Instance.AddComponent<LipSyncController>();
            lipSync.Initialize(root, avatar.Descriptor.lipSync, _microphone, _settings);

            // 首・頭の基準回転を記録するため待機ポーズ適用後に初期化
            avatar.Instance.AddComponent<FaceTrackingDriver>().Initialize(
                avatar.Animator, _tracker, blink, lipSync, expressions, _settings);

            // 腕・指の向きの基準を記録するため待機ポーズ適用後に初期化
            avatar.Instance.AddComponent<HandTrackingDriver>().Initialize(avatar.Animator, _tracker, _settings);

            // Constraint（揺れものの静止姿勢に反映されるよう先に初期化）
            avatar.Instance.AddComponent<ConstraintSolver>().Initialize(avatar.Animator, avatar.Constraints);

            // 揺れもの（静止姿勢を記録するため待機ポーズ適用後に初期化）
            avatar.Instance.AddComponent<PhysBoneSimulator>().Initialize(avatar.Animator, avatar.PhysBones, _settings);

            // 確認用の表示はアバターの腰の位置・向きに描く（非 Humanoid は足元基準）
            Transform hips = avatar.Animator != null && avatar.Animator.isHuman
                ? avatar.Animator.GetBoneTransform(HumanBodyBones.Hips)
                : null;
            _skeleton.SetAnchor(root, hips);

            // アバターの明るさ（マテリアルの色の倍率）を反映
            _rendering.SetAvatar(avatar.Instance);

            // 前回の視点があれば画角を先に戻す（Reset の戻り先の距離を同じ画角で求めるため）
            bool saved = _settings.TryGetAvatarCamera(avatar.SourcePath, out CameraPose pose);
            if (saved)
            {
                _orbit.FieldOfView = pose.fieldOfView;
            }

            // アバター本体（Humanoid は骨格基準）が映るようにカメラを合わせ、前回の視点があればそこへ戻す
            _orbit.Frame(avatar.CalculateFramingBounds());
            if (saved)
            {
                _orbit.SetPose(pose);
            }

            // 以降の視点・見た目の変化をこのアバターの分として記録する（最近使ったアバターの先頭にもなる）
            _cameraAvatarPath = avatar.SourcePath;
            _recordedPose = _orbit.Pose;
            _settings.SetAvatarCamera(_cameraAvatarPath, _recordedPose);
            _recordedLook = AvatarLook.From(_settings);
            _settings.SetAvatarLook(_cameraAvatarPath, _recordedLook);

            // 次回起動時に自動で読み込めるよう記録（保存は終了時）
            _settings.lastAvatarPath = avatar.SourcePath;
        }

        private static string ResolveInitialAvatarPath()
        {
            // コマンドライン引数を優先
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == AvatarArgument)
                {
                    return args[i + 1];
                }
            }

            // 無ければ前回のアバター
            return AppBootstrap.Settings?.lastAvatarPath ?? string.Empty;
        }
    }
}
