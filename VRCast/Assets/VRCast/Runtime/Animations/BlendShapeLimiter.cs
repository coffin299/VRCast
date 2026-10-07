using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// アバターごとの BlendShape の上限（まばたきで目が消える等、100 まで動かすと破綻する形の対策）。
    /// まばたき・口パク・表情・パーフェクトシンクは書き込む前に Limit を通す。
    /// どの処理も書かない固定の値は、全ての処理の後（LateUpdate の最後）に上限で切り、上限を緩めたら元の値へ戻す。
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class BlendShapeLimiter : MonoBehaviour
    {
        // まばたき・トラッキング等の LateUpdate より後に動かす
        private const int ExecutionOrder = 10000;

        /// <summary>
        /// アバター内の 1 つの BlendShape と、その上限。
        /// </summary>
        public sealed class Shape
        {
            public SkinnedMeshRenderer Renderer;
            public int Index;
            public string Path;
            public string Name;

            // 一覧の表示名（同じ名前が複数のメッシュにあっても分かるようメッシュ名付き。毎フレーム作らないよう列挙時に作る）
            public string Label;
            public bool IsFace;
            public float Max = BlendShapeLimit.MaxWeight;

            // 上限で切る前の固定の値（切っていなければ NaN。上限を緩めたときに戻す）
            public float Original = float.NaN;

            /// <summary>
            /// 上限を付けているなら true。
            /// </summary>
            public bool IsLimited => Max < BlendShapeLimit.MaxWeight;
        }

        // 表示中のアバターの上限（同時に表示するアバターは 1 体だけ）
        private static BlendShapeLimiter _active;

        private readonly List<Shape> _shapes = new List<Shape>();
        private readonly List<Shape> _limited = new List<Shape>();
        private readonly Dictionary<(SkinnedMeshRenderer, int), Shape> _lookup =
            new Dictionary<(SkinnedMeshRenderer, int), Shape>();

        /// <summary>
        /// アバター内の全 BlendShape（メッシュ順）。
        /// </summary>
        public IReadOnlyList<Shape> Shapes => _shapes;

        /// <summary>
        /// 上限を付けている BlendShape の数。
        /// </summary>
        public int LimitedCount => _limited.Count;

        /// <summary>
        /// 顔として動かす BlendShape（まばたき・口・表情・パーフェクトシンク）の数。
        /// </summary>
        public int FaceCount { get; private set; }

        /// <summary>
        /// 一覧を作り直した回数（Rescan のたびに増える。一覧を控えている側が作り直しに気付くため）。
        /// </summary>
        public int Version { get; private set; }

        // 列挙の起点と、読み込み時に渡された保存済みの上限（Rescan で新しく見つかった BlendShape に当てる）
        private Transform _root;
        private List<BlendShapeLimit> _savedLimits = new List<BlendShapeLimit>();

        /// <summary>
        /// 顔として動かす BlendShape として登録する（まばたき・口パク・パーフェクトシンクの上乗せと、表情プリセットの対象）。
        /// 一覧の「顔」に出すための区別で、上限の効き方は変わらない。
        /// </summary>
        public static void MarkFace(SkinnedMeshRenderer renderer, int index)
        {
            // アバター未表示・一覧に無い BlendShape・登録済みは何もしない
            if (_active == null || !_active._lookup.TryGetValue((renderer, index), out Shape shape) || shape.IsFace)
            {
                return;
            }

            shape.IsFace = true;
            _active.FaceCount++;
        }

        /// <summary>
        /// 書き込む値を上限で切る（上限の無い BlendShape・アバター未表示ならそのまま返す）。
        /// </summary>
        public static float Limit(SkinnedMeshRenderer renderer, int index, float weight)
        {
            // 上限の無い BlendShape はそのまま
            if (_active == null || !_active._lookup.TryGetValue((renderer, index), out Shape shape))
            {
                return weight;
            }

            // 書き込む処理がある BlendShape は、その処理の値が正なので固定の値として戻さない
            shape.Original = float.NaN;
            return Mathf.Min(weight, shape.Max);
        }

        /// <summary>
        /// 今の値を読む。固定の値を上限で切っている間は切る前の値を返す
        /// （BlendShapeOverlay が上限で切った値を「元の値」と取り違えないため）。
        /// </summary>
        public static float Read(SkinnedMeshRenderer renderer, int index)
        {
            float weight = renderer.GetBlendShapeWeight(index);
            if (_active != null && _active._lookup.TryGetValue((renderer, index), out Shape shape)
                && !float.IsNaN(shape.Original) && Mathf.Approximately(weight, shape.Max))
            {
                return shape.Original;
            }

            return weight;
        }

        /// <summary>
        /// アバター内の BlendShape を列挙し、記録済みの上限を当てる。
        /// 表情・まばたき等より先に初期化する（それらが MarkFace で顔の BlendShape を登録するため）。
        /// </summary>
        public void Initialize(Transform root, List<BlendShapeLimit> limits)
        {
            _active = this;
            _root = root;
            _savedLimits = limits ?? new List<BlendShapeLimit>();

            // 全メッシュの BlendShape を列挙し、記録済みの上限を当てる
            Enumerate();
            ApplySavedLimits(null);
        }

        /// <summary>
        /// メッシュの差し替え（パーフェクトシンクの形状の追加・削除）の後に一覧を作り直す。
        /// 同じパス・名前の BlendShape は上限と「顔」の区別を引き継ぎ、新しく見つかったものには保存済みの上限を当てる。
        /// </summary>
        public void Rescan()
        {
            // 今の上限と区別をパス・名前で控える
            var previous = new Dictionary<(string, string), Shape>();
            foreach (Shape shape in _shapes)
            {
                previous[(shape.Path, shape.Name)] = shape;
            }

            // 上限で切っていた固定の値を戻す（差し替えで無くなった BlendShape は除く）
            foreach (Shape shape in _limited)
            {
                if (!float.IsNaN(shape.Original) && shape.Renderer != null && shape.Renderer.sharedMesh != null
                    && shape.Index < shape.Renderer.sharedMesh.blendShapeCount)
                {
                    shape.Renderer.SetBlendShapeWeight(shape.Index, shape.Original);
                }
            }

            // 列挙し直す
            _shapes.Clear();
            _limited.Clear();
            _lookup.Clear();
            FaceCount = 0;
            Enumerate();

            // 引き継ぎ（前から有ったもの）
            foreach (Shape shape in _shapes)
            {
                if (!previous.TryGetValue((shape.Path, shape.Name), out Shape old))
                {
                    continue;
                }

                if (old.IsFace)
                {
                    shape.IsFace = true;
                    FaceCount++;
                }

                SetMax(shape, old.Max);
            }

            // 新しく見つかったものには保存済みの上限
            ApplySavedLimits(previous);
            Version++;
        }

        private void Enumerate()
        {
            // 全メッシュの BlendShape を列挙
            foreach (SkinnedMeshRenderer renderer in _root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // メッシュの無いレンダラーは対象外
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                string path = PathOf(renderer.transform, _root);
                string meshName = renderer.name;
                for (int index = 0; index < mesh.blendShapeCount; index++)
                {
                    string name = mesh.GetBlendShapeName(index);
                    var shape = new Shape
                    {
                        Renderer = renderer, Index = index, Path = path, Name = name, Label = $"{name} ({meshName})",
                    };
                    _shapes.Add(shape);
                    _lookup[(renderer, index)] = shape;
                }
            }
        }

        private void ApplySavedLimits(Dictionary<(string, string), Shape> skip)
        {
            // 記録済みの上限を当てる（見つからない BlendShape・引き継いだものは無視）
            foreach (BlendShapeLimit limit in _savedLimits)
            {
                if (skip != null && skip.ContainsKey((limit.path, limit.blendShape)))
                {
                    continue;
                }

                Shape shape = _shapes.Find(s => s.Path == limit.path && s.Name == limit.blendShape);
                if (shape != null)
                {
                    SetMax(shape, limit.max);
                }
            }
        }

        /// <summary>
        /// 上限を変える（100 で制限なし）。固定の値を切っていたら元へ戻し、次の LateUpdate で新しい上限で切り直す。
        /// </summary>
        public void SetMax(Shape shape, float max)
        {
            // 範囲内に制限し、変わらなければ何もしない
            max = Mathf.Clamp(max, BlendShapeLimit.MinWeight, BlendShapeLimit.MaxWeight);
            if (Mathf.Approximately(shape.Max, max))
            {
                return;
            }

            // 上限で切っていた固定の値を戻す
            if (!float.IsNaN(shape.Original) && shape.Renderer != null)
            {
                shape.Renderer.SetBlendShapeWeight(shape.Index, shape.Original);
            }

            shape.Original = float.NaN;
            shape.Max = max;

            // 上限を付けているものだけを毎フレーム確認する
            _limited.Remove(shape);
            if (shape.IsLimited)
            {
                _limited.Add(shape);
            }
        }

        /// <summary>
        /// 全ての上限を外す。
        /// </summary>
        public void ClearAll()
        {
            // SetMax が一覧から外すので複製を回す
            foreach (Shape shape in _limited.ToArray())
            {
                SetMax(shape, BlendShapeLimit.MaxWeight);
            }
        }

        /// <summary>
        /// 保存用に、上限を付けている BlendShape を書き出す。
        /// </summary>
        public List<BlendShapeLimit> Export()
        {
            return _limited.ConvertAll(shape => new BlendShapeLimit
            {
                path = shape.Path, blendShape = shape.Name, max = shape.Max,
            });
        }

        private void LateUpdate()
        {
            foreach (Shape shape in _limited)
            {
                // 破棄済みのメッシュは飛ばす
                if (shape.Renderer == null)
                {
                    continue;
                }

                // 上限を超えていれば（どの処理も書かない固定の値）、元の値を覚えて上限で切る
                float weight = shape.Renderer.GetBlendShapeWeight(shape.Index);
                if (weight > shape.Max)
                {
                    if (float.IsNaN(shape.Original))
                    {
                        shape.Original = weight;
                    }

                    shape.Renderer.SetBlendShapeWeight(shape.Index, shape.Max);
                }
            }
        }

        private void OnDestroy()
        {
            // 次のアバターの上限と取り違えない
            if (_active == this)
            {
                _active = null;
            }
        }

        /// <summary>
        /// ルートからの相対パス（"Body" や "Armature/Hips/Hair"。ルート自身は空）。
        /// </summary>
        public static string PathOf(Transform target, Transform root)
        {
            // 親をたどって名前を集め、ルート側から並べる
            var names = new List<string>();
            for (Transform node = target; node != null && node != root; node = node.parent)
            {
                names.Add(node.name);
            }

            names.Reverse();
            return string.Join("/", names);
        }
    }
}
