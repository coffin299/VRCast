using UnityEngine;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// MainPanel 内の Rendering セクション（背景・解像度・ライト）。
    /// </summary>
    public class RenderingSection
    {
        // 解像度プリセット（横長 / 縦長配信）
        private static readonly Vector2Int[] ResolutionPresets =
        {
            new Vector2Int(1280, 720),
            new Vector2Int(1920, 1080),
            new Vector2Int(720, 1280),
            new Vector2Int(1080, 1920),
        };

        private readonly RenderingController _rendering;

        public RenderingSection(RenderingController rendering)
        {
            _rendering = rendering;
        }

        public void Draw()
        {
            GUILayout.Label("Rendering");
            DrawBackground();
            DrawResolution();
            DrawLight();
        }

        private void DrawBackground()
        {
            // 透過切替（OBS はゲームキャプチャ +「透過を許可」で取り込む）
            _rendering.TransparentBackground = GUILayout.Toggle(
                _rendering.TransparentBackground, " Transparent background (OBS Game Capture)");

            // 透過時は色設定を出さない
            if (_rendering.TransparentBackground)
            {
                return;
            }

            // RGB スライダーで単色背景を設定
            Color color = _rendering.BackgroundColor;
            color.r = GuiControls.Slider("BG R", color.r, 0f, 1f);
            color.g = GuiControls.Slider("BG G", color.g, 0f, 1f);
            color.b = GuiControls.Slider("BG B", color.b, 0f, 1f);
            if (color != _rendering.BackgroundColor)
            {
                _rendering.BackgroundColor = color;
            }
        }

        private void DrawResolution()
        {
            // 現在のウィンドウサイズ
            GUILayout.Label($"Resolution: {_rendering.Width} x {_rendering.Height}");

            // プリセットボタン
            GUILayout.BeginHorizontal();
            foreach (Vector2Int preset in ResolutionPresets)
            {
                if (GUILayout.Button($"{preset.x}x{preset.y}"))
                {
                    _rendering.SetResolution(preset.x, preset.y);
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawLight()
        {
            // 値が変わったときだけ反映
            float intensity = GuiControls.Slider("Light", _rendering.LightIntensity, 0f, 3f);
            if (!Mathf.Approximately(intensity, _rendering.LightIntensity))
            {
                _rendering.LightIntensity = intensity;
            }

            float yaw = GuiControls.Slider("Light Yaw", _rendering.LightYaw, -180f, 180f);
            if (!Mathf.Approximately(yaw, _rendering.LightYaw))
            {
                _rendering.LightYaw = yaw;
            }

            float pitch = GuiControls.Slider("Light Pitch", _rendering.LightPitch, -90f, 90f);
            if (!Mathf.Approximately(pitch, _rendering.LightPitch))
            {
                _rendering.LightPitch = pitch;
            }
        }
    }
}
