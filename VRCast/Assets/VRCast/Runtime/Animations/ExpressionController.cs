using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// 表情プリセットを BlendShape に適用する。切り替え時は前の表情から次の表情へ少しずつ変える（モーフィング）。
    /// プリセットに含まれない BlendShape は読込時の値へ戻す。数字キー 1〜9 でプリセット、0 でニュートラル。
    /// </summary>
    public class ExpressionController : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Expression";

        // ホットキーで選べるプリセット数
        private const int HotkeyCount = 9;

        // 切り替えにかける秒数（BlendShape が 0 から 100 まで変わる時間。差が小さいほど早く終わる）
        private const float FadeSeconds = 0.2f;

        // BlendShape の重みの最大値
        private const float MaxWeight = 100f;

        // プリセットが触る 1 つの BlendShape（読込時の重み・今の重み・目標の重み）
        private sealed class Slot
        {
            public SkinnedMeshRenderer Renderer;
            public int Index;
            public float Baseline;
            public float Weight;
            public float Goal;
        }

        // 解決済みの 1 つの BlendShape 値（Slot の位置と、プリセットでの重み）
        private struct Target
        {
            public int Slot;
            public float Weight;
        }

        private readonly List<string> _names = new List<string>();
        private readonly List<Target[]> _presets = new List<Target[]>();

        // プリセットが触る BlendShape の一覧と、実体から一覧の位置を引く表
        private readonly List<Slot> _slots = new List<Slot>();
        private readonly Dictionary<(SkinnedMeshRenderer, int), int> _slotIndex =
            new Dictionary<(SkinnedMeshRenderer, int), int>();

        // 目標へ向けて変化中なら true
        private bool _fading;

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

            // パスと BlendShape 名から実体を探す
            if (!BlendShapeOverlay.TryFind(root, value.path, value.blendShape,
                    out SkinnedMeshRenderer renderer, out int index))
            {
                return false;
            }

            // 初めて触る BlendShape なら、読込時の重みを今の重み・目標として登録
            if (!_slotIndex.TryGetValue((renderer, index), out int slot))
            {
                float weight = renderer.GetBlendShapeWeight(index);
                slot = _slots.Count;
                _slots.Add(new Slot { Renderer = renderer, Index = index, Baseline = weight, Weight = weight, Goal = weight });
                _slotIndex[(renderer, index)] = slot;
            }

            target = new Target { Slot = slot, Weight = value.weight };
            return true;
        }

        /// <summary>
        /// 指定プリセットへ切り替える（FadeSeconds かけて変える）。範囲外はニュートラル扱い。
        /// </summary>
        public void Apply(int presetIndex)
        {
            // まず全ての目標を読込時の値にする（前の表情の影響を消す）
            foreach (Slot slot in _slots)
            {
                slot.Goal = slot.Baseline;
            }

            // 範囲内ならプリセットの重みを目標にする（範囲外はニュートラルのまま）
            bool valid = presetIndex >= 0 && presetIndex < _presets.Count;
            if (valid)
            {
                foreach (Target target in _presets[presetIndex])
                {
                    _slots[target.Slot].Goal = target.Weight;
                }
            }

            Current = valid ? presetIndex : -1;
            _fading = true;
        }

        public void ResetToNeutral()
        {
            Apply(-1);
        }

        private void Update()
        {
            HandleHotkeys();
            Fade(Time.deltaTime);
        }

        private void HandleHotkeys()
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

        private void Fade(float deltaTime)
        {
            // 変化中でなければ書き込まない（まばたき・口パクの上乗せを毎フレーム邪魔しない）
            if (!_fading)
            {
                return;
            }

            // 1 フレームで動かせる量（一定の速さで目標へ寄せる）
            float step = MaxWeight * deltaTime / FadeSeconds;
            _fading = false;
            foreach (Slot slot in _slots)
            {
                // 目標に着いている・破棄済みの Renderer は飛ばす
                if (Mathf.Approximately(slot.Weight, slot.Goal) || slot.Renderer == null)
                {
                    continue;
                }

                // 目標へ近づけて書き込み、まだ着かなければ次のフレームも続ける
                slot.Weight = Mathf.MoveTowards(slot.Weight, slot.Goal, step);
                slot.Renderer.SetBlendShapeWeight(slot.Index, slot.Weight);
                _fading |= !Mathf.Approximately(slot.Weight, slot.Goal);
            }
        }
    }
}
