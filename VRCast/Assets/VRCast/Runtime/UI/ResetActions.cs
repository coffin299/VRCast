using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// リセット（顔の向き・視線・表情・カメラ）の実行と表示名。パネル下部のボタンとショートカットキーで共用する。
    /// </summary>
    public sealed class ResetActions
    {
        private readonly AvatarComponentCache _avatar;
        private readonly OrbitCameraController _orbit;

        public ResetActions(AvatarSession session, OrbitCameraController orbit)
        {
            _avatar = new AvatarComponentCache(session);
            _orbit = orbit;
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
                case ResetAction.Gaze:
                    var face = _avatar.Get<FaceTrackingDriver>();
                    return face != null && face.IsTracking;
                case ResetAction.Expression:
                    var expressions = _avatar.Get<ExpressionController>();
                    return expressions != null && (expressions.Current >= 0 || expressions.IsManual);
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
                    // 顔の向き・上半身・視線を今の向きで正面にする
                    _avatar.Get<FaceTrackingDriver>().Calibrate();
                    break;
                case ResetAction.Gaze:
                    // 視線だけを正面にする
                    _avatar.Get<FaceTrackingDriver>().CalibrateGaze();
                    break;
                case ResetAction.Expression:
                    // 表情をニュートラルへ戻し、手動の固定も外す
                    _avatar.Get<ExpressionController>().ResetToNeutral();
                    break;
                default:
                    // カメラを正面の既定位置へ
                    _orbit.ResetView();
                    break;
            }
        }
    }
}
