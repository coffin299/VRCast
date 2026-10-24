using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// グラフィックタブ（背景、レガシー／最新ライティング、輪郭線）。数値はスライダーの横の欄へ直接入れられる。
    /// </summary>
    public class GraphicsSection
    {
        private readonly RenderingController _rendering;
        private readonly AppSettings _settings;
        private readonly Action _reapply;
        private readonly GraphicsPresetSection _presets;

        // 既定に戻すの二段階確認
        private bool _confirmingReset;

        // 利用者ライティングプリセットの名前入力
        private string _lightingPresetName = string.Empty;

        // 数値欄の入力途中（id → 文字列）。スライダーと欄で同じ値を編集する
        private readonly Dictionary<string, string> _numberInputs = new Dictionary<string, string>();

        public GraphicsSection(RenderingController rendering, AppSettings settings, Action reapply)
        {
            _rendering = rendering;
            _settings = settings;
            _reapply = reapply;
            _presets = new GraphicsPresetSection(settings, reapply);
        }

        /// <summary>
        /// スライダーと、同じ値を打ち込める欄。
        /// </summary>
        private float Field(string id, string label, float value, float min, float max, string format = "F2")
        {
            _numberInputs.TryGetValue(id, out string input);
            float result = GuiControls.SliderOrField(label, value, min, max, ref input, "gfx-" + id, format);
            _numberInputs[id] = input ?? string.Empty;
            // 色温度など整数表示の項目は、欄の数字と保存値がずれないよう整数にそろえる
            if (format == "F0")
            {
                result = Mathf.Round(result);
            }

            return Mathf.Clamp(result, min, max);
        }

        public void Draw()
        {
            DrawBackground();
            DrawAntiAliasing();
            DrawLightingSystem();
            if (_rendering.IsModern)
            {
                DrawModernLightingPresets();
                DrawModernLight();
                DrawShadows();
                DrawSsao();
                DrawScreenRim();
                DrawLook();
                DrawColorGrade();
                DrawDetailFx();
            }
            else
            {
                DrawLegacyLight();
            }

            DrawOutline();
            DrawResetDefaults();
            _presets.Draw();
        }

        private void DrawBackground()
        {
            GuiControls.BeginCard(Loc.T("Background", "背景", "배경", "背景", "背景"));

            // パネルのライト／ダーク（設定タブと同じ。既定の背景色のときはテーマに合わせて色も変わる）
            string[] themeLabels =
            {
                Loc.T("Light", "ライト", "라이트", "浅色", "淺色"),
                Loc.T("Dark", "ダーク", "다크", "深色", "深色"),
            };
            int theme = _rendering.DarkMode ? 1 : 0;
            int selectedTheme = GuiControls.LabeledChoice(
                Loc.T("Theme", "テーマ", "테마", "主题", "主題"), themeLabels, theme);
            if (selectedTheme != theme)
            {
                _rendering.DarkMode = selectedTheme == 1;
            }

            GuiControls.Hint(Loc.T(
                "Panel colors. If the background is still the default, it follows the theme.",
                "操作パネルの配色です。背景色が既定のままなら、テーマに合わせて切り替わります。",
                "조작 패널 배색입니다. 배경색이 기본값이면 테마에 맞춰 바뀝니다.",
                "操作面板配色。若背景色仍为默认，会随主题切换。",
                "操作面板配色。若背景色仍為預設，會隨主題切換。"));

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

            // RGB（スライダーか、欄への数値入力。値が変わったときだけ反映）
            Color color = _rendering.BackgroundColor;
            color.r = Field("bg-r", Loc.T("Red", "赤", "빨강", "红", "紅"), color.r, 0f, 1f);
            color.g = Field("bg-g", Loc.T("Green", "緑", "초록", "绿", "綠"), color.g, 0f, 1f);
            color.b = Field("bg-b", Loc.T("Blue", "青", "파랑", "蓝", "藍"), color.b, 0f, 1f);
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

        private void DrawAntiAliasing()
        {
            GuiControls.BeginCard(Loc.T("Anti-aliasing", "アンチエイリアス", "안티앨리어싱", "抗锯齿", "抗鋸齒"));
            GuiControls.Hint(Loc.T(
                "MSAA keeps edges sharp. FXAA is lighter but can blur. Can be combined with supersampling below.",
                "MSAA は輪郭をシャープに保ちます。FXAA は軽いですがぼやけやすいです。下の高解像度描画と併用できます。",
                "MSAA는 윤곽을 선명하게 유지합니다. FXAA는 가볍지만 흐려질 수 있습니다. 아래 고해상도와 함께 쓸 수 있습니다.",
                "MSAA 保持边缘清晰。FXAA 更轻但可能发糊。可与下方的高分辨率绘制并用。",
                "MSAA 保持邊緣清晰。FXAA 較輕但可能發糊。可與下方的高解析度繪製並用。"));

            AntiAliasingMode[] modes =
            {
                AntiAliasingMode.Off, AntiAliasingMode.Msaa, AntiAliasingMode.Fxaa,
            };
            string[] modeLabels =
            {
                Loc.T("Off", "オフ", "끄기", "关", "關"),
                "MSAA",
                "FXAA",
            };
            AntiAliasingMode currentMode = _rendering.AntiAliasingMode;
            if (currentMode == AntiAliasingMode.Unspecified || currentMode == AntiAliasingMode.Supersample)
            {
                currentMode = AntiAliasingMode.Msaa;
            }

            int modeIndex = IndexOf(modes, currentMode);
            GUILayout.Label(Loc.T("Method", "方式", "방식", "方式", "方式"));
            int modeSelected = DrawChoiceGrid(modeLabels, modeIndex, 3);
            if (modeSelected != modeIndex)
            {
                _rendering.AntiAliasingMode = modes[modeSelected];
                if (_rendering.AntiAliasingMode == AntiAliasingMode.Msaa
                    && _rendering.AntiAliasing == AntiAliasingQuality.Off)
                {
                    _rendering.AntiAliasing = AntiAliasingQuality.x4;
                }
            }

            if (_rendering.AntiAliasingMode == AntiAliasingMode.Msaa)
            {
                AntiAliasingQuality[] qualities =
                {
                    AntiAliasingQuality.x2, AntiAliasingQuality.x4, AntiAliasingQuality.x8,
                };
                AntiAliasingQuality current = _rendering.AntiAliasing == AntiAliasingQuality.Off
                    ? AntiAliasingQuality.x4
                    : _rendering.AntiAliasing;
                int qualityIndex = IndexOf(qualities, current);
                GUILayout.Label(Loc.T("MSAA quality", "MSAA の品質", "MSAA 품질", "MSAA 质量", "MSAA 品質"));
                int qualitySelected = DrawChoiceGrid(new[] { "2x", "4x", "8x" }, qualityIndex, 3);
                if (qualitySelected != qualityIndex)
                {
                    _rendering.AntiAliasing = qualities[qualitySelected];
                }
            }
            else if (_rendering.AntiAliasingMode == AntiAliasingMode.Fxaa)
            {
                GuiControls.Hint(Loc.T(
                    "FXAA smooths the whole image and may soften fine outlines.",
                    "FXAA は画面全体をならすため、細い輪郭がぼやけることがあります。",
                    "FXAA는 화면 전체를 부드럽게 해서 가는 윤곽이 흐려질 수 있습니다.",
                    "FXAA 会平滑整幅画面，细轮廓可能发糊。",
                    "FXAA 會平滑整幅畫面，細輪廓可能發糊。"));
            }

            GuiControls.EndCard();

            GuiControls.BeginCard(Loc.T("Supersampling", "高解像度描画", "고해상도 렌더", "高分辨率绘制", "高解析度繪製"));
            GuiControls.Hint(Loc.T(
                "Renders the avatar larger than the window, then scales down. Reduces jaggies; uses more GPU. Works with MSAA or FXAA.",
                "ウィンドウより高い解像度で描いてから縮小します。ジャギーを抑え、GPU 負荷は増えます。MSAA / FXAA と併用できます。",
                "창보다 높은 해상도로 그린 뒤 줄입니다. 톱니를 줄이고 GPU 부하는 늘습니다. MSAA/FXAA와 함께 쓸 수 있습니다.",
                "以高于窗口的分辨率绘制后再缩小。减轻锯齿，更耗 GPU。可与 MSAA / FXAA 并用。",
                "以高於視窗的解析度繪製後再縮小。減輕鋸齒，更耗 GPU。可與 MSAA / FXAA 並用。"));

            AntiAliasingQuality[] scales =
            {
                AntiAliasingQuality.Off, AntiAliasingQuality.x2, AntiAliasingQuality.x4, AntiAliasingQuality.x8,
            };
            string[] scaleLabels =
            {
                Loc.T("Off", "オフ", "끄기", "关", "關"),
                "125%", "150%", "200%",
            };
            int scaleIndex = IndexOf(scales, _rendering.Supersample);
            int scaleSelected = DrawChoiceGrid(scaleLabels, scaleIndex, 4);
            if (scaleSelected != scaleIndex)
            {
                _rendering.Supersample = scales[scaleSelected];
            }

            GuiControls.EndCard();
        }

        private static int IndexOf<T>(T[] values, T current)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (Equals(values[i], current))
                {
                    return i;
                }
            }

            return 0;
        }

        /// <summary>
        /// 選択ボタンを columns 列の格子で並べる（長いラベルでも幅を分け合って文字が潰れないようにする）。
        /// </summary>
        private static int DrawChoiceGrid(IReadOnlyList<string> labels, int selected, int columns)
        {
            columns = Mathf.Max(1, columns);
            for (int start = 0; start < labels.Count; start += columns)
            {
                GUILayout.BeginHorizontal();
                int end = Mathf.Min(start + columns, labels.Count);
                for (int i = start; i < end; i++)
                {
                    if (GUILayout.Toggle(selected == i, labels[i], GUI.skin.button,
                            GUILayout.ExpandWidth(true), GuiControls.Shrinkable)
                        && selected != i)
                    {
                        selected = i;
                    }
                }

                GUILayout.EndHorizontal();
            }

            return selected;
        }

        private void DrawLightingSystem()
        {
            GuiControls.BeginCard(Loc.T("Lighting", "ライティング", "라이팅", "光照", "光照"));
            GuiControls.Hint(Loc.T(
                "Legacy matches VRCast 2.x and earlier (default). Modern (3.x+) adds three lights, soft shadows, studio light, bloom and more.",
                "レガシーは VRCast 2.x 以前と同じ見た目です（既定）。最新（3.x 以降）は三点照明・柔らかい影・スタジオ環境光・ブルームなどを使います。",
                "레거시는 VRCast 2.x 이전과 같은 모습입니다(기본). 최신(3.x 이후)은 삼점 조명·부드러운 그림자·스튜디오 환경광·블룸 등을 씁니다.",
                "旧版与 VRCast 2.x 及更早相同（默认）。最新（3.x 起）使用三点照明、柔和阴影、工作室环境光、辉光等。",
                "舊版與 VRCast 2.x 及更早相同（預設）。最新（3.x 起）使用三點照明、柔和陰影、工作室環境光、輝光等。"));

            int selected = GuiControls.LabeledChoice(
                Loc.T("System", "系統", "계통", "系统", "系統"),
                new[]
                {
                    Loc.T("Legacy (≤2.x)", "レガシー（2.x 以前）", "레거시 (2.x 이전)", "旧版（2.x 以前）", "舊版（2.x 以前）"),
                    Loc.T("Modern (3.x+)", "最新（3.x 以降）", "최신 (3.x+)", "最新（3.x 起）", "最新（3.x 起）"),
                },
                (int)_rendering.LightingSystem);
            if (selected != (int)_rendering.LightingSystem)
            {
                _rendering.LightingSystem = (LightingSystem)selected;
            }

            GuiControls.EndCard();
        }

        private void DrawLegacyLight()
        {
            GuiControls.BeginCard(Loc.T("Light", "ライト", "조명", "灯光", "燈光"));
            GuiControls.Hint(Loc.T("Sunlight shines from one direction; ambient light brightens the whole avatar evenly",
                "太陽光は一方向から当たる光、環境光はアバター全体を均一に明るくする光です",
                "태양광은 한 방향에서 비추는 빛, 환경광은 아바타 전체를 고르게 밝히는 빛입니다",
                "阳光从一个方向照射，环境光会均匀照亮整个虚拟形象",
                "陽光從一個方向照射，環境光會均勻照亮整個虛擬形象"));
            DrawBrightnessHint();
            DrawLegacyPresets();
            DrawAvatarBrightness();
            DrawAmbientSlider();
            DrawKeyLightSliders(
                Loc.T("Sunlight", "太陽光", "태양광", "阳光", "陽光"),
                Loc.T("Sun color (K)", "太陽光の色 (K)", "태양광 색 (K)", "阳光颜色 (K)", "陽光顏色 (K)"));
            GuiControls.EndCard();
        }

        private void DrawModernLightingPresets()
        {
            GuiControls.BeginCard(Loc.T("Lighting presets", "ライティングプリセット", "라이팅 프리셋",
                "光照预设", "光照預設"));
            GuiControls.Hint(Loc.T(
                "Built-in looks: Legacy-like ≈ 2.x (no fill / IBL / FX), Default = clear shadows (v2). "
                + "Save current sliders under a name; export / import via Graphics preset.",
                "内蔵: レガシー寄り ≈ 2.x（フィル／IBL／FX なし）、既定＝影ver2。"
                + "今の値は名前付きで保存可。書き出し・読み込みは下のグラフィックプリセットから。",
                "내장: 레거시 비슷 ≈ 2.x(필/IBL/FX 없음), 기본＝그림자 ver2. "
                + "지금 값은 이름 붙여 저장 가능. 내보내기·불러오기는 아래 그래픽 프리셋에서.",
                "内置：旧版风 ≈ 2.x（无辅光 / IBL / 特效），默认＝阴影 ver2。"
                + "当前值可命名保存；导出 / 导入用下方图形预设。",
                "內建：舊版風 ≈ 2.x（無輔光／IBL／特效），預設＝陰影 ver2。"
                + "目前值可命名儲存；匯出／匯入用下方圖形預設。"));

            GuiControls.SubHeading(Loc.T("Built-in", "内蔵", "내장", "内置", "內建"));
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Legacy-like", "レガシー寄り", "레거시 비슷", "旧版风", "舊版風"),
                    GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.LegacyLike);
            }

            if (GUILayout.Button(Loc.T("Default", "既定", "기본", "默认", "預設"), GuiControls.Shrinkable))
            {
                _rendering.ApplyPreset(LightingPreset.Default);
            }

            GUILayout.EndHorizontal();

            GuiControls.SubHeading(Loc.T("Saved", "保存済み", "저장됨", "已保存", "已儲存"));
            DrawSavedLightingPresets();
            DrawSaveLightingPreset();
            GuiControls.EndCard();
        }

        private void DrawSavedLightingPresets()
        {
            List<LightingPresetEntry> presets = _settings.lightingPresets;
            if (presets == null || presets.Count == 0)
            {
                GuiControls.Hint(Loc.T(
                    "No saved looks yet.",
                    "まだ保存した見た目はありません。",
                    "아직 저장한 모습이 없습니다.",
                    "还没有保存的外观。",
                    "尚未有儲存的外觀。"));
                return;
            }

            for (int i = 0; i < presets.Count; i++)
            {
                LightingPresetEntry entry = presets[i];
                if (entry == null)
                {
                    continue;
                }

                int index = i;
                GUILayout.BeginHorizontal();
                GUILayout.Label(entry.name, GuiControls.Shrinkable);
                if (GUILayout.Button(Loc.T("Apply", "適用", "적용", "应用", "套用"), GUILayout.Width(72f)))
                {
                    if (_settings.ApplyLightingPreset(index))
                    {
                        _reapply();
                    }
                }

                if (GUILayout.Button(Loc.T("Delete", "削除", "삭제", "删除", "刪除"), GUILayout.Width(72f)))
                {
                    _settings.RemoveLightingPreset(index);
                    GUILayout.EndHorizontal();
                    break;
                }

                GUILayout.EndHorizontal();
            }
        }

        private void DrawSaveLightingPreset()
        {
            _lightingPresetName = GuiControls.TextField(
                Loc.T("Name", "名前", "이름", "名称", "名稱"),
                _lightingPresetName, LightingPresetEntry.MaxNameLength);

            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            bool canSave = !string.IsNullOrWhiteSpace(_lightingPresetName)
                && (_settings.lightingPresets == null
                    || _settings.lightingPresets.Count < AppSettings.MaxLightingPresets
                    || HasLightingPresetName(_lightingPresetName));
            GUI.enabled = canSave;
            if (GUILayout.Button(Loc.T("Save current", "今の値を保存", "지금 값 저장", "保存当前", "儲存目前"),
                    GuiControls.Shrinkable))
            {
                if (_settings.SaveLightingPreset(_lightingPresetName))
                {
                    _lightingPresetName = string.Empty;
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (_settings.lightingPresets != null
                && _settings.lightingPresets.Count >= AppSettings.MaxLightingPresets
                && !HasLightingPresetName(_lightingPresetName))
            {
                GuiControls.Hint(Loc.T(
                    "Saved look limit reached. Delete one, or overwrite an existing name.",
                    "保存の上限です。どれかを削除するか、同じ名前で上書きしてください。",
                    "저장 한도에 도달했습니다. 하나를 삭제하거나 같은 이름으로 덮어쓰세요.",
                    "已达保存上限。请删除一个，或用同名覆盖。",
                    "已達儲存上限。請刪除一個，或用同名覆寫。"));
            }
        }

        private bool HasLightingPresetName(string presetName)
        {
            string name = LightingPresetEntry.NormalizeName(presetName);
            if (string.IsNullOrEmpty(name) || _settings.lightingPresets == null)
            {
                return false;
            }

            foreach (LightingPresetEntry entry in _settings.lightingPresets)
            {
                if (entry != null
                    && string.Equals(entry.name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawModernLight()
        {
            GuiControls.BeginCard(Loc.T("Lights", "ライト", "조명", "灯光", "燈光"));
            GuiControls.Hint(Loc.T(
                "Key is the main light, Fill softens shadows, Rim outlines the silhouette. Remembered per avatar.",
                "キーが主光、フィルが影を明るく、リムが輪郭を縁取る光です。アバターごとに記憶されます。",
                "키가 주광, 필이 그림자를 밝히고, 림이 윤곽을 강조합니다. 아바타마다 기억됩니다.",
                "主光、辅光柔化阴影、轮廓光勾边。按虚拟形象分别记住。",
                "主光、輔光柔化陰影、輪廓光勾邊。依虛擬形象分別記住。"));
            DrawBrightnessHint();
            DrawAvatarBrightness();
            DrawAmbientSlider();

            GuiControls.SubHeading(Loc.T("Key light", "キーライト", "키 라이트", "主光", "主光"));
            DrawKeyLightSliders(
                Loc.T("Intensity", "強さ", "세기", "强度", "強度"),
                Loc.T("Color (K)", "色 (K)", "색 (K)", "颜色 (K)", "顏色 (K)"));

            GuiControls.SubHeading(Loc.T("Fill light", "フィルライト", "필 라이트", "辅光", "輔光"));
            DrawFillSliders();

            GuiControls.SubHeading(Loc.T("Rim light", "リムライト", "림 라이트", "轮廓光", "輪廓光"));
            DrawRimSliders();

            GuiControls.SubHeading(Loc.T("Studio environment", "スタジオ環境光", "스튜디오 환경광", "工作室环境光", "工作室環境光"));
            DrawIblSliders();
            GuiControls.EndCard();
        }

        private void DrawShadows()
        {
            GuiControls.BeginCard(Loc.T("Shadows", "影", "그림자", "阴影", "陰影"));
            GuiControls.Hint(Loc.T(
                "Self-shadow density and softness (no floor; works with transparent backgrounds).",
                "アバター自身の影の濃さと柔らかさです。床は出さないため透過背景のまま使えます。",
                "아바타 자체 그림자의 농도와 부드러움입니다. 바닥은 그리지 않아 투명 배경에서도 쓸 수 있습니다.",
                "虚拟形象自身阴影的浓度与柔和度。不绘制地面，可与透明背景并用。",
                "虛擬形象自身陰影的濃度與柔和度。不繪製地面，可與透明背景並用。"));

            _rendering.SoftShadows = GUILayout.Toggle(_rendering.SoftShadows,
                Loc.T("Enable shadows", "影を付ける", "그림자 사용", "启用阴影", "啟用陰影"));
            if (_rendering.SoftShadows)
            {
                float strength = Field("shadow-strength",
                    Loc.T("Shadow density", "影の濃さ", "그림자 농도", "阴影浓度", "陰影濃度"),
                    _rendering.ShadowStrength, 0f, AppSettings.MaxShadowStrength);
                if (!Mathf.Approximately(strength, _rendering.ShadowStrength))
                {
                    _rendering.ShadowStrength = strength;
                }

                float softness = Field("shadow-softness",
                    Loc.T("Softness", "柔らかさ", "부드러움", "柔和度", "柔和度"),
                    _rendering.ShadowSoftness, 0f, AppSettings.MaxShadowSoftness);
                if (!Mathf.Approximately(softness, _rendering.ShadowSoftness))
                {
                    _rendering.ShadowSoftness = softness;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawSsao()
        {
            GuiControls.BeginCard(Loc.T("Crease shadow (SSAO)", "くぼみの影（SSAO）", "움푹한 그림자 (SSAO)",
                "凹陷阴影（SSAO）", "凹陷陰影（SSAO）"));
            GuiControls.Hint(Loc.T(
                "Darkens the neck, under the chin, clothing folds, etc. Alpha is preserved.",
                "首元・あごの下・服のしわなどを暗くします。透明度は保ちます。",
                "목·턱 아래·옷 주름 등을 어둡게 합니다. 알파는 유지합니다.",
                "让颈根、下巴下方、衣服褶皱等变暗。保留透明度。",
                "讓頸根、下巴下方、衣服褶皺等變暗。保留透明度。"));
            _rendering.SsaoEnabled = GUILayout.Toggle(_rendering.SsaoEnabled,
                Loc.T("Enable crease shadow", "くぼみの影を使う", "움푹한 그림자 사용", "启用凹陷阴影", "啟用凹陷陰影"));
            if (_rendering.SsaoEnabled)
            {
                float intensity = Field("ssao-intensity",
                    Loc.T("Density", "濃さ", "농도", "浓度", "濃度"),
                    _rendering.SsaoIntensity, 0f, AppSettings.MaxSsaoIntensity);
                if (!Mathf.Approximately(intensity, _rendering.SsaoIntensity))
                {
                    _rendering.SsaoIntensity = intensity;
                }

                float radius = Field("ssao-radius",
                    Loc.T("Spread", "広がり", "범위", "范围", "範圍"),
                    _rendering.SsaoRadius, 0f, AppSettings.MaxSsaoRadius);
                if (!Mathf.Approximately(radius, _rendering.SsaoRadius))
                {
                    _rendering.SsaoRadius = radius;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawScreenRim()
        {
            GuiControls.BeginCard(Loc.T("Screen rim", "画面リム", "화면 림", "屏幕轮廓光", "螢幕輪廓光"));
            GuiControls.Hint(Loc.T(
                "Edge highlight from the camera view (works with the Rim light above). Alpha is preserved.",
                "カメラから見た輪郭を光らせます（上のリムライトと併用可）。透明度は保ちます。",
                "카메라에서 본 윤곽을 빛나게 합니다(위 림 라이트와 함께 사용 가능). 알파는 유지합니다.",
                "从相机视角照亮轮廓（可与上方轮廓光并用）。保留透明度。",
                "從相機視角照亮輪廓（可與上方輪廓光並用）。保留透明度。"));
            _rendering.ScreenRimEnabled = GUILayout.Toggle(_rendering.ScreenRimEnabled,
                Loc.T("Enable screen rim", "画面リムを使う", "화면 림 사용", "启用屏幕轮廓光", "啟用螢幕輪廓光"));
            if (_rendering.ScreenRimEnabled)
            {
                float intensity = Field("screen-rim-intensity", Loc.T("Strength", "強さ", "세기", "强度", "強度"),
                    _rendering.ScreenRimIntensity, 0f, AppSettings.MaxScreenRimIntensity);
                if (!Mathf.Approximately(intensity, _rendering.ScreenRimIntensity))
                {
                    _rendering.ScreenRimIntensity = intensity;
                }

                float power = Field("screen-rim-power", Loc.T("Falloff", "切れ方", "감쇠", "衰减", "衰減"),
                    _rendering.ScreenRimPower, 0.5f, AppSettings.MaxScreenRimPower);
                if (!Mathf.Approximately(power, _rendering.ScreenRimPower))
                {
                    _rendering.ScreenRimPower = power;
                }

                float hue = Field("screen-rim-hue", Loc.T("Hue", "色相", "색상", "色相", "色相"),
                    _rendering.ScreenRimHue, 0f, 1f);
                if (!Mathf.Approximately(hue, _rendering.ScreenRimHue))
                {
                    _rendering.ScreenRimHue = hue;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawLook()
        {
            GuiControls.BeginCard(Loc.T("Look", "映え", "연출", "观感", "觀感"));
            GuiControls.Hint(Loc.T(
                "Bloom and tone mapping. Alpha is preserved for transparent capture.",
                "ブルームとトーンマップです。透過キャプチャのため透明度は保ちます。",
                "블룸과 톤맵입니다. 투명 캡처를 위해 알파를 유지합니다.",
                "辉光与色调映射。为透明采集保留透明度。",
                "輝光與色調映射。為透明擷取保留透明度。"));

            _rendering.BloomEnabled = GUILayout.Toggle(_rendering.BloomEnabled,
                Loc.T("Bloom", "ブルーム", "블룸", "辉光", "輝光"));
            if (_rendering.BloomEnabled)
            {
                float bloom = Field("bloom", Loc.T("Bloom strength", "ブルームの強さ", "블룸 세기", "辉光强度", "輝光強度"),
                    _rendering.BloomIntensity, 0f, AppSettings.MaxBloomIntensity);
                if (!Mathf.Approximately(bloom, _rendering.BloomIntensity))
                {
                    _rendering.BloomIntensity = bloom;
                }

                float threshold = Field("bloom-threshold", Loc.T("Bloom threshold", "ブルームのしきい値", "블룸 임계값",
                        "辉光阈值", "輝光閾值"),
                    _rendering.BloomThreshold, 0f, AppSettings.MaxBloomThreshold);
                if (!Mathf.Approximately(threshold, _rendering.BloomThreshold))
                {
                    _rendering.BloomThreshold = threshold;
                }
            }

            _rendering.TonemapEnabled = GUILayout.Toggle(_rendering.TonemapEnabled,
                Loc.T("Tone mapping", "トーンマップ", "톤맵", "色调映射", "色調映射"));
            if (_rendering.TonemapEnabled)
            {
                float tonemap = Field("tonemap", Loc.T("Tone map strength", "トーンマップの強さ", "톤맵 세기",
                        "色调映射强度", "色調映射強度"),
                    _rendering.TonemapStrength, 0f, AppSettings.MaxTonemapStrength);
                if (!Mathf.Approximately(tonemap, _rendering.TonemapStrength))
                {
                    _rendering.TonemapStrength = tonemap;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawColorGrade()
        {
            GuiControls.BeginCard(Loc.T("Color grading", "色調補正", "색보정", "调色", "調色"));
            GuiControls.Hint(Loc.T(
                "Exposure, contrast, saturation, and white balance. Alpha is preserved.",
                "露出・コントラスト・彩度・ホワイトバランスです。透明度は保ちます。",
                "노출·대비·채도·화이트밸런스입니다. 알파는 유지합니다.",
                "曝光、对比度、饱和度与白平衡。保留透明度。",
                "曝光、對比度、飽和度與白平衡。保留透明度。"));
            _rendering.ColorGradeEnabled = GUILayout.Toggle(_rendering.ColorGradeEnabled,
                Loc.T("Enable color grading", "色調補正を使う", "색보정 사용", "启用调色", "啟用調色"));
            if (_rendering.ColorGradeEnabled)
            {
                float exposure = Field("exposure", Loc.T("Exposure", "露出", "노출", "曝光", "曝光"),
                    _rendering.Exposure, AppSettings.MinExposure, AppSettings.MaxExposure);
                if (!Mathf.Approximately(exposure, _rendering.Exposure))
                {
                    _rendering.Exposure = exposure;
                }

                float contrast = Field("contrast", Loc.T("Contrast", "コントラスト", "대비", "对比度", "對比度"),
                    _rendering.Contrast, AppSettings.MinContrast, AppSettings.MaxContrast);
                if (!Mathf.Approximately(contrast, _rendering.Contrast))
                {
                    _rendering.Contrast = contrast;
                }

                float saturation = Field("saturation", Loc.T("Saturation", "彩度", "채도", "饱和度", "飽和度"),
                    _rendering.Saturation, 0f, AppSettings.MaxSaturation);
                if (!Mathf.Approximately(saturation, _rendering.Saturation))
                {
                    _rendering.Saturation = saturation;
                }

                float temperature = Field("grade-temperature",
                    Loc.T("Temperature (cool ← → warm)", "色温度（寒 ← → 暖）", "색온도 (냉 ← → 온)",
                        "色温（冷 ← → 暖）", "色溫（冷 ← → 暖）"),
                    _rendering.ColorTemperatureBalance, -AppSettings.MaxColorBalance, AppSettings.MaxColorBalance);
                if (!Mathf.Approximately(temperature, _rendering.ColorTemperatureBalance))
                {
                    _rendering.ColorTemperatureBalance = temperature;
                }

                float tint = Field("grade-tint",
                    Loc.T("Tint (green ← → magenta)", "ティント（緑 ← → マゼンタ）", "틴트 (녹 ← → 마젠타)",
                        "色调（绿 ← → 品红）", "色調（綠 ← → 洋紅）"),
                    _rendering.ColorTint, -AppSettings.MaxColorBalance, AppSettings.MaxColorBalance);
                if (!Mathf.Approximately(tint, _rendering.ColorTint))
                {
                    _rendering.ColorTint = tint;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawDetailFx()
        {
            GuiControls.BeginCard(Loc.T("Detail", "ディテール", "디테일", "细节", "細節"));
            GuiControls.Hint(Loc.T(
                "Sharpen and vignette. Keep sharpen subtle on toon faces. Alpha is preserved.",
                "シャープとビネットです。トゥーン顔ではシャープを強くしすぎないでください。透明度は保ちます。",
                "샤픈과 비네트입니다. 툰 얼굴에는 샤픈을 너무 세게 하지 마세요. 알파는 유지합니다.",
                "锐化与暗角。二次元脸请勿锐化过强。保留透明度。",
                "銳化與暗角。二次元臉請勿銳化過強。保留透明度。"));

            _rendering.SharpenEnabled = GUILayout.Toggle(_rendering.SharpenEnabled,
                Loc.T("Sharpen", "シャープ", "샤픈", "锐化", "銳化"));
            if (_rendering.SharpenEnabled)
            {
                float sharpen = Field("sharpen", Loc.T("Amount", "強さ", "세기", "强度", "強度"),
                    _rendering.SharpenAmount, 0f, AppSettings.MaxSharpen);
                if (!Mathf.Approximately(sharpen, _rendering.SharpenAmount))
                {
                    _rendering.SharpenAmount = sharpen;
                }
            }

            _rendering.VignetteEnabled = GUILayout.Toggle(_rendering.VignetteEnabled,
                Loc.T("Vignette", "ビネット", "비네트", "暗角", "暗角"));
            if (_rendering.VignetteEnabled)
            {
                float vignette = Field("vignette", Loc.T("Strength", "強さ", "세기", "强度", "強度"),
                    _rendering.VignetteIntensity, 0f, AppSettings.MaxVignette);
                if (!Mathf.Approximately(vignette, _rendering.VignetteIntensity))
                {
                    _rendering.VignetteIntensity = vignette;
                }
            }

            GuiControls.EndCard();
        }

        private void DrawLegacyPresets()
        {
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
        }

        private void DrawBrightnessHint()
        {
            GuiControls.Hint(Loc.T("Light and avatar brightness are remembered for each avatar.",
                "ライトとアバターの明るさはアバターごとに記憶されます。",
                "조명과 아바타 밝기는 아바타마다 기억됩니다.",
                "灯光和虚拟形象亮度会按虚拟形象分别记住。",
                "燈光和虛擬形象亮度會依虛擬形象分別記住。"));
        }

        private void DrawAvatarBrightness()
        {
            float brightness = Field("avatar-brightness",
                Loc.T("Avatar brightness", "アバターの明るさ", "아바타 밝기", "虚拟形象亮度", "虛擬形象亮度"),
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
        }

        private void DrawAmbientSlider()
        {
            float ambient = Field("ambient",
                Loc.T("Ambient", "環境光", "환경광", "环境光", "環境光"), _rendering.AmbientIntensity, 0f,
                AppSettings.MaxAmbientIntensity);
            if (!Mathf.Approximately(ambient, _rendering.AmbientIntensity))
            {
                _rendering.AmbientIntensity = ambient;
            }
        }

        private void DrawKeyLightSliders(string intensityLabel, string temperatureLabel)
        {
            float intensity = Field("key-intensity", intensityLabel, _rendering.LightIntensity, 0f,
                AppSettings.MaxLightIntensity);
            if (!Mathf.Approximately(intensity, _rendering.LightIntensity))
            {
                _rendering.LightIntensity = intensity;
            }

            float temperature = Field("key-temp", temperatureLabel,
                _rendering.LightTemperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature, "F0");
            if (!Mathf.Approximately(temperature, _rendering.LightTemperature))
            {
                _rendering.LightTemperature = temperature;
            }

            float yaw = Field("key-yaw",
                Loc.T("Direction", "向き", "방향", "方向", "方向"), _rendering.LightYaw, -180f, 180f, "F1");
            if (!Mathf.Approximately(yaw, _rendering.LightYaw))
            {
                _rendering.LightYaw = yaw;
            }

            float pitch = Field("key-pitch",
                Loc.T("Height", "高さ", "높이", "高度", "高度"), _rendering.LightPitch, -90f, 90f, "F1");
            if (!Mathf.Approximately(pitch, _rendering.LightPitch))
            {
                _rendering.LightPitch = pitch;
            }
        }

        private void DrawFillSliders()
        {
            float intensity = Field("fill-intensity", Loc.T("Intensity", "強さ", "세기", "强度", "強度"),
                _rendering.FillIntensity, 0f, AppSettings.MaxLightIntensity);
            if (!Mathf.Approximately(intensity, _rendering.FillIntensity))
            {
                _rendering.FillIntensity = intensity;
            }

            float temperature = Field("fill-temp", Loc.T("Color (K)", "色 (K)", "색 (K)", "颜色 (K)", "顏色 (K)"),
                _rendering.FillTemperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature, "F0");
            if (!Mathf.Approximately(temperature, _rendering.FillTemperature))
            {
                _rendering.FillTemperature = temperature;
            }

            float yaw = Field("fill-yaw", Loc.T("Direction", "向き", "방향", "方向", "方向"),
                _rendering.FillYaw, -180f, 180f, "F1");
            if (!Mathf.Approximately(yaw, _rendering.FillYaw))
            {
                _rendering.FillYaw = yaw;
            }

            float pitch = Field("fill-pitch", Loc.T("Height", "高さ", "높이", "高度", "高度"),
                _rendering.FillPitch, -90f, 90f, "F1");
            if (!Mathf.Approximately(pitch, _rendering.FillPitch))
            {
                _rendering.FillPitch = pitch;
            }
        }

        private void DrawRimSliders()
        {
            float intensity = Field("rim-intensity", Loc.T("Intensity", "強さ", "세기", "强度", "強度"),
                _rendering.RimIntensity, 0f, AppSettings.MaxLightIntensity);
            if (!Mathf.Approximately(intensity, _rendering.RimIntensity))
            {
                _rendering.RimIntensity = intensity;
            }

            float temperature = Field("rim-temp", Loc.T("Color (K)", "色 (K)", "색 (K)", "颜色 (K)", "顏色 (K)"),
                _rendering.RimTemperature, AppSettings.MinLightTemperature, AppSettings.MaxLightTemperature, "F0");
            if (!Mathf.Approximately(temperature, _rendering.RimTemperature))
            {
                _rendering.RimTemperature = temperature;
            }

            float yaw = Field("rim-yaw", Loc.T("Direction", "向き", "방향", "方向", "方向"),
                _rendering.RimYaw, -180f, 180f, "F1");
            if (!Mathf.Approximately(yaw, _rendering.RimYaw))
            {
                _rendering.RimYaw = yaw;
            }

            float pitch = Field("rim-pitch", Loc.T("Height", "高さ", "높이", "高度", "高度"),
                _rendering.RimPitch, -90f, 90f, "F1");
            if (!Mathf.Approximately(pitch, _rendering.RimPitch))
            {
                _rendering.RimPitch = pitch;
            }
        }

        private void DrawIblSliders()
        {
            int preset = GuiControls.LabeledChoice(
                Loc.T("Preset", "プリセット", "프리셋", "预设", "預設"),
                new[]
                {
                    Loc.T("Studio", "スタジオ", "스튜디오", "工作室", "工作室"),
                    Loc.T("Warm", "暖色", "따뜻한", "暖色", "暖色"),
                    Loc.T("Cool", "寒色", "차가운", "冷色", "冷色"),
                },
                (int)_rendering.IblPreset);
            if (preset != (int)_rendering.IblPreset)
            {
                _rendering.IblPreset = (IblPreset)preset;
            }

            float ibl = Field("ibl", Loc.T("IBL strength", "環境光の強さ", "환경광 세기", "环境光强度", "環境光強度"),
                _rendering.IblIntensity, 0f, AppSettings.MaxIblIntensity);
            if (!Mathf.Approximately(ibl, _rendering.IblIntensity))
            {
                _rendering.IblIntensity = ibl;
            }

            float reflection = Field("reflection",
                Loc.T("Reflection", "反射", "반사", "反射", "反射"),
                _rendering.ReflectionStrength, 0f, AppSettings.MaxReflectionStrength);
            if (!Mathf.Approximately(reflection, _rendering.ReflectionStrength))
            {
                _rendering.ReflectionStrength = reflection;
            }
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

            // 太さの倍率（スライダーか欄への数値。値が変わったときだけ反映）
            float width = Field("outline", Loc.T("Thickness", "太さ", "굵기", "粗细", "粗細"),
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

        private void DrawResetDefaults()
        {
            GuiControls.BeginCard(Loc.T("Reset", "リセット", "초기화", "重置", "重設"));
            GuiControls.Hint(Loc.T(
                "Restores graphics settings only (background, anti-aliasing, lighting, outline). Pose and other tabs are kept.",
                "グラフィックの設定だけを既定に戻します（背景・アンチエイリアス・ライト・輪郭線）。ポーズなど他のタブはそのままです。",
                "그래픽 설정만 기본값으로 돌립니다(배경·안티앨리어싱·조명·윤곽선). 포즈 등 다른 탭은 그대로입니다.",
                "仅将图形设置恢复为默认（背景、抗锯齿、光照、轮廓线）。姿势等其他页保持不变。",
                "僅將圖形設定恢復為預設（背景、抗鋸齒、光照、輪廓線）。姿勢等其他分頁保持不變。"));

            if (_confirmingReset)
            {
                GuiControls.Warning(Loc.T(
                    "Reset all graphics settings to defaults?",
                    "グラフィックの設定をすべて既定に戻しますか？",
                    "그래픽 설정을 모두 기본값으로 되돌릴까요?",
                    "要将所有图形设置恢复为默认吗？",
                    "要將所有圖形設定恢復為預設嗎？"));
                GUILayout.BeginHorizontal();
                if (GuiControls.DangerButton(Loc.T("Reset to defaults", "既定に戻す", "기본값으로", "恢复默认", "恢復預設"),
                        GuiControls.Shrinkable))
                {
                    _settings.ResetGraphicsToDefaults();
                    _rendering.ApplyAll();
                    _reapply();
                    _confirmingReset = false;
                }

                if (GUILayout.Button(Loc.T("Cancel", "やめる", "취소", "取消", "取消"), GuiControls.Shrinkable))
                {
                    _confirmingReset = false;
                }

                GUILayout.EndHorizontal();
            }
            else
            {
                UiTheme theme = UiTheme.Current;
                if (GUILayout.Button(Loc.T("Reset graphics to defaults...", "グラフィックを既定に戻す...",
                        "그래픽 기본값으로...", "将图形恢复为默认...", "將圖形恢復為預設..."),
                        theme != null ? theme.OverflowButton : GUI.skin.button))
                {
                    _confirmingReset = true;
                }
            }

            GuiControls.EndCard();
        }
    }
}
