using UnityEngine;
using VRCast.Cameras;
using VRCast.Core;
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

            // 透過時はウィンドウ上だけの色、非透過時は映る背景色（値が変わったときだけ反映）
            if (_rendering.TransparentBackground)
            {
                GuiControls.Hint(Loc.T("Window color (shown only in this window, not captured by OBS)",
                    "ウィンドウの色（この画面だけの表示で、OBS には映りません）"));
                Color color = ColorSliders(_rendering.PreviewColor);
                if (color != _rendering.PreviewColor)
                {
                    _rendering.PreviewColor = color;
                }
            }
            else
            {
                Color color = ColorSliders(_rendering.BackgroundColor);
                if (color != _rendering.BackgroundColor)
                {
                    _rendering.BackgroundColor = color;
                }
            }

            GuiControls.EndCard();
        }

        private static Color ColorSliders(Color color)
        {
            // RGB スライダー
            color.r = GuiControls.Slider(Loc.T("Red", "赤"), color.r, 0f, 1f);
            color.g = GuiControls.Slider(Loc.T("Green", "緑"), color.g, 0f, 1f);
            color.b = GuiControls.Slider(Loc.T("Blue", "青"), color.b, 0f, 1f);
            return color;
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
            GuiControls.Hint(Loc.T("Sunlight shines from one direction; ambient light brightens the whole avatar evenly",
                "太陽光は一方向から当たる光、環境光はアバター全体を均一に明るくする光です"));

            // プリセット（太陽光と環境光をまとめて設定）
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Sunny", "晴れ")))
            {
                _rendering.ApplyPreset(LightingPreset.Sunny);
            }

            if (GUILayout.Button(Loc.T("Soft", "やわらか")))
            {
                _rendering.ApplyPreset(LightingPreset.Soft);
            }

            if (GUILayout.Button(Loc.T("Default", "既定")))
            {
                _rendering.ApplyPreset(LightingPreset.Default);
            }

            GUILayout.EndHorizontal();

            // 値が変わったときだけ反映
            float ambient = GuiControls.Slider(
                Loc.T("Ambient", "環境光"), _rendering.AmbientIntensity, 0f, AppSettings.MaxAmbientIntensity);
            if (!Mathf.Approximately(ambient, _rendering.AmbientIntensity))
            {
                _rendering.AmbientIntensity = ambient;
            }

            float intensity = GuiControls.Slider(Loc.T("Sunlight", "太陽光"), _rendering.LightIntensity, 0f, 3f);
            if (!Mathf.Approximately(intensity, _rendering.LightIntensity))
            {
                _rendering.LightIntensity = intensity;
            }

            // 色温度（左 = 夕日のような暖色、右 = 青白い光）。数値は K（100K 刻み）
            float temperature = GuiControls.Slider(Loc.T("Sun color (K)", "太陽光の色 (K)"), _rendering.LightTemperature,
                AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature, "F0");
            if (!Mathf.Approximately(temperature, _rendering.LightTemperature))
            {
                _rendering.LightTemperature = Mathf.Round(temperature / 100f) * 100f;
            }

            float yaw = GuiControls.Slider(Loc.T("Direction", "向き"), _rendering.LightYaw, -180f, 180f, "F0");
            if (!Mathf.Approximately(yaw, _rendering.LightYaw))
            {
                _rendering.LightYaw = yaw;
            }

            float pitch = GuiControls.Slider(Loc.T("Height", "高さ"), _rendering.LightPitch, -90f, 90f, "F0");
            if (!Mathf.Approximately(pitch, _rendering.LightPitch))
            {
                _rendering.LightPitch = pitch;
            }

            GuiControls.EndCard();
        }
    }
}
