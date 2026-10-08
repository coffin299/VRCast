using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// 操作パネルのテーマ（ライト = アバター背景の既定ベージュに合わせた配色、ダーク = 暗い茶系）。角丸・スイッチ・スライダーのテクスチャを実行時に生成し、
    /// IMGUI の既定スキンを複製して差し替える（GUILayout の既存の呼び出しがそのまま新しい見た目になる）。
    /// IMGUI のスキンは OnGUI 内でしか作れないため、最初の OnGUI で Create する。
    /// </summary>
    public sealed class UiTheme
    {
        // 表示できる OS フォント（先頭から順に使い、無い文字は後続で補う）。漢字の字形が言語で違うため、
        // 中国語表示では中国語フォントを日本語フォントより先にする。ハングルは Malgun Gothic
        private static readonly string[] FontNames =
            { "Yu Gothic UI", "Meiryo UI", "Malgun Gothic", "Microsoft YaHei UI", "Microsoft JhengHei UI", "Segoe UI", "Arial" };
        private static readonly string[] FontNamesSimplified =
            { "Microsoft YaHei UI", "Microsoft JhengHei UI", "Yu Gothic UI", "Malgun Gothic", "Segoe UI", "Arial" };
        private static readonly string[] FontNamesTraditional =
            { "Microsoft JhengHei UI", "Microsoft YaHei UI", "Yu Gothic UI", "Malgun Gothic", "Segoe UI", "Arial" };
        private const int FontSize = 14;

        /// <summary>
        /// テーマの配色一式。
        /// </summary>
        private sealed class Palette
        {
            public Color Background;
            public Color Surface;
            public Color Card;
            public Color Control;
            public Color ControlHover;
            public Color ControlActive;
            public Color Field;
            public Color FieldFocus;
            public Color Accent;
            public Color AccentHover;
            public Color OnAccent;
            public Color Track;
            public Color Knob;
            public Color Text;
            public Color TextDim;
            public Color Danger;
            public Color DangerHover;
            public Color DangerActive;
            public Color Success;
            public Color Warning;
        }

        // ライト（背景のベージュより濃いベージュの地、焦げ茶の文字、キャラメル色のアクセント）
        private static readonly Palette LightPalette = new Palette
        {
            Background = new Color32(204, 189, 162, 245),
            Surface = new Color32(191, 174, 145, 255),
            Card = new Color32(218, 205, 182, 255),
            Control = new Color32(196, 179, 149, 255),
            ControlHover = new Color32(182, 164, 133, 255),
            ControlActive = new Color32(168, 150, 119, 255),
            Field = new Color32(240, 233, 220, 255),
            FieldFocus = new Color32(250, 246, 238, 255),
            Accent = new Color32(166, 98, 52, 255),
            AccentHover = new Color32(186, 117, 68, 255),
            OnAccent = new Color32(255, 250, 242, 255),
            Track = new Color32(172, 154, 124, 255),
            Knob = new Color32(252, 248, 240, 255),
            Text = new Color32(58, 46, 34, 255),
            TextDim = new Color32(112, 96, 76, 255),
            Danger = new Color32(192, 56, 50, 255),
            DangerHover = new Color32(212, 74, 66, 255),
            DangerActive = new Color32(158, 42, 38, 255),
            Success = new Color32(52, 130, 76, 255),
            Warning = new Color32(160, 104, 0, 255),
        };

        // ダーク（暗い焦げ茶の地、生成り色の文字、暗い地でも見えるよう明るめのキャラメル色のアクセント）
        private static readonly Palette DarkPalette = new Palette
        {
            Background = new Color32(30, 27, 24, 245),
            Surface = new Color32(40, 36, 32, 255),
            Card = new Color32(50, 45, 40, 255),
            Control = new Color32(64, 58, 51, 255),
            ControlHover = new Color32(78, 71, 62, 255),
            ControlActive = new Color32(92, 84, 73, 255),
            Field = new Color32(34, 31, 28, 255),
            FieldFocus = new Color32(26, 24, 21, 255),
            Accent = new Color32(196, 124, 72, 255),
            AccentHover = new Color32(214, 142, 88, 255),
            OnAccent = new Color32(255, 250, 242, 255),
            Track = new Color32(86, 78, 68, 255),
            Knob = new Color32(236, 230, 220, 255),
            Text = new Color32(236, 228, 216, 255),
            TextDim = new Color32(168, 156, 140, 255),
            Danger = new Color32(200, 64, 56, 255),
            DangerHover = new Color32(220, 82, 72, 255),
            DangerActive = new Color32(166, 48, 42, 255),
            Success = new Color32(104, 186, 126, 255),
            Warning = new Color32(232, 184, 72, 255),
        };

        // 使用中の配色（以下の名前で各 Build から参照する）
        private Palette _colors = LightPalette;
        private Color Background => _colors.Background;
        private Color Surface => _colors.Surface;
        private Color CardColor => _colors.Card;
        private Color Control => _colors.Control;
        private Color ControlHover => _colors.ControlHover;
        private Color ControlActive => _colors.ControlActive;
        private Color FieldColor => _colors.Field;
        private Color FieldFocus => _colors.FieldFocus;
        private Color Accent => _colors.Accent;
        private Color AccentHover => _colors.AccentHover;
        private Color OnAccent => _colors.OnAccent;
        private Color TrackColor => _colors.Track;
        private Color Knob => _colors.Knob;
        private Color TextColor => _colors.Text;
        private Color TextDim => _colors.TextDim;
        private Color DangerColor => _colors.Danger;
        private Color DangerHover => _colors.DangerHover;
        private Color DangerActive => _colors.DangerActive;
        private Color SuccessColor => _colors.Success;
        private Color WarningColor => _colors.Warning;

        // 角丸の半径（px）
        private const int WindowRadius = 10;
        private const int CardRadius = 8;
        private const int ControlRadius = 6;

        // スイッチの大きさ（テクスチャ幅 = つまみ部分 + 透明な余白、ラベルはその右）
        private const int SwitchWidth = 38;
        private const int SwitchHeight = 22;
        private const int SwitchTextureWidth = 44;

        // スライダーの高さ・溝の太さ・つまみの直径
        private const int SliderHeight = 16;
        private const int TrackThickness = 4;
        private const int ThumbSize = 16;

        // スクロールバーの幅
        private const int ScrollbarWidth = 8;

        // 生成したテクスチャ（破棄用）
        private readonly List<Texture2D> _textures = new List<Texture2D>();

        /// <summary>
        /// 最後に作成したテーマ（GuiControls から参照）。
        /// </summary>
        public static UiTheme Current { get; private set; }

        /// <summary>
        /// フォントの選択に使った表示言語（変わったら作り直す）。
        /// </summary>
        public UiLanguage Language { get; private set; }

        /// <summary>
        /// ダークモードの配色で作ったか（設定が変わったら作り直す）。
        /// </summary>
        public bool Dark { get; private set; }

        /// <summary>
        /// カード内の説明文の下に引く区切り線の色（控えめな文字色を薄くしたもの）。
        /// </summary>
        public Color Divider => new Color(TextDim.r, TextDim.g, TextDim.b, DividerAlpha);

        // 区切り線の不透明度
        private const float DividerAlpha = 0.35f;

        public GUISkin Skin { get; private set; }
        public GUIStyle Title { get; private set; }
        public GUIStyle SectionTitle { get; private set; }

        // カード内の小見出し（アクセント色の太字、上に余白）
        public GUIStyle SubTitle { get; private set; }
        public GUIStyle Hint { get; private set; }
        public GUIStyle KeyHint { get; private set; }
        public GUIStyle Value { get; private set; }
        public GUIStyle Centered { get; private set; }
        public GUIStyle Card { get; private set; }
        public GUIStyle Sidebar { get; private set; }
        public GUIStyle Tab { get; private set; }

        // 短い文字のボタン（言語の切替など。代替フォントの字形が下へはみ出しても切らない）
        public GUIStyle OverflowButton { get; private set; }

        // 取り返しのつかない操作用の赤いボタンと、完了表示の緑の文字
        public GUIStyle Danger { get; private set; }
        public GUIStyle Success { get; private set; }

        // デバッグログの重要度ラベル（警告は黄、エラーは赤の太字）
        public GUIStyle WarningText { get; private set; }
        public GUIStyle ErrorText { get; private set; }

        /// <summary>
        /// テーマを作成する（OnGUI 内で呼ぶ）。language は解決済みの表示言語（フォントの優先順に使う）、dark はダークモードの配色にするか。
        /// </summary>
        public static UiTheme Create(UiLanguage language, bool dark)
        {
            var theme = new UiTheme { Language = language, Dark = dark, _colors = dark ? DarkPalette : LightPalette };
            theme.Build();
            Current = theme;
            return theme;
        }

        /// <summary>
        /// 生成したスキン・テクスチャ・フォントを破棄する。
        /// </summary>
        public void Destroy()
        {
            foreach (Texture2D texture in _textures)
            {
                Object.Destroy(texture);
            }

            _textures.Clear();
            if (Skin != null)
            {
                Object.Destroy(Skin.font);
                Object.Destroy(Skin);
            }

            // 破棄済みのテーマを参照させない
            if (Current == this)
            {
                Current = null;
            }
        }

        private void Build()
        {
            // 既定スキンを複製してフォントを差し替える
            Skin = Object.Instantiate(GUI.skin);
            Skin.hideFlags = HideFlags.HideAndDontSave;
            Skin.font = Font.CreateDynamicFontFromOSFont(FontNamesOf(Language), FontSize);

            BuildWindow();
            BuildText();
            BuildButton();
            BuildToggle();
            BuildTextField();
            BuildSlider();
            BuildScrollbar();
            BuildPanels();
        }

        private static string[] FontNamesOf(UiLanguage language)
        {
            // 中国語は簡体字・繁体字それぞれのフォントを先頭に、それ以外は日本語フォントを先頭に
            switch (language)
            {
                case UiLanguage.ChineseSimplified:
                    return FontNamesSimplified;
                case UiLanguage.ChineseTraditional:
                    return FontNamesTraditional;
                default:
                    return FontNames;
            }
        }

        private void BuildWindow()
        {
            // 角丸の半透明パネル
            Skin.window = new GUIStyle
            {
                border = Offset(WindowRadius + 1),
                padding = new RectOffset(12, 12, 10, 12),
            };
            Texture2D background = Rounded(Background, WindowRadius);
            Skin.window.normal.background = background;
            Skin.window.onNormal.background = background;
        }

        private void BuildText()
        {
            // 本文（折り返しあり）。日本語フォントの字形は行の高さより下へはみ出すため、枠で切らずにはみ出して描く
            Skin.label = new GUIStyle(Skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                clipping = TextClipping.Overflow,
                padding = new RectOffset(2, 2, 3, 3),
                margin = new RectOffset(4, 4, 2, 2),
            };
            Skin.label.normal.textColor = TextColor;

            // 見出し・補足・数値・中央寄せ
            Title = new GUIStyle(Skin.label) { fontSize = 17, fontStyle = FontStyle.Bold, wordWrap = false };
            SectionTitle = new GUIStyle(Skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            SectionTitle.margin.bottom = 6;
            SubTitle = new GUIStyle(Skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            SubTitle.margin.top = 10;
            SubTitle.normal.textColor = Accent;
            Hint = new GUIStyle(Skin.label) { fontSize = 12 };
            Hint.normal.textColor = TextDim;

            // 見出し行のキー操作の案内（目立つようアクセント色の太字）
            KeyHint = new GUIStyle(Skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                alignment = TextAnchor.MiddleRight,
            };
            KeyHint.normal.textColor = Accent;
            Value = new GUIStyle(Hint) { alignment = TextAnchor.MiddleRight, wordWrap = false };
            // 選択行の候補名（幅が足りなければ左右を切らずに折り返す）
            Centered = new GUIStyle(Skin.label) { alignment = TextAnchor.MiddleCenter };
            Success = new GUIStyle(Skin.label) { fontStyle = FontStyle.Bold, wordWrap = false };
            Success.normal.textColor = SuccessColor;
            // 注意は文が長くなるので折り返す
            WarningText = new GUIStyle(Success) { wordWrap = true };
            WarningText.normal.textColor = WarningColor;
            ErrorText = new GUIStyle(Success);
            ErrorText.normal.textColor = DangerColor;
        }

        private void BuildButton()
        {
            // 角丸ボタン（選択中 = アクセント色、トグルボタンとしても使う）
            Skin.button = new GUIStyle
            {
                font = null,
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                border = Offset(ControlRadius + 1),
                padding = new RectOffset(10, 10, 6, 6),
                margin = new RectOffset(4, 4, 3, 3),
                clipping = TextClipping.Clip,
            };
            SetStates(Skin.button, Rounded(Control, ControlRadius), Rounded(ControlHover, ControlRadius),
                Rounded(ControlActive, ControlRadius), TextColor);
            SetOnStates(Skin.button, Rounded(Accent, ControlRadius), Rounded(AccentHover, ControlRadius), OnAccent);
            OverflowButton = new GUIStyle(Skin.button) { clipping = TextClipping.Overflow };

            // 赤いボタン（全設定のリセット等）
            Danger = new GUIStyle(Skin.button) { fontStyle = FontStyle.Bold };
            SetStates(Danger, Rounded(DangerColor, ControlRadius), Rounded(DangerHover, ControlRadius),
                Rounded(DangerActive, ControlRadius), OnAccent);
        }

        private void BuildToggle()
        {
            // スイッチ型のトグル（左端にスイッチ、右にラベル。テクスチャの左側を固定幅で描かせる）
            Skin.toggle = new GUIStyle
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Overflow,
                fixedHeight = SwitchHeight,
                border = new RectOffset(SwitchTextureWidth - 2, 1, 0, 0),
                padding = new RectOffset(SwitchTextureWidth + 4, 4, 0, 0),
                margin = new RectOffset(4, 4, 4, 4),
            };
            Texture2D off = Switch(false);
            Texture2D on = Switch(true);
            SetStates(Skin.toggle, off, off, off, TextColor);
            SetOnStates(Skin.toggle, on, on, TextColor);
        }

        private void BuildTextField()
        {
            // 角丸の明るい入力欄（フォーカス時はさらに明るく）
            Skin.textField = new GUIStyle
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                clipping = TextClipping.Clip,
                border = Offset(ControlRadius + 1),
                padding = new RectOffset(8, 8, 5, 5),
                margin = new RectOffset(4, 4, 3, 3),
            };
            Texture2D normal = Rounded(FieldColor, ControlRadius);
            Texture2D focused = Rounded(FieldFocus, ControlRadius);
            SetStates(Skin.textField, normal, focused, normal, TextColor);
            Skin.textField.focused.background = focused;
            Skin.textField.focused.textColor = TextColor;
            Skin.settings.cursorColor = TextColor;
            Skin.settings.selectionColor = new Color(Accent.r, Accent.g, Accent.b, 0.5f);
        }

        private void BuildSlider()
        {
            // 細い溝と丸いつまみ
            Skin.horizontalSlider = new GUIStyle
            {
                fixedHeight = SliderHeight,
                border = new RectOffset(TrackThickness, TrackThickness, 0, 0),
                margin = new RectOffset(4, 4, 6, 6),
            };
            Skin.horizontalSlider.normal.background = Track();
            Skin.horizontalSliderThumb = new GUIStyle { fixedWidth = ThumbSize, fixedHeight = ThumbSize };
            Texture2D thumb = Circle(Accent);
            SetStates(Skin.horizontalSliderThumb, thumb, Circle(AccentHover), Circle(Knob), TextColor);
        }

        private void BuildScrollbar()
        {
            // 細い縦スクロールバー（上下ボタンなし）
            Skin.verticalScrollbar = new GUIStyle
            {
                fixedWidth = ScrollbarWidth,
                border = Offset(ScrollbarWidth / 2),
                margin = new RectOffset(4, 0, 2, 2),
            };
            Skin.verticalScrollbar.normal.background = Rounded(Surface, ScrollbarWidth / 2 - 1);
            Skin.verticalScrollbarThumb = new GUIStyle { fixedWidth = ScrollbarWidth, border = Offset(ScrollbarWidth / 2) };
            SetStates(Skin.verticalScrollbarThumb, Rounded(Control, ScrollbarWidth / 2 - 1),
                Rounded(ControlHover, ScrollbarWidth / 2 - 1), Rounded(Accent, ScrollbarWidth / 2 - 1), TextColor);
            Skin.verticalScrollbarUpButton = new GUIStyle();
            Skin.verticalScrollbarDownButton = new GUIStyle();
            Skin.scrollView = new GUIStyle();
        }

        private void BuildPanels()
        {
            // セクションのカード
            Card = new GUIStyle
            {
                border = Offset(CardRadius + 1),
                padding = new RectOffset(12, 12, 10, 12),
                margin = new RectOffset(0, 4, 0, 10),
            };
            Card.normal.background = Rounded(CardColor, CardRadius);

            // 左のタブ列
            Sidebar = new GUIStyle
            {
                border = Offset(CardRadius + 1),
                padding = new RectOffset(6, 6, 6, 6),
                margin = new RectOffset(0, 10, 0, 0),
            };
            Sidebar.normal.background = Rounded(Surface, CardRadius);

            // タブ（選択中 = アクセント色、未選択は背景なし）
            Tab = new GUIStyle(Skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 34f,
                padding = new RectOffset(12, 8, 6, 6),
                margin = new RectOffset(0, 0, 2, 2),
            };
            SetStates(Tab, null, Rounded(ControlHover, ControlRadius), Rounded(ControlActive, ControlRadius), TextDim);
            Tab.hover.textColor = TextColor;
        }

        private static void SetStates(GUIStyle style, Texture2D normal, Texture2D hover, Texture2D active, Color text)
        {
            // 未選択時の各状態（無効時は文字色を暗く）
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = active;
            style.focused.background = normal;
            style.normal.textColor = text;
            style.hover.textColor = text;
            style.active.textColor = text;
            style.focused.textColor = text;
        }

        private static void SetOnStates(GUIStyle style, Texture2D normal, Texture2D hover, Color text)
        {
            // 選択中の各状態
            style.onNormal.background = normal;
            style.onHover.background = hover;
            style.onActive.background = normal;
            style.onFocused.background = normal;
            style.onNormal.textColor = text;
            style.onHover.textColor = text;
            style.onActive.textColor = text;
            style.onFocused.textColor = text;
        }

        private static RectOffset Offset(int size)
        {
            return new RectOffset(size, size, size, size);
        }

        private Texture2D Rounded(Color color, int radius)
        {
            // 角の半径 + 伸縮する中央 2px の正方形
            int size = radius * 2 + 4;
            return Paint(size, size, (x, y) => Over(Color.clear, color, RoundedCoverage(x, y, 0f, 0f, size, size, radius)));
        }

        private Texture2D Switch(bool on)
        {
            // 角丸の溝とつまみ（ON は右・アクセント色）。溝の右側は透明な余白
            float trackTop = 2f;
            float trackHeight = SwitchHeight - 4f;
            float knobRadius = trackHeight * 0.5f - 2f;
            float knobX = on ? SwitchWidth - trackHeight * 0.5f : trackHeight * 0.5f;
            Color track = on ? Accent : TrackColor;
            return Paint(SwitchTextureWidth, SwitchHeight, (x, y) =>
            {
                float trackCoverage = RoundedCoverage(x, y, 0f, trackTop, SwitchWidth, trackHeight, trackHeight * 0.5f);
                float knobCoverage = CircleCoverage(x, y, knobX, SwitchHeight * 0.5f, knobRadius);
                return Over(Over(Color.clear, track, trackCoverage), Knob, knobCoverage);
            });
        }

        private Texture2D Track()
        {
            // 縦中央の細い溝（左右は伸縮）
            int width = TrackThickness * 2 + 2;
            float top = (SliderHeight - TrackThickness) * 0.5f;
            return Paint(width, SliderHeight, (x, y) =>
                Over(Color.clear, TrackColor, RoundedCoverage(x, y, 0f, top, width, TrackThickness, TrackThickness * 0.5f)));
        }

        private Texture2D Circle(Color color)
        {
            // つまみの円
            float center = ThumbSize * 0.5f;
            return Paint(ThumbSize, ThumbSize, (x, y) =>
                Over(Color.clear, color, CircleCoverage(x, y, center, center, center - 1f)));
        }

        private Texture2D Paint(int width, int height, System.Func<float, float, Color> shade)
        {
            // 各ピクセルの中心で色を求めて塗る
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    pixels[y * width + x] = shade(x + 0.5f, y + 0.5f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _textures.Add(texture);
            return texture;
        }

        private static float RoundedCoverage(float px, float py, float left, float top, float width, float height, float radius)
        {
            // 角丸長方形の内側からの距離（境界 1px でなめらかに）
            float qx = Mathf.Max(Mathf.Abs(px - (left + width * 0.5f)) - (width * 0.5f - radius), 0f);
            float qy = Mathf.Max(Mathf.Abs(py - (top + height * 0.5f)) - (height * 0.5f - radius), 0f);
            float distance = Mathf.Sqrt(qx * qx + qy * qy) - radius;
            return Mathf.Clamp01(0.5f - distance);
        }

        private static float CircleCoverage(float px, float py, float cx, float cy, float radius)
        {
            // 円の内側からの距離（境界 1px でなめらかに）
            float distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy)) - radius;
            return Mathf.Clamp01(0.5f - distance);
        }

        private static Color Over(Color under, Color over, float coverage)
        {
            // 覆う色を被覆率で重ねる（アルファ合成）
            float alpha = over.a * coverage;
            float outAlpha = alpha + under.a * (1f - alpha);
            if (outAlpha <= 0f)
            {
                return Color.clear;
            }

            Color rgb = (over * alpha + under * under.a * (1f - alpha)) / outAlpha;
            rgb.a = outAlpha;
            return rgb;
        }
    }
}
