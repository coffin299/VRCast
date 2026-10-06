using System;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// Shape keys タブ（BlendShape ごとの上限。顔 / その他の切り替え・検索・上限付きだけの表示、アバターごとに記録）。
    /// </summary>
    public class ShapeKeySection
    {
        // 一覧に一度に出す行数（多いと操作パネルが重くなるため、超えたら検索で絞り込んでもらう）
        private const int MaxLimitRows = 40;

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;

        // 一覧の表示条件（0 = 顔、1 = その他・検索文字列・上限付きだけ）
        private int _limitGroup;
        private string _limitSearch = string.Empty;
        private bool _limitedOnly;

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
            // 条件に合う BlendShape をスライダーで並べる（行数の上限を超えた分は件数だけ出す）
            bool face = _limitGroup == 0;
            bool changed = false;
            int shown = 0;
            int hidden = 0;
            foreach (BlendShapeLimiter.Shape shape in limiter.Shapes)
            {
                // 対象・上限付きだけ・検索文字列で絞り込む
                if (shape.IsFace != face || (_limitedOnly && !shape.IsLimited)
                    || (_limitSearch.Length > 0 && shape.Name.IndexOf(_limitSearch, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                // 行数の上限を超えた分は数えるだけ
                if (shown >= MaxLimitRows)
                {
                    hidden++;
                    continue;
                }

                // 同じ名前が複数のメッシュにあっても分かるようメッシュ名を付ける（欄が狭いときに名前が残るよう後ろに）
                shown++;
                string label = $"{shape.Name} ({shape.Renderer.name})";
                float max = GuiControls.Slider(label, shape.Max, BlendShapeLimit.MinWeight, BlendShapeLimit.MaxWeight, "F0");
                if (!Mathf.Approximately(max, shape.Max))
                {
                    limiter.SetMax(shape, Mathf.Round(max));
                    changed = true;
                }
            }

            // 1 件も無ければ理由を出す
            if (shown == 0)
            {
                GuiControls.Hint(face && limiter.FaceCount == 0
                    ? Loc.T("This avatar has no blend shapes moved by blink, mouth or expressions (see Others)",
                        "このアバターには、まばたき・口・表情で動く BlendShape がありません（その他を見てください）",
                        "이 아바타에는 눈 깜빡임·입·표정으로 움직이는 BlendShape가 없습니다 (기타를 확인하세요)",
                        "此虚拟形象没有由眨眼、嘴、表情驱动的 BlendShape（请查看其他）",
                        "此虛擬形象沒有由眨眼、嘴、表情驅動的 BlendShape（請查看其他）")
                    : Loc.T("No matching blend shapes", "該当する BlendShape がありません", "해당하는 BlendShape가 없습니다",
                        "没有符合的 BlendShape", "沒有符合的 BlendShape"));
            }

            // 出し切れなかった分は絞り込みを促す
            if (hidden > 0)
            {
                GuiControls.Hint(Loc.T($"{hidden} more. Narrow down with Search.",
                    $"ほかに {hidden} 件あります。検索で絞り込んでください。", $"그 외 {hidden}개가 있습니다. 검색으로 좁혀 주세요.",
                    $"还有 {hidden} 个。请用搜索缩小范围。", $"還有 {hidden} 個。請用搜尋縮小範圍。"));
            }

            return changed;
        }
    }
}
