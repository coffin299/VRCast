using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パネル下部に常に表示するリセットボタン（顔の向き・視線・表情・カメラ）。
    /// 配信中によく使う操作を、どのタブを開いていても押せるようにする。
    /// </summary>
    public class ResetBar
    {
        private readonly AvatarComponentCache _avatar;
        private readonly OrbitCameraController _orbit;

        public ResetBar(AvatarSession session, OrbitCameraController orbit)
        {
            _avatar = new AvatarComponentCache(session);
            _orbit = orbit;
        }

        public void Draw(GUIStyle style)
        {
            _avatar.Refresh();
            GUILayout.BeginHorizontal(style);
            GUILayout.Label(Loc.T("Reset", "リセット", "초기화", "重置", "重設"), UiTheme.Current.Hint,
                GUILayout.ExpandWidth(false));

            // 顔の向き・上半身・視線を今の向きで正面にする（トラッキング受信中のみ）
            var face = _avatar.Get<FaceTrackingDriver>();
            bool tracking = face != null && face.IsTracking;
            GUI.enabled = tracking;
            if (GUILayout.Button(Loc.T("Head", "顔の向き", "얼굴 방향", "头部朝向", "頭部朝向")))
            {
                face.Calibrate();
            }

            // 視線だけを正面にする
            if (GUILayout.Button(Loc.T("Gaze", "視線", "시선", "视线", "視線")))
            {
                face.CalibrateGaze();
            }

            // 表情をニュートラルへ戻し、手動の固定も外す（表情データがあるときのみ）
            var expressions = _avatar.Get<ExpressionController>();
            GUI.enabled = expressions != null && (expressions.Current >= 0 || expressions.IsManual);
            if (GUILayout.Button(Loc.T("Expression", "表情", "표정", "表情", "表情")))
            {
                expressions.ResetToNeutral();
            }

            // カメラを正面の既定位置へ
            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("Camera", "カメラ", "카메라", "相机", "相機")))
            {
                _orbit.ResetView();
            }

            GUILayout.EndHorizontal();
        }
    }
}
