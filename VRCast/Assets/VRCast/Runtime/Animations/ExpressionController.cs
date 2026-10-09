using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.Animations
{
    /// <summary>
    /// 表情プリセットを BlendShape に適用する。切り替え時は前の表情から次の表情へ少しずつ変える（モーフィング）。
    /// プリセットに含まれない BlendShape は読込時の値へ戻す。
    /// ショートカットキーはアバターごとに割り当てたものだけ（既定はすべて未割り当て）。Windows の仮想キーと Ctrl / Alt / Shift の
    /// 組み合わせで判定し、設定によりウィンドウが前面に無いときも反応する。
    /// </summary>
    public class ExpressionController : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Expression";

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

        // 今の表情（ニュートラルへ戻り切るまで）をトラッキングの表情反映が出したなら true
        private bool _tracked;

        // プリセットが触る BlendShape に上限をかけていない間なら true（変わったら全て書き直す）
        private bool _exempt;
        private bool _rewrite;

        // ショートカットキー（先頭 = ニュートラル、以降はプリセットの並び。None = 未割り当て）と押下判定
        private KeyCombo[] _hotkeys = { KeyCombo.None };
        private readonly HotkeyPoller _hotkeyPoller = new HotkeyPoller();

        // 背面でも反応するかの設定（未設定なら前面のときだけ）
        private AppSettings _settings;

        // 選択中のプリセット（-1 = ニュートラル）
        public int Current { get; private set; } = -1;

        /// <summary>
        /// 手動（ボタン・ショートカットキー・外部操作）で選んだ表情を固定中なら true。
        /// 固定中はトラッキングの表情の自動検出で上書きしない。
        /// </summary>
        public bool IsManual { get; private set; }

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

            // ショートカットキーはニュートラルとプリセットの数だけ（すべて未割り当て）
            _hotkeys = new KeyCombo[_names.Count + 1];

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

            // 表情で動かす BlendShape として、上限の一覧の「顔」に出す
            BlendShapeLimiter.MarkFace(renderer, index);
            target = new Target { Slot = slot, Weight = value.weight };
            return true;
        }

        /// <summary>
        /// 指定プリセットへ切り替える（FadeSeconds かけて変える）。範囲外はニュートラル扱い。
        /// tracked はトラッキングの表情反映から出すとき true（設定により BlendShape の上限をかけない）。
        /// </summary>
        public void Apply(int presetIndex, bool tracked = false)
        {
            // 上限をかけるかは次の Update で反映する
            _tracked = tracked;

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

        /// <summary>
        /// 手動で表情を選んで固定する（範囲外はニュートラル）。
        /// toggle なら、固定中の表情をもう一度選んだときに固定を外して自動検出へ戻す。
        /// </summary>
        public void Select(int presetIndex, bool toggle)
        {
            // 範囲外はニュートラルとしてそろえる（固定中の表情と比べるため）
            int preset = presetIndex >= 0 && presetIndex < _presets.Count ? presetIndex : -1;

            // 固定中の同じ表情を選び直したら、自動検出へ戻す
            if (toggle && IsManual && Current == preset)
            {
                ReleaseManual();
                return;
            }

            Apply(preset);
            IsManual = true;
        }

        /// <summary>
        /// 手動の固定を外して自動検出へ戻す（いったんニュートラルにし、検出中なら次のフレームで検出した表情になる）。
        /// </summary>
        public void ReleaseManual()
        {
            // 固定していなければ今の表情のまま
            if (!IsManual)
            {
                return;
            }

            ResetToNeutral();
        }

        /// <summary>
        /// ニュートラルへ戻し、手動の固定も外す。
        /// </summary>
        public void ResetToNeutral()
        {
            IsManual = false;
            Apply(-1);
        }

        private void Update()
        {
            HandleHotkeys();
            UpdateExemption();
            Fade(Time.deltaTime);
        }

        private void UpdateExemption()
        {
            // トラッキングで出した表情で、設定 ON のときだけ上限をかけない（設定の切り替えもすぐ反映する）
            bool exempt = _tracked && _settings != null && _settings.trackingExpressionsIgnoreLimits;
            if (exempt == _exempt)
            {
                return;
            }

            // プリセットが触る全ての BlendShape を切り替え、今の値を新しい扱いで書き直す
            _exempt = exempt;
            foreach (Slot slot in _slots)
            {
                BlendShapeLimiter.SetExempt(slot.Renderer, slot.Index, exempt);
            }

            _rewrite = true;
            _fading = true;
        }

        /// <summary>
        /// 保存した割り当てを読み込む（今のアバターに無いプリセット名・使えないキーは無視）。
        /// settings は背面でも反応するかの設定を毎フレーム読むために持つ。
        /// </summary>
        public void LoadHotkeys(IReadOnlyList<ExpressionHotkey> hotkeys, AppSettings settings)
        {
            _settings = settings;

            // いったんすべて未割り当てにする
            for (int i = 0; i < _hotkeys.Length; i++)
            {
                _hotkeys[i] = KeyCombo.None;
            }

            foreach (ExpressionHotkey hotkey in hotkeys)
            {
                // 壊れた値は飛ばす
                if (hotkey == null || !hotkey.IsValid)
                {
                    continue;
                }

                // ニュートラルか、名前の一致するプリセットへ割り当てる（同じ組み合わせは後のものが勝つ）
                int preset = hotkey.preset == ExpressionHotkey.NeutralPreset ? -1 : _names.IndexOf(hotkey.preset);
                if (preset >= 0 || hotkey.preset == ExpressionHotkey.NeutralPreset)
                {
                    SetHotkey(preset, hotkey.Combo);
                }
            }
        }

        /// <summary>
        /// 割り当てを保存用の一覧にする（割り当てたものだけ）。
        /// </summary>
        public List<ExpressionHotkey> ExportHotkeys()
        {
            var hotkeys = new List<ExpressionHotkey>();
            for (int i = 0; i < _hotkeys.Length; i++)
            {
                // 未割り当ては保存しない
                if (!_hotkeys[i].IsAssigned)
                {
                    continue;
                }

                // 先頭はニュートラル、以降はプリセット名で保存（アバターを作り直しても名前で戻せる）
                string preset = i == 0 ? ExpressionHotkey.NeutralPreset : _names[i - 1];
                hotkeys.Add(ExpressionHotkey.From(preset, _hotkeys[i]));
            }

            return hotkeys;
        }

        /// <summary>
        /// プリセット（-1 = ニュートラル）のショートカットキーを返す（未割り当て・範囲外は None）。
        /// </summary>
        public KeyCombo GetHotkey(int presetIndex)
        {
            int slot = presetIndex + 1;
            return slot >= 0 && slot < _hotkeys.Length ? _hotkeys[slot] : KeyCombo.None;
        }

        /// <summary>
        /// プリセット（-1 = ニュートラル）にキーの組み合わせを割り当てる（None で解除）。同じ組み合わせを使っていた表情からは外す。
        /// </summary>
        public void SetHotkey(int presetIndex, KeyCombo combo)
        {
            // 範囲外・使えないキーは何もしない（未割り当ては解除として受け付ける）
            int slot = presetIndex + 1;
            if (slot < 0 || slot >= _hotkeys.Length || (combo.IsAssigned && !VirtualKeys.IsAssignable(combo.VirtualKey)))
            {
                return;
            }

            // 1 つの組み合わせで 1 つの表情だけを選ぶよう、ほかの表情の同じ組み合わせを外す
            if (combo.IsAssigned)
            {
                for (int i = 0; i < _hotkeys.Length; i++)
                {
                    if (_hotkeys[i].Equals(combo))
                    {
                        _hotkeys[i] = KeyCombo.None;
                    }
                }
            }

            _hotkeys[slot] = combo;
        }

        /// <summary>
        /// その組み合わせを割り当てたプリセット（-1 = ニュートラル）を探す。見つかれば true（未割り当ての組み合わせは false）。
        /// </summary>
        public bool TryFindHotkey(KeyCombo combo, out int presetIndex)
        {
            presetIndex = -1;

            // 未割り当ては探さない
            if (!combo.IsAssigned)
            {
                return false;
            }

            // 先頭はニュートラル、以降はプリセットの並び
            int slot = System.Array.IndexOf(_hotkeys, combo);
            presetIndex = slot - 1;
            return slot >= 0;
        }

        private void HandleHotkeys()
        {
            // 押された組み合わせの表情（先頭 = ニュートラル）で固定する（固定中の表情のキーをもう一度押すと自動検出へ戻す）
            bool background = _settings != null && _settings.expressionHotkeysInBackground;
            int pressed = _hotkeyPoller.Poll(_hotkeys, background);
            if (pressed >= 0)
            {
                Select(pressed - 1, true);
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
            bool rewrite = _rewrite;
            _rewrite = false;
            _fading = false;
            foreach (Slot slot in _slots)
            {
                // 破棄済みの Renderer と、（上限の扱いを変えた直後以外は）目標に着いているものは飛ばす
                if (slot.Renderer == null || (!rewrite && Mathf.Approximately(slot.Weight, slot.Goal)))
                {
                    continue;
                }

                // 目標へ近づけて（アバターごとの上限で切って）書き込み、まだ着かなければ次のフレームも続ける
                slot.Weight = Mathf.MoveTowards(slot.Weight, slot.Goal, step);
                slot.Renderer.SetBlendShapeWeight(slot.Index, BlendShapeLimiter.Limit(slot.Renderer, slot.Index, slot.Weight));
                _fading |= !Mathf.Approximately(slot.Weight, slot.Goal);
            }

            // ニュートラルへ戻り切ったら、読込時の値には再び上限をかける
            if (!_fading && Current < 0)
            {
                _tracked = false;
            }
        }
    }
}
