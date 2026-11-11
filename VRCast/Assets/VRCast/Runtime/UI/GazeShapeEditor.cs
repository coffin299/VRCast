using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// トラッキングタブの目の動きの上限の下で、視線に使う BlendShape をアバターごとに選び直す欄
    /// （上・下・左・右。自動の名前に当たらず、目の動きの上限が効かないアバター向け）。
    /// 候補はアバター内の全 BlendShape を名前で絞り込んで選ぶ（軽くするため見えている行だけを描く）。
    /// </summary>
    public sealed class GazeShapeEditor
    {
        // 候補の一覧の 1 行の高さと、一覧の欄の最大の高さ
        private const float RowHeight = 26f;
        private const float MaxListHeight = 240f;

        // 行のラベル・ボタンの幅
        private const float SlotLabelWidth = 130f;
        private const float SlotButtonWidth = 80f;

        // 選び直す対象。-1 は選んでいない
        private const int NoSlot = -1;
        private const int SlotUp = 0;
        private const int SlotDown = 1;
        private const int SlotLeft = 2;
        private const int SlotRight = 3;

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;

        // 表示中のアバターの指定と、そのアバターのパス（アバターが替わったら読み直す）
        private GazeShapeOverride _value;
        private string _valuePath;

        // 候補の一覧を開いている対象・検索文字列・スクロール位置
        private int _picking = NoSlot;
        private string _search = string.Empty;
        private Vector2 _scroll;

        // 検索に合う候補（アバター・検索文字列が変わったときだけ作り直す）と、作ったときの条件
        private readonly List<BlendShapeLimiter.Shape> _matches = new List<BlendShapeLimiter.Shape>();
        private (BlendShapeLimiter limiter, string search) _matchesKey;

        public GazeShapeEditor(AvatarSession session, AvatarComponentCache avatar, AppSettings settings)
        {
            _session = session;
            _avatar = avatar;
            _settings = settings;
        }

        public void Draw(FaceTrackingDriver driver)
        {
            // アバター未表示なら出さない（指定はアバターごと）
            var limiter = _avatar.Get<BlendShapeLimiter>();
            LoadedAvatar current = _session.Current;
            if (driver == null || limiter == null || current == null)
            {
                return;
            }

            // アバターが替わったら、そのアバターの指定を読み直し、開いていた一覧は閉じる
            if (_valuePath != current.SourcePath)
            {
                _valuePath = current.SourcePath;
                _value = _settings.GetGazeShapes(_valuePath);
                _picking = NoSlot;
            }

            // 指定の ON/OFF（OFF なら名前からの自動のまま）
            bool enabled = GUILayout.Toggle(_value.enabled, Loc.T(
                "Choose the eye-look blend shapes (this avatar only)", "目の動きの BlendShape を指定する（このアバターだけ）",
                "눈 움직임 BlendShape 지정 (이 아바타만)", "指定眼睛动作的 BlendShape（仅此虚拟形象）",
                "指定眼睛動作的 BlendShape（僅此虛擬形象）"));
            if (enabled != _value.enabled)
            {
                _value.enabled = enabled;
                Apply(driver);
            }

            if (!_value.enabled)
            {
                return;
            }

            GuiControls.Hint(Loc.T(
                "For avatars whose eyes ignore the eye movement limit. Chosen shapes move with that limit. Directions left empty stay automatic. Bones mode does not move these keys.",
                "目の動きの上限が効かないアバター向けです。選んだキーは、その上限で動きます。空の方向は今まで通りです。「目のトラッキング」がボーンのときは、このキーは動きません。",
                "눈 움직임 상한이 먹지 않는 아바타용입니다. 고른 키는 그 상한으로 움직입니다. 비운 방향은 지금까지와 같습니다. '눈 트래킹'이 본이면 이 키는 움직이지 않습니다.",
                "适用于眼睛动作上限无效的虚拟形象。所选键会按该上限移动。留空的方向保持自动。“眼睛追踪”为骨骼时，这些键不会动。",
                "適用於眼睛動作上限無效的虛擬形象。所選鍵會按該上限移動。留空的方向保持自動。「眼睛追蹤」為骨骼時，這些鍵不會動。"));
            DrawSlot(SlotUp, Loc.T("Look up", "上を見る", "위를 보기", "向上看", "向上看"), _value.up, driver);
            DrawSlot(SlotDown, Loc.T("Look down", "下を見る", "아래를 보기", "向下看", "向下看"), _value.down, driver);
            DrawSlot(SlotLeft, Loc.T("Look left", "左を見る", "왼쪽 보기", "向左看", "向左看"), _value.left, driver);
            DrawSlot(SlotRight, Loc.T("Look right", "右を見る", "오른쪽 보기", "向右看", "向右看"), _value.right, driver);

            // 選んだ名前がこのアバターに無ければ動かないことを伝える
            if (!_value.IsEmpty && !driver.HasCustomGazeShapes)
            {
                GuiControls.Warning(Loc.T("⚠ None of the chosen blend shapes were found, so the eyes do not use them.",
                    "⚠ 選んだ BlendShape がこのアバターに無いため、目の動きには使いません。",
                    "⚠ 선택한 BlendShape가 이 아바타에 없어 눈 움직임에 사용하지 않습니다.",
                    "⚠ 所选 BlendShape 不在此虚拟形象上，因此不会用于眼睛动作。",
                    "⚠ 所選 BlendShape 不在此虛擬形象上，因此不會用於眼睛動作。"));
            }

            if (_picking != NoSlot)
            {
                DrawPicker(limiter, driver);
            }
        }

        private void DrawSlot(int slot, string label, BlendShapeRef value, FaceTrackingDriver driver)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(SlotLabelWidth));
            UiTheme theme = UiTheme.Current;
            GUILayout.Label(value.IsSet ? value.blendShape : Loc.T("Auto", "自動", "자동", "自动", "自動"),
                theme != null ? theme.Value : GUI.skin.label, GuiControls.Shrinkable);

            bool open = _picking == slot;
            if (GUILayout.Button(open
                    ? Loc.T("Close", "閉じる", "닫기", "关闭", "關閉")
                    : Loc.T("Choose", "選ぶ", "선택", "选择", "選擇"), GUILayout.Width(SlotButtonWidth)))
            {
                _picking = open ? NoSlot : slot;
                _scroll = Vector2.zero;
            }

            GUI.enabled = value.IsSet;
            if (GUILayout.Button(Loc.T("Clear", "解除", "해제", "清除", "清除"), GUILayout.Width(SlotButtonWidth)))
            {
                value.Set(null, null);
                Apply(driver);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private void DrawPicker(BlendShapeLimiter limiter, FaceTrackingDriver driver)
        {
            GuiControls.SubHeading(Loc.T("Pick a blend shape", "BlendShape を選んでください", "BlendShape를 선택하세요",
                "请选择 BlendShape", "請選擇 BlendShape"));
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Search", "検索", "검색", "搜索", "搜尋"), GUILayout.ExpandWidth(false));
            _search = GUILayout.TextField(_search, GuiControls.Shrinkable);
            GUILayout.EndHorizontal();
            GuiControls.Hint(Loc.T("e.g. look, eye, up", "例: look、eye、上、目", "예: look, eye, up",
                "例：look、eye、up", "例：look、eye、up"));

            RefreshMatches(limiter);
            if (_matches.Count == 0)
            {
                GuiControls.Hint(Loc.T("No matching blend shapes", "該当する BlendShape がありません",
                    "해당하는 BlendShape가 없습니다", "没有符合的 BlendShape", "沒有符合的 BlendShape"));
                return;
            }

            float height = Mathf.Min(MaxListHeight, _matches.Count * RowHeight);
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Height(height));
            int first = Mathf.Clamp(Mathf.FloorToInt(_scroll.y / RowHeight), 0, _matches.Count);
            int last = Mathf.Min(_matches.Count, first + Mathf.CeilToInt(height / RowHeight) + 1);
            GUILayout.Space(first * RowHeight);

            BlendShapeLimiter.Shape chosen = null;
            for (int i = first; i < last; i++)
            {
                if (GUILayout.Button(_matches[i].Label, GUILayout.Height(RowHeight - 2f)))
                {
                    chosen = _matches[i];
                }
            }

            GUILayout.Space((_matches.Count - last) * RowHeight);
            GUILayout.EndScrollView();

            if (chosen != null)
            {
                SlotRef(_picking).Set(chosen.Path, chosen.Name);
                _picking = NoSlot;
                Apply(driver);
            }
        }

        private BlendShapeRef SlotRef(int slot)
        {
            return slot == SlotDown ? _value.down
                : slot == SlotLeft ? _value.left
                : slot == SlotRight ? _value.right
                : _value.up;
        }

        private void RefreshMatches(BlendShapeLimiter limiter)
        {
            var key = (limiter, _search);
            if (_matchesKey.Equals(key))
            {
                return;
            }

            _matchesKey = key;
            _matches.Clear();
            _scroll = Vector2.zero;
            foreach (BlendShapeLimiter.Shape shape in limiter.Shapes)
            {
                if (_search.Length == 0 || shape.Label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _matches.Add(shape);
                }
            }
        }

        private void Apply(FaceTrackingDriver driver)
        {
            _settings.SetGazeShapes(_valuePath, _value);
            driver.SetGazeShapes(_value);
        }
    }
}
