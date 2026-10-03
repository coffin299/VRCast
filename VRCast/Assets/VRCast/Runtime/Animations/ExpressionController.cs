using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// 表情プリセットを BlendShape に適用する。切り替え時は前の表情の影響を読込時の値へ戻してから適用する。
    /// 数字キー 1〜9 でプリセット、0 でニュートラル。
    /// </summary>
    public class ExpressionController : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Expression";

        // ホットキーで選べるプリセット数
        private const int HotkeyCount = 9;

        // 解決済みの 1 つの BlendShape 値
        private struct Target
        {
            public SkinnedMeshRenderer Renderer;
            public int Index;
            public float Weight;
        }

        private readonly List<string> _names = new List<string>();
        private readonly List<Target[]> _presets = new List<Target[]>();

        // プリセットが触る BlendShape の読込時の重み
        private readonly Dictionary<(SkinnedMeshRenderer, int), float> _baseline =
            new Dictionary<(SkinnedMeshRenderer, int), float>();

        // 選択中のプリセット（-1 = ニュートラル）
        public int Current { get; private set; } = -1;

        public IReadOnlyList<string> Names => _names;

        public void Initialize(Transform root, ExpressionSet expressions)
        {
            int unresolved = 0;
            foreach (ExpressionPreset preset in expressions.presets)
            {
                // パスと BlendShape 名を実体に解決
                var targets = new List<Target>();
                foreach (BlendShapeValue value in preset.values)
                {
                    if (TryResolve(root, value, out Target target))
                    {
                        targets.Add(target);
                    }
                    else
                    {
                        unresolved++;
                    }
                }

                // 1 つも解決できないプリセットは UI に出さない
                if (targets.Count > 0)
                {
                    _names.Add(preset.name);
                    _presets.Add(targets.ToArray());
                }
            }

            // EditorOnly 等で除去されたメッシュを指す値は無視される
            if (unresolved > 0)
            {
                VRCastLog.Warning(LogCategory, $"Skipped {unresolved} unresolved blend shape values.");
            }

            VRCastLog.Info(LogCategory, $"Loaded {_names.Count} expressions.");
        }

        private bool TryResolve(Transform root, BlendShapeValue value, out Target target)
        {
            target = default;

            // 空パスはルート自身
            Transform node = string.IsNullOrEmpty(value.path) ? root : root.Find(value.path);
            SkinnedMeshRenderer renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
            if (renderer == null || renderer.sharedMesh == null)
            {
                return false;
            }

            // BlendShape 名からインデックスを取得
            int index = renderer.sharedMesh.GetBlendShapeIndex(value.blendShape);
            if (index < 0)
            {
                return false;
            }

            // 初回だけ読込時の重みを記録
            if (!_baseline.ContainsKey((renderer, index)))
            {
                _baseline[(renderer, index)] = renderer.GetBlendShapeWeight(index);
            }

            target = new Target { Renderer = renderer, Index = index, Weight = value.weight };
            return true;
        }

        /// <summary>
        /// 指定プリセットを適用する。範囲外はニュートラル扱い。
        /// </summary>
        public void Apply(int presetIndex)
        {
            // 前の表情の影響を消す
            RestoreBaseline();

            // 範囲外ならニュートラルのまま
            if (presetIndex < 0 || presetIndex >= _presets.Count)
            {
                Current = -1;
                return;
            }

            // プリセットの重みを設定
            foreach (Target target in _presets[presetIndex])
            {
                target.Renderer.SetBlendShapeWeight(target.Index, target.Weight);
            }

            Current = presetIndex;
        }

        public void ResetToNeutral()
        {
            Apply(-1);
        }

        private void RestoreBaseline()
        {
            foreach (KeyValuePair<(SkinnedMeshRenderer, int), float> entry in _baseline)
            {
                // 破棄済みの Renderer は飛ばす
                if (entry.Key.Item1 != null)
                {
                    entry.Key.Item1.SetBlendShapeWeight(entry.Key.Item2, entry.Value);
                }
            }
        }

        private void Update()
        {
            // UI のテキスト入力中は数字キーを奪わない
            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            // 0 キーでニュートラル
            if (Input.GetKeyDown(KeyCode.Alpha0))
            {
                ResetToNeutral();
                return;
            }

            // 1〜9 キーで対応するプリセット
            int count = Mathf.Min(HotkeyCount, _presets.Count);
            for (int i = 0; i < count; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    Apply(i);
                    return;
                }
            }
        }
    }
}
