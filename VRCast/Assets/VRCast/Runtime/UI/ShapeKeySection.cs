using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// Shape key setup タブ（BlendShape ごとの上限。顔 / その他の切り替え・検索・上限付きだけの表示、アバターごとに記録）。
    /// 一覧は全件を専用のスクロール欄に出し、軽くするため見えている行だけを描く（絞り込み結果は条件が変わったときだけ作り直す）。
    /// </summary>
    public class ShapeKeySection
    {
        // 一覧の 1 行の高さと、一覧の欄の最大の高さ（全件を出すが、描くのは欄に見えている行だけ）
        private const float RowHeight = 26f;
        private const float MaxListHeight = 520f;

        // スライダーと数値の幅
        private const float SliderWidth = 150f;
        private const float ValueWidth = 36f;

        // 0〜100 の数値の表示（毎フレーム文字列を作らない）
        private static readonly string[] WeightTexts = BuildWeightTexts();

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;

        // 一覧の表示条件（0 = 顔、1 = その他・検索文字列・上限付きだけ）
        private int _limitGroup;
        private string _limitSearch = string.Empty;
        private bool _limitedOnly;

        // 条件に合う BlendShape（条件・アバター・上限付きの数・顔の数が変わったときだけ作り直す）と、作ったときの条件
        private readonly List<BlendShapeLimiter.Shape> _rows = new List<BlendShapeLimiter.Shape>();
        private (BlendShapeLimiter limiter, int group, string search, bool limitedOnly, int limited, int face) _rowsKey;

        // 一覧の欄のスクロール位置
        private Vector2 _listScroll;

        // 行の名前の文字（折り返さず、はみ出しは切る。スキンが作り直されたら作り直す）
        private GUIStyle _rowLabel;
        private GUIStyle _rowLabelSource;

        public ShapeKeySection(AvatarSession session, AppSettings settings)
        {
            _session = session;
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        public void Draw()
        {
            _avatar.Refresh();
            GuiControls.BeginCard(Loc.T("Blend shape limits", "BlendShape の上限", "BlendShape 상한", "BlendShape 上限",
                "BlendShape 上限"));
            GuiControls.Hint(Loc.T(
                "Caps how far each blend shape moves (100 = no limit). Lower it when, for example, the eyes disappear while blinking. Saved per avatar.",
                "各 BlendShape が動く最大値です（100 = 制限なし）。まばたきで目が消える等のときに下げます。アバターごとに保存されます。",
                "각 BlendShape가 움직이는 최대값입니다 (100 = 제한 없음). 눈을 깜빡일 때 눈이 사라지는 경우 등에 낮춥니다. 아바타별로 저장됩니다.",
                "每个 BlendShape 可动到的最大值（100 = 不限制）。例如眨眼时眼睛消失时调低。按虚拟形象分别保存。",
                "每個 BlendShape 可動到的最大值（100 = 不限制）。例如眨眼時眼睛消失時調低。依虛擬形象分別儲存。"));

            // アバター未表示なら一覧を出さない
            var limiter = _avatar.Get<BlendShapeLimiter>();
            if (limiter == null)
            {
                GuiControls.Hint(Loc.T("Load an avatar to see its blend shapes.", "アバターを読み込むと BlendShape が表示されます。",
                    "아바타를 불러오면 BlendShape가 표시됩니다.", "加载虚拟形象后会显示 BlendShape。", "載入虛擬形象後會顯示 BlendShape。"));
                GuiControls.EndCard();
                return;
            }

            DrawFilters(limiter);
            bool changed = DrawRows(limiter);

            // 上限をすべて外す
            if (limiter.LimitedCount > 0
                && GUILayout.Button(Loc.T("Remove all limits", "上限をすべて解除", "상한 모두 해제", "解除全部上限", "解除全部上限"),
                    GuiControls.Shrinkable))
            {
                limiter.ClearAll();
                changed = true;
            }

            // 変えたらこのアバターの分として記録する（保存は終了時）
            if (changed && _session.Current != null)
            {
                _settings.SetBlendShapeLimits(_session.Current.SourcePath, limiter.Export());
            }

            GuiControls.EndCard();
        }

        private void DrawFilters(BlendShapeLimiter limiter)
        {
            // 顔（まばたき・口・表情・パーフェクトシンクで動くもの）か、その他か
            string[] groups =
            {
                Loc.T("Face (blink, mouth, expressions)", "顔（まばたき・口・表情）", "얼굴 (눈 깜빡임·입·표정)",
                    "面部（眨眼、嘴、表情）", "臉部（眨眼、嘴、表情）"),
                Loc.T("Others", "その他", "기타", "其他", "其他"),
            };
            _limitGroup = GuiControls.EnumSelector(Loc.T("Show", "対象", "대상", "对象", "對象"), groups, _limitGroup);

            // 名前で絞り込み（大文字・小文字は区別しない）
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Search", "検索", "검색", "搜索", "搜尋"), GUILayout.ExpandWidth(false));
            _limitSearch = GUILayout.TextField(_limitSearch, GuiControls.Shrinkable);
            GUILayout.EndHorizontal();
            _limitedOnly = GUILayout.Toggle(_limitedOnly, Loc.T(
                $"Limited only ({limiter.LimitedCount})", $"上限を付けたものだけ（{limiter.LimitedCount} 件）",
                $"상한을 설정한 것만 ({limiter.LimitedCount}개)", $"仅显示已设上限的（{limiter.LimitedCount} 个）",
                $"僅顯示已設上限的（{limiter.LimitedCount} 個）"));
        }

        private bool DrawRows(BlendShapeLimiter limiter)
        {
            RefreshRows(limiter);
            bool face = _limitGroup == 0;

            // 1 件も無ければ理由を出す
            if (_rows.Count == 0)
            {
                GuiControls.Hint(face && limiter.FaceCount == 0
                    ? Loc.T("This avatar has no blend shapes moved by blink, mouth or expressions (see Others)",
                        "このアバターには、まばたき・口・表情で動く BlendShape がありません（その他を見てください）",
                        "이 아바타에는 눈 깜빡임·입·표정으로 움직이는 BlendShape가 없습니다 (기타를 확인하세요)",
                        "此虚拟形象没有由眨眼、嘴、表情驱动的 BlendShape（请查看其他）",
                        "此虛擬形象沒有由眨眼、嘴、表情驅動的 BlendShape（請查看其他）")
                    : Loc.T("No matching blend shapes", "該当する BlendShape がありません", "해당하는 BlendShape가 없습니다",
                        "没有符合的 BlendShape", "沒有符合的 BlendShape"));
                return false;
            }

            GuiControls.Hint(Loc.T($"{_rows.Count} blend shapes", $"{_rows.Count} 件", $"{_rows.Count}개", $"{_rows.Count} 个",
                $"{_rows.Count} 個"));

            // 全件分の高さを持つ欄に、見えている行だけを描く（上下の見えない行は空白で高さだけ確保する）
            float height = Mathf.Min(MaxListHeight, _rows.Count * RowHeight);
            _listScroll = GUILayout.BeginScrollView(_listScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Height(height));
            int first = Mathf.Clamp(Mathf.FloorToInt(_listScroll.y / RowHeight), 0, _rows.Count);
            int last = Mathf.Min(_rows.Count, first + Mathf.CeilToInt(height / RowHeight) + 1);
            GUILayout.Space(first * RowHeight);

            bool changed = false;
            for (int i = first; i < last; i++)
            {
                changed |= DrawRow(limiter, _rows[i]);
            }

            GUILayout.Space((_rows.Count - last) * RowHeight);
            GUILayout.EndScrollView();
            return changed;
        }

        private bool DrawRow(BlendShapeLimiter limiter, BlendShapeLimiter.Shape shape)
        {
            // 名前（幅いっぱい、はみ出しは切る）+ スライダー + 数値の、高さ固定の 1 行
            GUILayout.BeginHorizontal(GUILayout.Height(RowHeight));
            GUILayout.Label(shape.Label, RowLabel(), GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true));
            float max = GUILayout.HorizontalSlider(shape.Max, BlendShapeLimit.MinWeight, BlendShapeLimit.MaxWeight,
                GUILayout.Width(SliderWidth));
            UiTheme theme = UiTheme.Current;
            GUILayout.Label(WeightTexts[Mathf.Clamp(Mathf.RoundToInt(max), 0, WeightTexts.Length - 1)],
                theme != null ? theme.Value : GUI.skin.label, GUILayout.Width(ValueWidth));
            GUILayout.EndHorizontal();

            // 動かしたら整数に丸めて当てる
            if (Mathf.Approximately(max, shape.Max))
            {
                return false;
            }

            limiter.SetMax(shape, Mathf.Round(max));
            return true;
        }

        private void RefreshRows(BlendShapeLimiter limiter)
        {
            // 条件・アバター・上限付きの数・顔の数が前回と同じなら作り直さない
            var key = (limiter, _limitGroup, _limitSearch, _limitedOnly, limiter.LimitedCount, limiter.FaceCount);
            if (_rowsKey.Equals(key))
            {
                return;
            }

            // 条件が変わったら先頭から見せる（上限付きの数だけの変化ではスクロールを保つ）
            if (_rowsKey.limiter != limiter || _rowsKey.group != _limitGroup || _rowsKey.search != _limitSearch
                || _rowsKey.limitedOnly != _limitedOnly)
            {
                _listScroll = Vector2.zero;
            }

            _rowsKey = key;
            _rows.Clear();
            bool face = _limitGroup == 0;
            foreach (BlendShapeLimiter.Shape shape in limiter.Shapes)
            {
                // 対象・上限付きだけ・検索文字列で絞り込む
                if (shape.IsFace == face && (!_limitedOnly || shape.IsLimited)
                    && (_limitSearch.Length == 0 || shape.Name.IndexOf(_limitSearch, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    _rows.Add(shape);
                }
            }
        }

        private GUIStyle RowLabel()
        {
            // テーマ（スキン）が作り直されたら、その本文の文字を元に作り直す
            if (_rowLabel == null || _rowLabelSource != GUI.skin.label)
            {
                _rowLabelSource = GUI.skin.label;
                _rowLabel = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip };
            }

            return _rowLabel;
        }

        private static string[] BuildWeightTexts()
        {
            // 上限の範囲（0〜100）の整数の文字列
            var texts = new string[(int)BlendShapeLimit.MaxWeight + 1];
            for (int i = 0; i < texts.Length; i++)
            {
                texts[i] = i.ToString();
            }

            return texts;
        }
    }
}
