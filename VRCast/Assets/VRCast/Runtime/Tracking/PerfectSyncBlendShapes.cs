using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// パーフェクトシンク: MediaPipe の ARKit 互換 BlendShape の値を、アバターの同名 BlendShape へそのまま書き込む。
    /// 名前は大文字小文字・区切り記号・FBX の接頭辞（"blendShape1." 等）を無視し、末尾の Left / Right は L / R も受け付ける。
    /// 同名の BlendShape が複数のメッシュ（顔・歯・舌など）にあれば全部動かす。
    /// 表情プリセット等の値を壊さないよう BlendShapeOverlay で「元の値と大きい方」を書く。
    /// </summary>
    public sealed class PerfectSyncBlendShapes
    {
        /// <summary>
        /// パーフェクトシンク対応とみなす、見つかった BlendShape の種類数の下限
        /// （まばたき用の eyeBlinkLeft / Right だけを持つアバターを対象にしないため）。
        /// </summary>
        public const int MinMatchedShapes = 20;

        // 値の追従速度（大きいほど速い、1 秒あたり。検出の細かな揺れで顔が震えないように）。
        // 目標との差が FastDifference 以上なら速め（口を開ける・まばたきなどの大きな動きで遅れない）
        private const float BaseSmoothing = 30f;
        private const float FastSmoothing = 60f;
        private const float FastDifference = 0.3f;

        /// <summary>
        /// 動きのなめらかさ（高いほどゆっくり寄せる）。
        /// </summary>
        public MotionSmoothing Smoothing { get; set; }

        // ARKit 名（正規化済み）→ BlendShapeNames 内の位置
        private static readonly Dictionary<string, int> NameToIndex = BuildNameTable();

        // 左右を入れ替えた相手の位置（左右の無い名前は自分自身）
        private static readonly int[] Opposite = BuildOpposites();

        // まばたき・顎の開きの位置（既存のまばたき・口パクと二重に動かさないための判定と、値の補正に使う）
        private static readonly int EyeBlinkLeft = ArKitFace.EyeBlinkLeft;
        private static readonly int EyeBlinkRight = ArKitFace.EyeBlinkRight;
        private static readonly int JawOpen = ArKitFace.JawOpen;

        // ARKit の各名前に対応するアバター側の書き込み先（無ければ null）
        private readonly List<Target>[] _targets;

        // 書き込む BlendShape（同じ BlendShape は 1 つにまとめる）と、今のフレームで書く値・書いたか・前のフレームで書いたか。
        // 両目まとめのまばたき・笑顔などに左右の ARKit 名を割り当てると、別々の上乗せが互いの値を元の値として取り込み、
        // 値が下がらなくなる（半目のまま残る）ため、名前ごとの値の大きい方を BlendShape ごとに 1 回だけ書く
        private readonly BlendShapeOverlay[] _slots;
        private readonly float[] _slotValues;
        private readonly bool[] _slotUsed;
        private readonly bool[] _slotActive;

        // 平滑化済みの値（0〜1、アバター側の左右）
        private readonly float[] _current;

        // 書き込み中なら true（解除時に一度だけ元の値へ戻すため）
        private bool _active;

        /// <summary>
        /// 書き込み先 1 つ（_slots の番号）と、その強さ（割り当てた既存の BlendShape は強さを変えられる。名前で見つけたものは 1）。
        /// </summary>
        private readonly struct Target
        {
            public readonly int Slot;
            public readonly float Scale;

            public Target(int slot, float scale)
            {
                Slot = slot;
                Scale = scale;
            }
        }

        /// <summary>
        /// 書き込む BlendShape の一覧を作る途中の状態（同じレンダラー・番号は同じ番号にする）。
        /// </summary>
        private sealed class SlotTable
        {
            public readonly List<BlendShapeOverlay> Overlays = new List<BlendShapeOverlay>();
            private readonly Dictionary<(SkinnedMeshRenderer, int), int> _byShape =
                new Dictionary<(SkinnedMeshRenderer, int), int>();

            public int Get(SkinnedMeshRenderer renderer, int index)
            {
                // 初めての BlendShape なら上乗せ用オブジェクトを作る
                if (!_byShape.TryGetValue((renderer, index), out int slot))
                {
                    slot = Overlays.Count;
                    Overlays.Add(BlendShapeOverlay.Create(renderer, index));
                    _byShape[(renderer, index)] = slot;
                }

                return slot;
            }
        }

        private PerfectSyncBlendShapes(List<Target>[] targets, List<BlendShapeOverlay> slots, int matched)
        {
            _targets = targets;
            _slots = slots.ToArray();
            _slotValues = new float[_slots.Length];
            _slotUsed = new bool[_slots.Length];
            _slotActive = new bool[_slots.Length];
            _current = new float[targets.Length];
            MatchedCount = matched;
        }

        /// <summary>
        /// 見つかった ARKit 名の種類数（0〜BlendShapeNames.Length）。
        /// </summary>
        public int MatchedCount { get; }

        /// <summary>
        /// パーフェクトシンクとして使えるなら true（MinMatchedShapes 種類以上ある）。
        /// </summary>
        public bool IsAvailable => MatchedCount >= MinMatchedShapes;

        /// <summary>
        /// 有効にする条件を満たすなら true（anyShape なら 1 種類でもあれば、そうでなければ IsAvailable と同じ）。
        /// </summary>
        public bool IsAvailableFor(bool anyShape)
        {
            // 条件に応じて必要な種類数を切り替える
            return MatchedCount >= (anyShape ? 1 : MinMatchedShapes);
        }

        /// <summary>
        /// まばたき（eyeBlinkLeft / Right の両方）を動かせるなら true（その間は既存のまばたきを開いたままにする）。
        /// </summary>
        public bool DrivesBlink => _targets[EyeBlinkLeft] != null && _targets[EyeBlinkRight] != null;

        /// <summary>
        /// 口の開き（jawOpen）を動かせるなら true（その間はカメラによる口パクを止める）。
        /// </summary>
        public bool DrivesJaw => _targets[JawOpen] != null;

        /// <summary>
        /// 目の動き（eyeLook*）のいずれかを動かせるなら true（その間は視線用 BlendShape を二重に動かさない）。
        /// </summary>
        public bool DrivesEyeLook
        {
            get
            {
                string[] names = ArKitFace.BlendShapeNames;
                for (int i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i] != null && names[i].StartsWith("eyeLook", System.StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// root 以下の全 SkinnedMeshRenderer から ARKit 名の BlendShape を探す。
        /// bindings（パーフェクトシンク化で既存の BlendShape を ARKit 名に割り当てたもの）があれば、その ARKit 名は割り当てを使い、
        /// 残りの ARKit 名を名前の照合で探す。
        /// </summary>
        public static PerfectSyncBlendShapes Create(Transform root, IReadOnlyList<PerfectSyncBinding> bindings = null)
        {
            var targets = new List<Target>[ArKitFace.BlendShapeNames.Length];
            var slots = new SlotTable();
            int matched = AddBindings(root, bindings, targets, slots);
            var bound = new bool[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                bound[i] = targets[i] != null;
            }

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // メッシュの無いレンダラーは対象外
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    // ARKit 名でない・割り当て済みの ARKit 名は対象外
                    int index = Match(mesh.GetBlendShapeName(shape));
                    if (index < 0 || bound[index])
                    {
                        continue;
                    }

                    // 初めて見つかった名前なら種類数を数える
                    if (targets[index] == null)
                    {
                        targets[index] = new List<Target>();
                        matched++;
                    }

                    targets[index].Add(new Target(slots.Get(renderer, shape), 1f));
                }
            }

            return new PerfectSyncBlendShapes(targets, slots.Overlays, matched);
        }

        private static int AddBindings(Transform root, IReadOnlyList<PerfectSyncBinding> bindings, List<Target>[] targets,
            SlotTable slots)
        {
            // 割り当てが無ければ何もしない
            int matched = 0;
            if (bindings == null)
            {
                return matched;
            }

            foreach (PerfectSyncBinding binding in bindings)
            {
                // 知らない ARKit 名・見つからない BlendShape・強さ 0 は飛ばす
                int index = binding != null ? Array.IndexOf(ArKitFace.BlendShapeNames, binding.name) : -1;
                if (index < 0 || binding.weight <= 0f)
                {
                    continue;
                }

                if (!BlendShapeOverlay.TryFind(root, binding.path, binding.blendShape,
                        out SkinnedMeshRenderer renderer, out int shape))
                {
                    continue;
                }

                // 初めて割り当てた名前なら種類数を数える
                if (targets[index] == null)
                {
                    targets[index] = new List<Target>();
                    matched++;
                }

                targets[index].Add(new Target(slots.Get(renderer, shape), binding.weight));
            }

            return matched;
        }

        /// <summary>
        /// BlendShape 名に対応する ARKit 名の位置（BlendShapeNames 内）。対応しなければ -1。
        /// </summary>
        public static int Match(string blendShapeName)
        {
            // 空の名前は対象外
            if (string.IsNullOrEmpty(blendShapeName))
            {
                return -1;
            }

            // FBX の取り込みで付く "blendShape1." のような接頭辞を外してから正規化
            int dot = blendShapeName.LastIndexOf('.');
            string name = Normalize(dot >= 0 ? blendShapeName.Substring(dot + 1) : blendShapeName);
            return NameToIndex.TryGetValue(name, out int index) ? index : -1;
        }

        /// <summary>
        /// アバター側の ARKit 名の位置に対応する、受信値（映像基準の左右）の位置。
        /// MediaPipe の左右は映像上の左右（本人とは逆）なので、鏡像 OFF（本人の左 = アバターの左）では入れ替え、
        /// 鏡像 ON（本人の右 = アバターの左）ではそのまま使う。
        /// </summary>
        public static int SourceIndex(int avatarIndex, bool mirror)
        {
            return mirror ? avatarIndex : Opposite[avatarIndex];
        }

        /// <summary>
        /// 受信値を書き込む（毎フレーム呼ぶ）。range は入力元のまばたきの値の範囲。
        /// drivesBlink が false なら eyeBlinkLeft / Right は元の値へ戻して書かない（自動まばたきに任せる）。
        /// eyes はまばたきの補正（目の開きの補正・左右をそろえる補正。既定値なら補正なし）。
        /// boost は口・頬の強調（既定値なら強調なし）。jaw は口の開きの補正（既定値なら補正なし）。
        /// </summary>
        public void Apply(
            float[] scores, ArKitRange range, bool mirror, float deltaTime, bool drivesBlink, EyeAdjustment eyes = default,
            PerfectSyncBoost boost = default, JawOpenAdjustment jaw = default)
        {
            // 要素数が合わない値は使わない
            if (scores == null || scores.Length != _targets.Length)
            {
                return;
            }

            _active = true;

            // まばたきは閉じ切るよう範囲を広げた閉じ具合にしてから、左右を一緒に補正する
            float blinkLeft = range.BlinkToClosed(Mathf.Clamp01(scores[SourceIndex(EyeBlinkLeft, mirror)]));
            float blinkRight = range.BlinkToClosed(Mathf.Clamp01(scores[SourceIndex(EyeBlinkRight, mirror)]));
            eyes.Apply(ref blinkLeft, ref blinkRight);
            Array.Clear(_slotUsed, 0, _slotUsed.Length);
            for (int i = 0; i < _targets.Length; i++)
            {
                // アバターに無い名前は計算しない
                if (_targets[i] == null)
                {
                    continue;
                }

                // まばたきを動かさない間は書かない（同じ BlendShape への自動まばたきと競合させない。書いていた分は下で一度だけ戻す）
                bool blinkShape = i == EyeBlinkLeft || i == EyeBlinkRight;
                if (blinkShape && !drivesBlink)
                {
                    _current[i] = 0f;
                    continue;
                }

                // 左右を合わせた値（まばたきは補正済みの閉じ具合、口の開きは補正後、口・頬は強調後）を平滑化して 0〜100 にする
                float target = i == EyeBlinkLeft ? blinkLeft
                    : i == EyeBlinkRight ? blinkRight
                    : i == JawOpen ? jaw.Apply(Mathf.Clamp01(scores[SourceIndex(i, mirror)]))
                    : boost.Apply(i, Mathf.Clamp01(scores[SourceIndex(i, mirror)]));

                float blend = MotionSmoothingLevels.FollowBlend(Smoothing, Mathf.Abs(target - _current[i]),
                    FastDifference, BaseSmoothing, FastSmoothing, deltaTime);
                _current[i] = Mathf.Lerp(_current[i], target, blend);
                Collect(i, _current[i] * 100f);
            }

            WriteSlots();
        }

        /// <summary>
        /// 書き込みをやめて元の値へ戻す（書き込み中だったときだけ働く）。
        /// </summary>
        public void Release()
        {
            // 既に戻していれば何もしない
            if (!_active)
            {
                return;
            }

            _active = false;

            // 次に使うときは 0 から追従させ、全部の BlendShape を上乗せ 0 で元の値へ戻す
            Array.Clear(_current, 0, _current.Length);
            for (int s = 0; s < _slots.Length; s++)
            {
                _slots[s].Write(0f);
                _slotActive[s] = false;
            }
        }

        private void Collect(int index, float weight)
        {
            // 同じ名前の全メッシュへ、それぞれの強さを掛けた値を控える（同じ BlendShape に複数の名前があれば大きい方）
            foreach (Target target in _targets[index])
            {
                float value = weight * target.Scale;
                if (!_slotUsed[target.Slot] || value > _slotValues[target.Slot])
                {
                    _slotValues[target.Slot] = value;
                }

                _slotUsed[target.Slot] = true;
            }
        }

        private void WriteSlots()
        {
            // 今のフレームで値のある BlendShape だけを 1 回ずつ書き、書かなくなった BlendShape は一度だけ元の値へ戻す
            for (int s = 0; s < _slots.Length; s++)
            {
                if (_slotUsed[s])
                {
                    _slots[s].Write(_slotValues[s]);
                    _slotActive[s] = true;
                }
                else if (_slotActive[s])
                {
                    _slots[s].Write(0f);
                    _slotActive[s] = false;
                }
            }
        }

        private static string Normalize(string name)
        {
            // 英数字だけを小文字で残す（"Eye_Blink_Left" と "eyeBlinkLeft" を同じにする）
            var chars = new char[name.Length];
            int length = 0;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars[length++] = char.ToLowerInvariant(c);
                }
            }

            return new string(chars, 0, length);
        }

        private static Dictionary<string, int> BuildNameTable()
        {
            var table = new Dictionary<string, int>();
            string[] names = ArKitFace.BlendShapeNames;
            for (int i = 0; i < names.Length; i++)
            {
                // 正式な名前と、末尾の Left / Right を L / R にした名前（"eyeBlink_L" 等）を登録
                string normalized = Normalize(names[i]);
                table[normalized] = i;
                if (normalized.EndsWith("left", StringComparison.Ordinal))
                {
                    table[normalized.Substring(0, normalized.Length - 4) + "l"] = i;
                }
                else if (normalized.EndsWith("right", StringComparison.Ordinal))
                {
                    table[normalized.Substring(0, normalized.Length - 5) + "r"] = i;
                }
            }

            return table;
        }

        private static int[] BuildOpposites()
        {
            string[] names = ArKitFace.BlendShapeNames;
            var opposites = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                // 名前の Left と Right を入れ替えた相手を探す（無ければ自分自身）
                string name = names[i];
                string swapped = name.EndsWith("Left", StringComparison.Ordinal)
                    ? name.Substring(0, name.Length - 4) + "Right"
                    : name.EndsWith("Right", StringComparison.Ordinal)
                        ? name.Substring(0, name.Length - 5) + "Left"
                        : name;
                int index = Array.IndexOf(names, swapped);
                opposites[i] = index >= 0 ? index : i;
            }

            return opposites;
        }
    }
}
