using UnityEngine;
using VRCast.Cameras;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// Display タブ（カメラの画角・リセット、背景、解像度、ライト）。
    /// </summary>
    public class DisplaySection
    {
        // FOV スライダーの範囲
        private const float MinFov = 10f;
        private const float MaxFov = 90f;

        // 解像度プリセット（横長 / 縦長配信）
        private static readonly Vector2Int[] ResolutionPresets =
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1920, 1080),
            new Vector2Int(720, 1280),
            new Vector2Int(1080, 1920),
        };

        private readonly OrbitCameraController _orbit;
        private readonly RenderingController _rendering;

        public DisplaySection(OrbitCameraController orbit, RenderingController rendering)
        {
            _orbit = orbit;
            _rendering = rendering;
        }

        public void Draw()
        {
            DrawCamera();
            DrawBackground();
            DrawResolution();
            DrawLight();
        }

        private void DrawCamera()
        {
            GuiControls.BeginCard(Loc.T("Camera", "カメラ"));
            GuiControls.Hint(Loc.T("Right drag: rotate / Middle drag: pan / Wheel: zoom",
                "右ドラッグ: 回転 / 中ドラッグ: 移動 / ホイール: ズーム"));
            _orbit.FieldOfView = GuiControls.Slider(Loc.T("Field of view", "画角"), _orbit.FieldOfView, MinFov, MaxFov);

            // 視点リセット
            if (GUILayout.Button(Loc.T("Reset camera", "カメラをリセット")))
            {
                _orbit.ResetView();
            }

            GuiControls.EndCard();
        }

        private void DrawBackground()
        {
            GuiControls.BeginCard(Loc.T("Background", "背景"));

            // 透過切替（OBS はゲームキャプチャ +「透過を許可」で取り込む）
            _rendering.TransparentBackground = GUILayout.Toggle(
                _rendering.TransparentBackground, Loc.T("Transparent (OBS Game Capture)", "透過（OBS ゲームキャプチャ）"));

            // 透過時は色設定を出さない
            if (!_rendering.TransparentBackground)
            {
                // RGB スライダーで単色背景を設定（値が変わったときだけ反映）
                Color color = _rendering.BackgroundColor;
                color.r = GuiControls.Slider(Loc.T("Red", "赤"), color.r, 0f, 1f);
                color.g = GuiControls.Slider(Loc.T("Green", "緑"), color.g, 0f, 1f);
                color.b = GuiControls.Slider(Loc.T("Blue", "青"), color.b, 0f, 1f);
                if (color != _rendering.BackgroundColor)
                {
                    _rendering.BackgroundColor = color;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawResolution()
        {
            GuiControls.BeginCard(Loc.T("Resolution", "解像度"));
            GuiControls.Hint(Loc.T("Current", "現在") + $": {_rendering.Width} x {_rendering.Height}");

            // プリセットボタン（2 列）
            for (int i = 0; i < ResolutionPresets.Length; i++)
            {
                // 行の開始
                if (i % 2 == 0)
                {
                    GUILayout.BeginHorizontal();
                }

                Vector2Int preset = ResolutionPresets[i];
                if (GUILayout.Button($"{preset.x} x {preset.y}"))
                {
                    _rendering.SetResolution(preset.x, preset.y);
                }

                // 行の終了（最終要素でも閉じる）
                if (i % 2 == 1 || i == ResolutionPresets.Length - 1)
                {
                    GUILayout.EndHorizontal();
                }
            }

            GuiControls.EndCard();
        }

        private void DrawLight()
        {
            GuiControls.BeginCard(Loc.T("Light", "ライト"));

            // 値が変わったときだけ反映
            float intensity = GuiControls.Slider(Loc.T("Intensity", "明るさ"), _rendering.LightIntensity, 0f, 3f);
            if (!Mathf.Approximately(intensity, _rendering.LightIntensity))
            {
                _rendering.LightIntensity = intensity;
            }

            float yaw = GuiControls.Slider(Loc.T("Direction", "向き"), _rendering.LightYaw, -180f, 180f);
            if (!Mathf.Approximately(yaw, _rendering.LightYaw))
            {
                _rendering.LightYaw = yaw;
            }

            float pitch = GuiControls.Slider(Loc.T("Height", "高さ"), _rendering.LightPitch, -90f, 90f);
            if (!Mathf.Approximately(pitch, _rendering.LightPitch))
            {
                _rendering.LightPitch = pitch;
            }

            GuiControls.EndCard();
        }
    }
}
