using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;

namespace VRCast.UI
{
    /// <summary>
    /// MainPanel 内の Pose / Expressions セクション。表示中アバターのコントローラーを操作する。
    /// </summary>
    public class AnimationSection
    {
        // 表情ボタンの列数と一覧の最大高さ
        private const int ExpressionColumns = 3;
        private const float ExpressionListHeight = 150f;

        // ボタンに表示する名前の最大文字数
        private const int MaxButtonLabelLength = 14;

        private readonly AvatarSession _session;
        private GameObject _cachedInstance;
        private PoseController _pose;
        private ExpressionController _expressions;
        private Vector2 _expressionScroll;

        public AnimationSection(AvatarSession session)
        {
            _session = session;
        }

        public void Draw()
        {
            // アバター未表示なら何も出さない
            if (!RefreshControllers())
            {
                return;
            }

            DrawPose();
            GUILayout.Space(8f);
            DrawExpressions();
        }

        private bool RefreshControllers()
        {
            GameObject instance = _session.Current?.Instance;

            // アバターが替わったときだけ取り直す
            if (instance != _cachedInstance)
            {
                _cachedInstance = instance;
                _pose = instance != null ? instance.GetComponent<PoseController>() : null;
                _expressions = instance != null ? instance.GetComponent<ExpressionController>() : null;
                _expressionScroll = Vector2.zero;
            }

            return instance != null;
        }

        private void DrawPose()
        {
            GUILayout.Label("Pose");

            // 非 Humanoid は操作不可
            if (_pose == null || !_pose.IsAvailable)
            {
                GUILayout.Label("Pose control requires a Humanoid avatar.");
                return;
            }

            // 腕と肘の度合い
            _pose.ArmDown = GuiControls.Slider("Arms down", _pose.ArmDown, 0f, 1f);
            _pose.ElbowBend = GuiControls.Slider("Elbow bend", _pose.ElbowBend, 0f, 1f);

            // よく使う 2 状態へのショートカット
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("T-Pose"))
            {
                _pose.ArmDown = 0f;
                _pose.ElbowBend = 0f;
            }

            if (GUILayout.Button("Relaxed"))
            {
                _pose.ArmDown = 1f;
                _pose.ElbowBend = 0.3f;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawExpressions()
        {
            GUILayout.Label("Expressions (keys 1-9, 0: neutral)");

            // 表情データが無いアバター
            if (_expressions == null || _expressions.Names.Count == 0)
            {
                GUILayout.Label("No expressions in this package.");
                return;
            }

            // ニュートラルは常に先頭
            if (GUILayout.Toggle(_expressions.Current < 0, "Neutral", GUI.skin.button))
            {
                if (_expressions.Current >= 0)
                {
                    _expressions.ResetToNeutral();
                }
            }

            // 件数が多いアバター向けにスクロール
            _expressionScroll = GUILayout.BeginScrollView(
                _expressionScroll, GUILayout.Height(ExpressionListHeight));
            for (int i = 0; i < _expressions.Names.Count; i++)
            {
                // 行の開始
                if (i % ExpressionColumns == 0)
                {
                    GUILayout.BeginHorizontal();
                }

                // 選択中はトグル表示、押されたら適用
                bool selected = _expressions.Current == i;
                if (GUILayout.Toggle(selected, Label(i), GUI.skin.button) && !selected)
                {
                    _expressions.Apply(i);
                }

                // 行の終了（最終要素でも閉じる）
                if (i % ExpressionColumns == ExpressionColumns - 1 || i == _expressions.Names.Count - 1)
                {
                    GUILayout.EndHorizontal();
                }
            }

            GUILayout.EndScrollView();
        }

        private string Label(int index)
        {
            // ホットキー番号を前置し、長い名前は省略
            string name = _expressions.Names[index];
            if (name.Length > MaxButtonLabelLength)
            {
                name = name.Substring(0, MaxButtonLabelLength - 1) + "…";
            }

            return index < 9 ? $"{index + 1}: {name}" : name;
        }
    }
}
