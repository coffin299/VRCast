using System.Collections.Generic;
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

        // 共通接頭辞を切る位置の区切り文字
        private static readonly char[] PrefixSeparators = { '_', '-', ' ' };

        private readonly AvatarComponentCache _avatar;
        private readonly List<string> _labels = new List<string>();
        private PoseController _pose;
        private ExpressionController _expressions;
        private Vector2 _expressionScroll;

        public AnimationSection(AvatarSession session)
        {
            _avatar = new AvatarComponentCache(session);
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
            // アバターが替わったときだけ取り直す
            if (_avatar.Refresh())
            {
                _pose = _avatar.Get<PoseController>();
                _expressions = _avatar.Get<ExpressionController>();
                _expressionScroll = Vector2.zero;
                BuildLabels();
            }

            return _avatar.HasAvatar;
        }

        private void DrawPose()
        {
            GUILayout.Label("Pose");
            if (_pose == null)
            {
                return;
            }

            // アバターの向き（全アバター共通）
            _pose.BodyYaw = GuiControls.Slider("Body yaw", _pose.BodyYaw, -180f, 180f);

            // 非 Humanoid は腕の操作不可
            if (!_pose.IsAvailable)
            {
                GUILayout.Label("Arm pose requires a Humanoid avatar.");
                return;
            }

            // 腕と肘の度合い
            _pose.ArmDown = GuiControls.Slider("Arms down", _pose.ArmDown, 0f, 1f);
            _pose.ElbowBend = GuiControls.Slider("Elbow bend", _pose.ElbowBend, 0f, 1f);

            // よく使う 3 状態へのショートカット
            GUILayout.BeginHorizontal();

            // 気を付け（既定）: 腕を下ろし切り、肘はまっすぐ
            if (GUILayout.Button("Attention"))
            {
                _pose.ArmDown = 1f;
                _pose.ElbowBend = 0f;
            }

            // 腕を少し開き、肘を軽く曲げる
            if (GUILayout.Button("Relaxed"))
            {
                _pose.ArmDown = 0.85f;
                _pose.ElbowBend = 0.3f;
            }

            // 読込時の姿勢（通常 T ポーズ）
            if (GUILayout.Button("T-Pose"))
            {
                _pose.ArmDown = 0f;
                _pose.ElbowBend = 0f;
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
            // アバター切替時に作成済みの表示名
            return _labels[index];
        }

        private void BuildLabels()
        {
            _labels.Clear();
            if (_expressions == null)
            {
                return;
            }

            // 全表情に共通する接頭辞（区切り文字まで）を表示から省く
            IReadOnlyList<string> names = _expressions.Names;
            int prefixLength = CommonPrefixLength(names);
            for (int i = 0; i < names.Count; i++)
            {
                // 接頭辞を除き、長い名前は省略
                string name = names[i].Substring(prefixLength);
                if (name.Length > MaxButtonLabelLength)
                {
                    name = name.Substring(0, MaxButtonLabelLength - 1) + "…";
                }

                // ホットキー対象には番号を前置
                _labels.Add(i < 9 ? $"{i + 1}: {name}" : name);
            }
        }

        private static int CommonPrefixLength(IReadOnlyList<string> names)
        {
            // 1 件以下なら省略しない
            if (names.Count < 2)
            {
                return 0;
            }

            // 全名前で一致する先頭文字数を求める
            int length = names[0].Length;
            for (int i = 1; i < names.Count; i++)
            {
                int max = Mathf.Min(length, names[i].Length);
                int j = 0;
                while (j < max && names[i][j] == names[0][j])
                {
                    j++;
                }

                length = j;
            }

            // 単語の途中で切らないよう、最後の区切り文字の直後まで戻す
            int cut = names[0].LastIndexOfAny(PrefixSeparators, Mathf.Max(0, length - 1)) + 1;
            if (length == 0 || cut <= 0)
            {
                return 0;
            }

            // 名前が空になる場合は省略しない
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].Length <= cut)
                {
                    return 0;
                }
            }

            return cut;
        }
    }
}
