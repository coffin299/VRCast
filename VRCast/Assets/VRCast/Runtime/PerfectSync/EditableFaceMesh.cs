using UnityEngine;
using UnityEngine.Rendering;
using VRCast.Animations;
using VRCast.AvatarFormat;
using VRCast.Tracking;
using Object = UnityEngine.Object;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// パーフェクトシンクの形状を作れる 1 つのメッシュ（読み取り可能な SkinnedMeshRenderer のメッシュ）。
    /// ARKit 名ごとの頂点の差分（メッシュ空間、頂点数ぶんの配列）を持ち、Rebuild で元のメッシュの複製に BlendShape として加える。
    /// 元の BlendShape の番号は変わらないため、表情・まばたき等の参照（レンダラー + 番号）はそのまま使える。
    /// </summary>
    public sealed class EditableFaceMesh
    {
        // 差分が無いとみなす大きさ（m の 2 乗）
        private const float ZeroDeltaSqr = 1e-14f;

        // 追加する BlendShape の重み（ARKit の 1.0 = 100）
        private const float FrameWeight = 100f;

        private static readonly string[] Names = ArKitFace.BlendShapeNames;

        // ARKit 名ごとの差分（null = 無し）と、元から持っている ARKit 名（上書きできない）
        private readonly Vector3[][] _deltas = new Vector3[Names.Length][];
        private readonly bool[] _native = new bool[Names.Length];

        // 作業用メッシュ（元の複製 + 作った形状。null = 元のメッシュのまま）と、ARKit 名 → その中の BlendShape の番号
        private Mesh _working;
        private readonly int[] _workingIndex = new int[Names.Length];

        // 編集中の形状を頂点で表示しているなら true（終わったら元の位置へ戻す）
        private bool _previewing;

        // 元の頂点・法線・三角形（必要になったときに読む）と、作業用の配列
        private Vector3[] _baseVertices;
        private Vector3[] _baseNormals;
        private int[] _triangles;
        private Vector3[] _scratch;
        private string _vertexHash;

        // 法線の差分を求めるための、元の形の三角形だけのメッシュと、その再計算した法線
        private Mesh _normalMesh;
        private Vector3[] _recalculatedBaseNormals;

        private EditableFaceMesh(SkinnedMeshRenderer renderer, string path, Mesh original)
        {
            Renderer = renderer;
            Path = path;
            Original = original;
            VertexCount = original.vertexCount;
            OriginalShapeCount = original.blendShapeCount;

            // 元から持っている ARKit 名（同じ名前は追加できず、パーフェクトシンクはそちらを使う）
            for (int i = 0; i < OriginalShapeCount; i++)
            {
                int index = PerfectSyncBlendShapes.Match(original.GetBlendShapeName(i));
                if (index >= 0)
                {
                    _native[index] = true;
                }
            }

            // 読み込み時の BlendShape の値（作成モード中はこの値に固定して、まばたき等で形を変えない）
            NeutralWeights = new float[OriginalShapeCount];
            for (int i = 0; i < OriginalShapeCount; i++)
            {
                NeutralWeights[i] = renderer.GetBlendShapeWeight(i);
            }

            for (int i = 0; i < _workingIndex.Length; i++)
            {
                _workingIndex[i] = -1;
            }
        }

        public SkinnedMeshRenderer Renderer { get; }

        /// <summary>
        /// アバタールートからの相対パス。
        /// </summary>
        public string Path { get; }

        /// <summary>
        /// bundle から読んだ元のメッシュ。
        /// </summary>
        public Mesh Original { get; }

        public int VertexCount { get; }

        /// <summary>
        /// 元のメッシュの BlendShape の数（これより後ろが作った形状）。
        /// </summary>
        public int OriginalShapeCount { get; }

        /// <summary>
        /// 読み込み時の元の BlendShape の値。
        /// </summary>
        public float[] NeutralWeights { get; }

        /// <summary>
        /// 元の頂点位置（メッシュ空間）。
        /// </summary>
        public Vector3[] BaseVertices => _baseVertices ?? (_baseVertices = Original.vertices);

        /// <summary>
        /// 元の法線（メッシュ空間。メッシュに無ければ再計算した法線）。
        /// </summary>
        public Vector3[] BaseNormals
        {
            get
            {
                // メッシュの法線を使い、無い・数が合わなければ三角形から求める
                if (_baseNormals == null)
                {
                    Vector3[] normals = Original.normals;
                    _baseNormals = normals.Length == VertexCount ? normals : RecalculatedBaseNormals();
                }

                return _baseNormals;
            }
        }

        /// <summary>
        /// 全サブメッシュの三角形（頂点番号の 3 つ組）。
        /// </summary>
        public int[] Triangles => _triangles ?? (_triangles = Original.triangles);

        /// <summary>
        /// 保存・照合用の頂点ハッシュ。
        /// </summary>
        public string VertexHash => _vertexHash ?? (_vertexHash = PerfectSyncCodec.HashVertices(BaseVertices));

        /// <summary>
        /// 作れるメッシュなら作る（メッシュ無し・読み取り不可・頂点数が範囲外なら null）。
        /// </summary>
        public static EditableFaceMesh TryCreate(SkinnedMeshRenderer renderer, Transform root)
        {
            // Read/Write 無効のメッシュは頂点を読めない（書き出しツールのオプションで有効にする）
            Mesh mesh = renderer.sharedMesh;
            if (mesh == null || !mesh.isReadable || mesh.vertexCount == 0 || mesh.vertexCount > PerfectSyncSet.MaxVertexCount)
            {
                return null;
            }

            return new EditableFaceMesh(renderer, BlendShapeLimiter.PathOf(renderer.transform, root), mesh);
        }

        /// <summary>
        /// 差分が無い（保存しない）とみなす大きさなら true。
        /// </summary>
        public static bool IsZero(Vector3 delta)
        {
            return delta.sqrMagnitude <= ZeroDeltaSqr;
        }

        /// <summary>
        /// 元から持っている ARKit 名なら true（作った形状で上書きできない）。
        /// </summary>
        public bool IsNative(int shape)
        {
            return _native[shape];
        }

        /// <summary>
        /// 形状の差分（無ければ null。配列は直接書き換えてよい）。
        /// </summary>
        public Vector3[] GetDeltas(int shape)
        {
            return _deltas[shape];
        }

        /// <summary>
        /// 形状の差分（無ければ 0 の配列を作る）。
        /// </summary>
        public Vector3[] GetOrCreateDeltas(int shape)
        {
            return _deltas[shape] ?? (_deltas[shape] = new Vector3[VertexCount]);
        }

        /// <summary>
        /// 形状の差分を差し替える（null で消す）。元に戻す操作用。
        /// </summary>
        public void SetDeltas(int shape, Vector3[] deltas)
        {
            _deltas[shape] = deltas;
        }

        /// <summary>
        /// 0 でない差分があれば true。
        /// </summary>
        public bool HasDeltas(int shape)
        {
            Vector3[] deltas = _deltas[shape];
            if (deltas == null || _native[shape])
            {
                return false;
            }

            foreach (Vector3 delta in deltas)
            {
                if (!IsZero(delta))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 作った形状が 1 つでもあれば true。
        /// </summary>
        public bool HasAnyDeltas()
        {
            for (int i = 0; i < Names.Length; i++)
            {
                if (HasDeltas(i))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 作業用メッシュの中の形状の BlendShape の番号（追加していなければ -1）。
        /// </summary>
        public int WorkingShapeIndex(int shape)
        {
            return _workingIndex[shape];
        }

        /// <summary>
        /// 差分のある形状を BlendShape として加えたメッシュに作り直し、レンダラーへ当てる。
        /// 作った形状が無く keepWorking でなければ元のメッシュに戻す。元の BlendShape の値は引き継ぐ。
        /// </summary>
        public void Rebuild(bool keepWorking = false)
        {
            // 頂点で表示していた形状は元の位置へ戻してから作り直す
            EndPreview();

            // 元の BlendShape の今の値を控える（メッシュを差し替えると消えるため）
            var weights = new float[OriginalShapeCount];
            for (int i = 0; i < weights.Length; i++)
            {
                weights[i] = Renderer.GetBlendShapeWeight(i);
            }

            for (int i = 0; i < _workingIndex.Length; i++)
            {
                _workingIndex[i] = -1;
            }

            // 作った形状が無ければ元のメッシュへ戻す
            Mesh next = null;
            if (HasAnyDeltas() || keepWorking)
            {
                next = Object.Instantiate(Original);
                next.name = Original.name;
                for (int shape = 0; shape < Names.Length; shape++)
                {
                    // 元から持っている名前・差分の無い形状は加えない（同じ名前の BlendShape は追加できない）
                    if (!HasDeltas(shape) || Original.GetBlendShapeIndex(Names[shape]) >= 0)
                    {
                        continue;
                    }

                    next.AddBlendShapeFrame(Names[shape], FrameWeight, _deltas[shape], NormalDeltas(_deltas[shape]), null);
                    _workingIndex[shape] = next.blendShapeCount - 1;
                }
            }

            // 差し替えて元の BlendShape の値を戻し、前の作業用メッシュを破棄する
            Renderer.sharedMesh = next != null ? next : Original;
            for (int i = 0; i < weights.Length; i++)
            {
                Renderer.SetBlendShapeWeight(i, weights[i]);
            }

            if (_working != null)
            {
                Object.Destroy(_working);
            }

            _working = next;
        }

        /// <summary>
        /// 形状を BlendShape ではなく頂点（元の位置 + 差分 × weight）で表示する（編集中に毎回作り直さないため）。
        /// </summary>
        public void ShowPreview(int shape, float weight)
        {
            // 頂点を書き換えるため作業用メッシュを用意する
            if (_working == null)
            {
                Rebuild(true);
            }

            // 元の位置に差分を足した頂点を当てる（差分が無ければ元の位置）
            Vector3[] baseVertices = BaseVertices;
            Vector3[] deltas = _deltas[shape];
            Vector3[] vertices = Scratch();
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] = deltas != null ? baseVertices[i] + deltas[i] * weight : baseVertices[i];
            }

            _working.SetVertices(vertices);
            _previewing = true;
        }

        /// <summary>
        /// 頂点での表示をやめて元の位置へ戻す。
        /// </summary>
        public void EndPreview()
        {
            // 表示していなければ何もしない
            if (!_previewing || _working == null)
            {
                _previewing = false;
                return;
            }

            _working.SetVertices(BaseVertices);
            _previewing = false;
        }

        /// <summary>
        /// 作業用メッシュを破棄する。restore なら元のメッシュをレンダラーへ戻す（アバターの破棄中は戻さない）。
        /// </summary>
        public void Dispose(bool restore)
        {
            // 元のメッシュへ戻す（値も引き継ぐ）
            if (restore && _working != null && Renderer != null)
            {
                var weights = new float[OriginalShapeCount];
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] = Renderer.GetBlendShapeWeight(i);
                }

                Renderer.sharedMesh = Original;
                for (int i = 0; i < weights.Length; i++)
                {
                    Renderer.SetBlendShapeWeight(i, weights[i]);
                }
            }

            // 自分で作ったメッシュを破棄
            if (_working != null)
            {
                Object.Destroy(_working);
                _working = null;
            }

            if (_normalMesh != null)
            {
                Object.Destroy(_normalMesh);
                _normalMesh = null;
            }

            _previewing = false;
        }

        private Vector3[] NormalDeltas(Vector3[] deltas)
        {
            // 変形後の形の法線を再計算し、元の形の再計算した法線との差を法線の差分にする
            // （同じ方法で求めた法線どうしの差なので、UV の継ぎ目の段差は打ち消し合う）
            Vector3[] baseNormals = RecalculatedBaseNormals();
            Vector3[] baseVertices = BaseVertices;
            Vector3[] deformed = Scratch();
            for (int i = 0; i < deformed.Length; i++)
            {
                deformed[i] = baseVertices[i] + deltas[i];
            }

            _normalMesh.SetVertices(deformed);
            _normalMesh.RecalculateNormals();
            Vector3[] normals = _normalMesh.normals;
            for (int i = 0; i < normals.Length; i++)
            {
                normals[i] -= baseNormals[i];
            }

            // 次の計算のため元の形へ戻しておく
            _normalMesh.SetVertices(baseVertices);
            return normals;
        }

        private Vector3[] RecalculatedBaseNormals()
        {
            // 初回だけ元の形の三角形メッシュを作り、法線を再計算して控える
            if (_normalMesh == null)
            {
                _normalMesh = new Mesh { indexFormat = IndexFormat.UInt32 };
                _normalMesh.SetVertices(BaseVertices);
                _normalMesh.SetTriangles(Triangles, 0, false);
                _normalMesh.RecalculateNormals();
                _recalculatedBaseNormals = _normalMesh.normals;
            }

            return _recalculatedBaseNormals;
        }

        private Vector3[] Scratch()
        {
            // 頂点数ぶんの作業用配列を使い回す
            return _scratch ?? (_scratch = new Vector3[VertexCount]);
        }
    }
}
