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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Create()
        {
            // 既に存在すれば二重生成しない
            if (FindObjectOfType<AppRoot>() != null)
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

            // 仮想カメラ出力（描画結果を受け取るためカメラに付ける）
            var virtualCamera = mainCamera.gameObject.AddComponent<VirtualCameraOutput>();
            virtualCamera.Initialize(_settings);

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

            // 操作パネル
            _initialAvatarPath = ResolveInitialAvatarPath();
            gameObject.AddComponent<MainPanel>().Initialize(
                _session, _orbit, _rendering, _microphone, _tracker, trackerProcess, _skeleton, virtualCamera, fileDrop,
                updates, _settings, _initialAvatarPath);
        }

        private void Start()
        {
            // 起動時に指定または前回のアバターがあれば自動で読み込む
            if (!string.IsNullOrEmpty(_initialAvatarPath) && File.Exists(_initialAvatarPath))
            {
                _session.Load(_initialAvatarPath);
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
            // 待機ポーズ・表情・まばたき・リップシンク・トラッキング・揺れもの（アバターと一緒に破棄されるよう本体に付ける）
            Transform root = avatar.Instance.transform;
            avatar.Instance.AddComponent<PoseController>().Initialize(avatar.Animator, _settings);
            var expressions = avatar.Instance.AddComponent<ExpressionController>();
            expressions.Initialize(root, avatar.Expressions);
            var blink = avatar.Instance.AddComponent<BlinkController>();
            blink.Initialize(root, avatar.Descriptor.eyelids, _settings);
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

            // アバター本体（Humanoid は骨格基準）が映るようにカメラを合わせる
            _orbit.Frame(avatar.CalculateFramingBounds());

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
