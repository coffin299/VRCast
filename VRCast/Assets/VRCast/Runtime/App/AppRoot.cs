using System;
using System.IO;
using UnityEngine;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Rendering;
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
            AppSettings settings = AppBootstrap.Settings ?? new AppSettings();
            var rendering = gameObject.AddComponent<RenderingController>();
            rendering.Initialize(mainCamera, settings);

            // 操作パネル
            _initialAvatarPath = ResolveInitialAvatarPath();
            gameObject.AddComponent<MainPanel>().Initialize(_session, _orbit, rendering, _initialAvatarPath);
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
            // アバター本体（Humanoid は骨格基準）が映るようにカメラを合わせる
            _orbit.Frame(avatar.CalculateFramingBounds());

            // 次回起動時に自動で読み込めるよう記録（保存は終了時）
            if (AppBootstrap.Settings != null)
            {
                AppBootstrap.Settings.lastAvatarPath = avatar.SourcePath;
            }
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
