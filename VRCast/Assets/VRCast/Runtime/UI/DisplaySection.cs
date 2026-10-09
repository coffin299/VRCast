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

        // 太陽光スライダーの上限
        private const float MaxSunlight = 4f;

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
        private readonly AppSettings _settings;

        public DisplaySection(OrbitCameraController orbit, RenderingController rendering, AppSettings settings)
        {
            _orbit = orbit;
            _rendering = rendering;
            _settings = settings;
        }

        public void Draw()
        {
            DrawCamera();
            DrawBackground();
            DrawResolution();
            DrawLight();
            DrawOutline();
        }

        private void DrawCamera()
        {
            GuiControls.BeginCard(Loc.T("Camera", "カメラ", "카메라", "相机", "相機"));
            GuiControls.Hint(Loc.T("Right drag: rotate / Middle drag: pan / Wheel: zoom",
                "右ドラッグ: 回転 / 中ドラッグ: 移動 / ホイール: ズーム",
                "오른쪽 드래그: 회전 / 가운데 드래그: 이동 / 휠: 확대·축소",
                "右键拖动：旋转 / 中键拖动：平移 / 滚轮：缩放",
                "右鍵拖曳：旋轉 / 中鍵拖曳：平移 / 滾輪：縮放"));
            GuiControls.Hint(Loc.T(
                "The camera position and field of view are remembered for each avatar, also after restarting.",
                "カメラの位置と画角はアバターごとに記憶され、再起動後も戻ります。",
                "카메라 위치와 화각은 아바타마다 기억되며, 다시 시작한 후에도 유지됩니다.",
                "相机位置和视野会按虚拟形象分别记住，重启后也会恢复。",
                "相機位置和視野會依虛擬形象分別記住，重新啟動後也會恢復。"));

            // 位置を決めた後にマウス操作で動かしてしまわないよう固定する（画角とリセットは固定中も使える）
            _settings.cameraLocked = GUILayout.Toggle(_settings.cameraLocked,
                Loc.T("Lock camera (ignore mouse drag and wheel)", "カメラを固定（マウスのドラッグ・ホイールで動かさない）",
                    "카메라 고정 (마우스 드래그·휠로 움직이지 않음)", "锁定相机（不响应鼠标拖动和滚轮）",
                    "鎖定相機（不回應滑鼠拖曳和滾輪）"));
            if (_settings.cameraLocked)
            {
                GuiControls.Hint(Loc.T("Field of view and Reset camera still work while locked.",
                    "固定中も画角の変更とカメラのリセットは使えます。",
                    "고정 중에도 화각 변경과 카메라 초기화는 사용할 수 있습니다.",
                    "锁定时仍可更改视野和重置相机。",
                    "鎖定時仍可變更視野和重設相機。"));
            }

            _orbit.FieldOfView = GuiControls.Slider(
                Loc.T("Field of view", "画角", "화각", "视野", "視野"), _orbit.FieldOfView, MinFov, MaxFov);

            // 視点リセット
            if (GUILayout.Button(Loc.T("Reset camera", "カメラをリセット", "카메라 초기화", "重置相机", "重設相機")))
            {
                _orbit.ResetView();
            }

            GuiControls.EndCard();
        }

        private void DrawBackground()
        {
            GuiControls.BeginCard(Loc.T("Background", "背景", "배경", "背景", "背景"));

            // 透過切替（OBS はゲームキャプチャ +「透過を許可」で取り込む）
            _rendering.TransparentBackground = GUILayout.Toggle(
                _rendering.TransparentBackground,
                Loc.T("Transparent (OBS Game Capture)", "透過（OBS ゲームキャプチャ）", "투명 (OBS 게임 캡처)",
                    "透明（OBS 游戏采集）", "透明（OBS 遊戲擷取）"));

            // 透過時は色がウィンドウ上だけに見えることを案内
            GuiControls.Hint(_rendering.TransparentBackground
                ? Loc.T("Background color (shown only in this window, not captured by OBS)",
                    "背景色（透過中はこの画面だけの表示で、OBS には映りません）",
                    "배경색 (투명 중에는 이 창에만 보이고 OBS에는 표시되지 않습니다)",
                    "背景色（透明时仅在此窗口显示，不会出现在 OBS 中）",
                    "背景色（透明時僅在此視窗顯示，不會出現在 OBS 中）")
                : Loc.T("Background color", "背景色", "배경색", "背景色", "背景色"));

            // RGB スライダー（値が変わったときだけ反映）
            Color color = _rendering.BackgroundColor;
            color.r = GuiControls.Slider(Loc.T("Red", "赤", "빨강", "红", "紅"), color.r, 0f, 1f);
            color.g = GuiControls.Slider(Loc.T("Green", "緑", "초록", "绿", "綠"), color.g, 0f, 1f);
            color.b = GuiControls.Slider(Loc.T("Blue", "青", "파랑", "蓝", "藍"), color.b, 0f, 1f);
            if (color != _rendering.BackgroundColor)
            {
                _rendering.BackgroundColor = color;
            }

            // 現在のテーマの既定色に戻す（ライト = ベージュ、ダーク = 暗い灰色）
            string defaultLabel = _rendering.DarkMode
                ? Loc.T("Dark gray (default)", "ダークグレー（既定）", "다크 그레이 (기본)", "深灰色（默认）", "深灰色（預設）")
                : Loc.T("Beige (default)", "ベージュ（既定）", "베이지 (기본)", "米色（默认）", "米色（預設）");
            if (GUILayout.Button(defaultLabel))
            {
                _rendering.BackgroundColor = _rendering.DefaultBackgroundColor;
            }

            GuiControls.EndCard();
        }

        private void DrawResolution()
        {
            GuiControls.BeginCard(Loc.T("Resolution", "解像度", "해상도", "分辨率", "解析度"));
            GuiControls.Hint(Loc.T("Current", "現在", "현재", "当前", "目前") + $": {_rendering.Width} x {_rendering.Height}");

            // プリセットボタン（2 列）
            for (int i = 0; i < ResolutionPresets.Length; i++)
            {
                // 行の開始
                if (i % 2 == 0)
                {
                    GUILayout.BeginHorizontal();
                }

                Vector2Int preset = ResolutionPresets[i];
                if (GUILayout.Button($"{preset.x} x {preset.y}", GuiControls.Shrinkable))
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
            GuiControls.BeginCard(Loc.T("Light", "ライト", "조명", "灯光", "燈光"));
            GuiControls.Hint(Loc.T("Sunlight shines from one direction; ambient light brightens the whole avatar evenly",
                "太陽光は一方向から当たる光、環境光はアバター全体を均一に明るくする光です",
                "태양광은 한 방향에서 비추는 빛, 환경광은 아바타 전체를 고르게 밝히는 빛입니다",
                "阳光从一个方向照射，环境光会均匀照亮整个虚拟形象",
                "陽光從一個方向照射，環境光會均勻照亮整個虛擬形象"));
            GuiControls.Hint(Loc.T("Light and avatar brightness are remembered for each avatar.",
                "ライトとアバターの明るさはアバターごとに記憶されます。",
                "조명과 아바타 밝기는 아바타마다 기억됩니다.",
                "灯光和虚拟形象亮度会按虚拟形象分别记住。",
                "燈光和虛擬形象亮度會依虛擬形象分別記住。"));

            // プリセット（太陽光と環境光をまとめて設定）
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Sunny", "晴れ", "맑음", "晴天", "晴天"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Sunny);
            }

            if (GUILayout.Button(Loc.T("Soft", "やわらか", "부드럽게", "柔和", "柔和"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Soft);
            }

            if (GUILayout.Button(Loc.T("Default", "既定", "기본", "默认", "預設"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Default);
            }

            GUILayout.EndHorizontal();

            // アバターの明るさ（ライトを強くしても明るくならないシェーダー向け。1 = マテリアルのまま）
            float brightness = GuiControls.Slider(Loc.T("Avatar brightness", "アバターの明るさ", "아바타 밝기", "虚拟形象亮度", "虛擬形象亮度"),
                _rendering.AvatarBrightness, AppSettings.MinAvatarBrightness, AppSettings.MaxAvatarBrightness);
            if (!Mathf.Approximately(brightness, _rendering.AvatarBrightness))
            {
                _rendering.AvatarBrightness = brightness;
            }

            GuiControls.Hint(Loc.T("If more light does not help (lilToon etc. cap brightness), raise Avatar brightness",
                "ライトを強くしても明るくならないとき（lilToon 等は明るさに上限あり）はアバターの明るさを上げてください",
                "조명을 강하게 해도 밝아지지 않을 때 (lilToon 등은 밝기 상한이 있음) 아바타 밝기를 올리세요",
                "增强灯光仍不变亮时（lilToon 等有亮度上限），请调高虚拟形象亮度",
                "增強燈光仍不變亮時（lilToon 等有亮度上限），請調高虛擬形象亮度"));

            // 値が変わったときだけ反映
            float ambient = GuiControls.Slider(
                Loc.T("Ambient", "環境光", "환경광", "环境光", "環境光"), _rendering.AmbientIntensity, 0f, AppSettings.MaxAmbientIntensity);
            if (!Mathf.Approximately(ambient, _rendering.AmbientIntensity))
            {
                _rendering.AmbientIntensity = ambient;
            }

            float intensity = GuiControls.Slider(
                Loc.T("Sunlight", "太陽光", "태양광", "阳光", "陽光"), _rendering.LightIntensity, 0f, MaxSunlight);
            if (!Mathf.Approximately(intensity, _rendering.LightIntensity))
            {
                _rendering.LightIntensity = intensity;
            }

            // 色温度（左 = 夕日のような暖色、右 = 青白い光）。数値は K（100K 刻み）
            float temperature = GuiControls.Slider(Loc.T("Sun color (K)", "太陽光の色 (K)", "태양광 색 (K)", "阳光颜色 (K)", "陽光顏色 (K)"),
                _rendering.LightTemperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature, "F0");
            if (!Mathf.Approximately(temperature, _rendering.LightTemperature))
            {
                _rendering.LightTemperature = Mathf.Round(temperature / 100f) * 100f;
            }

            // 向きは正面（カメラ側）からの角度、0 で正面から当たる
            float yaw = GuiControls.Slider(
                Loc.T("Direction", "向き", "방향", "方向", "方向"), _rendering.LightYaw, -180f, 180f, "F0");
            if (!Mathf.Approximately(yaw, _rendering.LightYaw))
            {
                _rendering.LightYaw = yaw;
            }

            float pitch = GuiControls.Slider(
                Loc.T("Height", "高さ", "높이", "高度", "高度"), _rendering.LightPitch, -90f, 90f, "F0");
            if (!Mathf.Approximately(pitch, _rendering.LightPitch))
            {
                _rendering.LightPitch = pitch;
            }

            GuiControls.EndCard();
        }

        private void DrawOutline()
        {
            GuiControls.BeginCard(Loc.T("Outline", "輪郭線", "윤곽선", "轮廓线", "輪廓線"));
            GuiControls.Hint(Loc.T(
                "Scales the avatar's outline (lilToon / MToon / Poiyomi / UTS). 1 = as set in the material, 0 = no outline. Remembered for each avatar.",
                "アバターの輪郭線（lilToon / MToon / Poiyomi / UTS）の太さを変えます。1 = マテリアルのまま、0 = 輪郭線なし。アバターごとに記憶されます。",
                "아바타 윤곽선 (lilToon / MToon / Poiyomi / UTS)의 굵기를 바꿉니다. 1 = 머티리얼 그대로, 0 = 윤곽선 없음. 아바타마다 기억됩니다.",
                "调整虚拟形象轮廓线（lilToon / MToon / Poiyomi / UTS）的粗细。1 = 保持材质设置，0 = 无轮廓线。按虚拟形象分别记住。",
                "調整虛擬形象輪廓線（lilToon / MToon / Poiyomi / UTS）的粗細。1 = 保持材質設定，0 = 無輪廓線。依虛擬形象分別記住。"));

            // 太さの倍率（値が変わったときだけ反映）
            float width = GuiControls.Slider(Loc.T("Thickness", "太さ", "굵기", "粗细", "粗細"),
                _rendering.OutlineWidth, 0f, AppSettings.MaxOutlineWidth);
            if (!Mathf.Approximately(width, _rendering.OutlineWidth))
            {
                _rendering.OutlineWidth = width;
            }

            // マテリアルのままの太さに戻す
            if (GUILayout.Button(Loc.T("As in material (1)", "マテリアルのまま（1）", "머티리얼 그대로 (1)", "保持材质（1）", "保持材質（1）")))
            {
                _rendering.OutlineWidth = 1f;
            }

            // 輪郭線の無いアバター（または未対応のシェーダー）ではスライダーが効かないことを案内
            if (!_rendering.HasAvatarOutline)
            {
                GuiControls.Hint(Loc.T("This avatar has no outline this slider can change.",
                    "このアバターには、ここで太さを変えられる輪郭線がありません。",
                    "이 아바타에는 여기서 굵기를 바꿀 수 있는 윤곽선이 없습니다.",
                    "此虚拟形象没有可在此调整粗细的轮廓线。",
                    "此虛擬形象沒有可在此調整粗細的輪廓線。"));
            }

            GuiControls.EndCard();
        }
    }
}
