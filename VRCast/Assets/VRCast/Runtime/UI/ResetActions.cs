using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Rendering;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// リセット（顔の向き・視線・表情・カメラ）とカメラ目線の切り替え、背景透過のスクリーンショット、離席の実行と表示名。
    /// パネル下部のボタンとショートカットキーで共用する。
    /// </summary>
    public sealed class ResetActions
    {
        private readonly AvatarComponentCache _avatar;
        private readonly OrbitCameraController _orbit;
        private readonly AvatarScreenshot _screenshot;
        private readonly AppSettings _settings;

        public ResetActions(AvatarSession session, OrbitCameraController orbit, AvatarScreenshot screenshot,
            AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _orbit = orbit;
            _screenshot = screenshot;
            _settings = settings;
        }

        /// <summary>
        /// ON / OFF を切り替える操作なら true（ボタンを押し込んだ見た目で状態を出す）。
        /// </summary>
        public static bool IsToggle(ResetAction action)
        {
            return action == ResetAction.LookAtCamera || action == ResetAction.Away;
        }

        /// <summary>
        /// 切り替える操作が今 ON なら true（リセットは常に false）。
        /// </summary>
        public bool IsOn(ResetAction action)
        {
            switch (action)
            {
                case ResetAction.LookAtCamera:
                    return _settings.trackingLookAtCamera;
                case ResetAction.Away:
                    return _settings.trackingAway;
                default:
                    return false;
            }
        }

        /// <summary>
        /// リセットの表示名（表示言語に合わせる）。
        /// </summary>
        public static string Label(ResetAction action)
        {
            switch (action)
            {
                case ResetAction.Head:
                    return Loc.T("Head", "顔の向き", "얼굴 방향", "头部朝向", "頭部朝向");
                case ResetAction.Gaze:
                    return Loc.T("Gaze", "視線", "시선", "视线", "視線");
                case ResetAction.Expression:
                    return Loc.T("Expression", "表情", "표정", "表情", "表情");
                case ResetAction.LookAtCamera:
                    return Loc.T("Look at camera", "カメラ目線", "카메라 시선", "看向镜头", "看向鏡頭");
                case ResetAction.Screenshot:
                    return Loc.T("Screenshot", "スクショ", "스크린샷", "截图", "截圖");
                case ResetAction.Away:
                    return Loc.T("Away", "離席", "자리 비움", "离开", "離席");
                default:
                    return Loc.T("Camera", "カメラ", "카메라", "相机", "相機");
            }
        }

        /// <summary>
        /// 今実行できるなら true（顔の向き・視線はトラッキング受信中、表情は表情が出ているか固定中のみ）。
        /// </summary>
        public bool CanRun(ResetAction action)
        {
            // アバターが替わっていればコンポーネントを取り直す
            _avatar.Refresh();
            switch (action)
            {
                case ResetAction.Head:
                    // 顔か全身（顔の値を送らない送信元もある）を受信中なら正面を取り直せる
                    var head = _avatar.Get<FaceTrackingDriver>();
                    var body = _avatar.Get<FullBodyTrackingDriver>();
                    return (head != null && head.IsTracking) || (body != null && body.IsTracking);
                case ResetAction.Gaze:
                    var face = _avatar.Get<FaceTrackingDriver>();
                    return face != null && face.IsTracking;
                case ResetAction.Expression:
                    var expressions = _avatar.Get<ExpressionController>();
                    return expressions != null && (expressions.Current >= 0 || expressions.IsManual);
                case ResetAction.LookAtCamera:
                    // アバターが無くても切り替えておける（次に読み込んだアバターから効く）
                    return true;
                case ResetAction.Screenshot:
                    // 撮るアバターがあるときだけ
                    return _avatar.HasAvatar;
                case ResetAction.Away:
                    // アバターが無くても切り替えておける（次に読み込んだアバターから効く）
                    return true;
                default:
                    return true;
            }
        }

        /// <summary>
        /// 実行する（実行できない状態なら何もしない）。
        /// </summary>
        public void Run(ResetAction action)
        {
            // 受信していない・表情が出ていない等のときは何もしない
            if (!CanRun(action))
            {
                return;
            }

            switch (action)
            {
                case ResetAction.Head:
                    // 顔の向き・上半身・視線（全身の追従中は体の向き・位置も）を今の向きで正面にする
                    var headDriver = _avatar.Get<FaceTrackingDriver>();
                    if (headDriver != null)
                    {
                        headDriver.Calibrate();
                    }

                    var bodyDriver = _avatar.Get<FullBodyTrackingDriver>();
                    if (bodyDriver != null)
                    {
                        bodyDriver.Calibrate();
                    }

                    break;
                case ResetAction.Gaze:
                    // 視線だけを正面にする
                    _avatar.Get<FaceTrackingDriver>().CalibrateGaze();
                    break;
                case ResetAction.Expression:
                    // 表情をニュートラルへ戻し、手動の固定も外す
                    _avatar.Get<ExpressionController>().ResetToNeutral();
                    break;
                case ResetAction.LookAtCamera:
                    // 目を画面に向けるかを切り替える
                    _settings.trackingLookAtCamera = !_settings.trackingLookAtCamera;
                    break;
                case ResetAction.Screenshot:
                    // 背景を透過した PNG を保存先へ書く（結果は出力タブとログに出る）
                    _screenshot.Capture();
                    break;
                case ResetAction.Away:
                    // 顔・手・体のトラッキングを止めるか、戻す
                    _settings.trackingAway = !_settings.trackingAway;
                    break;
                default:
                    // カメラを正面の既定位置へ
                    _orbit.ResetView();
                    break;
            }
        }
    }
}
