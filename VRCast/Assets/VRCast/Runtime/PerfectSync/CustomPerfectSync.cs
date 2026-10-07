using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// アバターの「作ったパーフェクトシンクの形状」をまとめて扱う。読み込み時に .vrcaster の形状を照合して加え、
    /// 作成モードでの変更の再構築・保存用の書き出しを行う。表情・まばたき等より先に初期化する（BlendShape の一覧に含めるため）。
    /// </summary>
    public sealed class CustomPerfectSync : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "PerfectSync";

        private static readonly string[] Names = ArKitFace.BlendShapeNames;

        private readonly List<EditableFaceMesh> _meshes = new List<EditableFaceMesh>();

        // アバター内のどこかのメッシュが元から持っている ARKit 名
        private readonly bool[] _nativeAnywhere = new bool[Names.Length];

        /// <summary>
        /// 形状を作れるメッシュ（読み取り可能な SkinnedMeshRenderer）。空なら作成モードは使えない。
        /// </summary>
        public IReadOnlyList<EditableFaceMesh> Meshes => _meshes;

        /// <summary>
        /// 読み込んだ .vrcaster のパス（保存先）。
        /// </summary>
        public string SourcePath { get; private set; }

        /// <summary>
        /// 読み込み時に .vrcaster から加えた形状の数。
        /// </summary>
        public int LoadedShapeCount { get; private set; }

        /// <summary>
        /// メッシュを作り直したときに通知する（BlendShape の上限の一覧・パーフェクトシンクの対象を作り直すため）。
        /// </summary>
        public event Action Rebuilt;

        public void Initialize(Transform root, string sourcePath, PerfectSyncData data)
        {
            SourcePath = sourcePath;

            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // 元から持っている ARKit 名を控える（読み取り不可のメッシュも含む）
                Mesh mesh = renderer.sharedMesh;
                if (mesh != null)
                {
                    for (int i = 0; i < mesh.blendShapeCount; i++)
                    {
                        int index = PerfectSyncBlendShapes.Match(mesh.GetBlendShapeName(i));
                        if (index >= 0)
                        {
                            _nativeAnywhere[index] = true;
                        }
                    }
                }

                // 頂点を読めるメッシュだけを形状の対象にする
                EditableFaceMesh editable = EditableFaceMesh.TryCreate(renderer, root);
                if (editable != null)
                {
                    _meshes.Add(editable);
                }
            }

            // 保存済みの形状を加える（照合できたものだけ）
            if (data != null && !data.IsEmpty)
            {
                LoadedShapeCount = Apply(data);
                RebuildAll();
                VRCastLog.Info(LogCategory, $"Applied {LoadedShapeCount}/{data.Shapes.Count} custom perfect sync shapes.");
            }
        }

        /// <summary>
        /// 元から持っている ARKit 名なら true（作った形状は加えない）。
        /// </summary>
        public bool IsNative(int shape)
        {
            return _nativeAnywhere[shape];
        }

        /// <summary>
        /// どれかのメッシュにこの形状の差分があれば true。
        /// </summary>
        public bool HasShape(int shape)
        {
            foreach (EditableFaceMesh mesh in _meshes)
            {
                if (mesh.HasDeltas(shape))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 直近の RebuildAll 時点の、作った形状の数（ARKit 名の種類数。UI で毎回数えないよう控える）。
        /// </summary>
        public int ShapeCount { get; private set; }

        /// <summary>
        /// 形状の差分を当てる（data に含まれる形状だけを置き換え、他は残す）。照合できたメッシュに 1 つでも当てた形状の数を返す。
        /// 作り直しは呼び出し側が RebuildAll で行う。
        /// </summary>
        public int Apply(PerfectSyncData data)
        {
            // 保存時のメッシュと、パス・頂点数・頂点ハッシュが一致するものだけを対象にする
            var targets = new EditableFaceMesh[data.Meshes.Count];
            for (int i = 0; i < targets.Length; i++)
            {
                PerfectSyncMeshInfo info = data.Meshes[i];
                EditableFaceMesh mesh = _meshes.Find(m => m.Path == info.path);
                if (mesh == null)
                {
                    VRCastLog.Warning(LogCategory, $"Mesh '{info.path}' is missing or not editable. Re-export the avatar.");
                }
                else if (mesh.VertexCount != info.vertexCount || mesh.VertexHash != info.vertexHash)
                {
                    VRCastLog.Warning(LogCategory, $"Mesh '{info.path}' has changed since the shapes were made. Skipped.");
                }
                else
                {
                    targets[i] = mesh;
                }
            }

            int applied = 0;
            foreach (PerfectSyncShapeData shape in data.Shapes)
            {
                // ARKit 名でない形状は使わない
                int index = Array.IndexOf(Names, shape.Name);
                if (index < 0)
                {
                    VRCastLog.Warning(LogCategory, $"Unknown shape '{shape.Name}'. Skipped.");
                    continue;
                }

                bool any = false;
                foreach (PerfectSyncMeshDelta delta in shape.Meshes)
                {
                    // 照合できなかったメッシュ・元から持っている名前には当てない
                    EditableFaceMesh mesh = targets[delta.Mesh];
                    if (mesh == null || mesh.IsNative(index))
                    {
                        continue;
                    }

                    // 疎な差分を頂点数ぶんの配列へ広げる
                    var dense = new Vector3[mesh.VertexCount];
                    for (int k = 0; k < delta.Indices.Length; k++)
                    {
                        dense[delta.Indices[k]] = delta.Deltas[k];
                    }

                    mesh.SetDeltas(index, dense);
                    any = true;
                }

                applied += any ? 1 : 0;
            }

            return applied;
        }

        /// <summary>
        /// 全メッシュを作り直して通知する。
        /// </summary>
        public void RebuildAll()
        {
            foreach (EditableFaceMesh mesh in _meshes)
            {
                mesh.Rebuild();
            }

            // 作った形状の数を数え直す
            int count = 0;
            for (int i = 0; i < Names.Length; i++)
            {
                count += HasShape(i) ? 1 : 0;
            }

            ShapeCount = count;
            Rebuilt?.Invoke();
        }

        /// <summary>
        /// 保存用に、作った形状を目次と形状ごとのデータにする（差分の無い頂点・メッシュ・形状は含めない）。
        /// </summary>
        public PerfectSyncSet Export(List<PerfectSyncShape> shapes)
        {
            shapes.Clear();

            // 差分を持つメッシュだけを目次に載せる
            var meshes = new List<PerfectSyncMeshInfo>();
            var meshIndex = new Dictionary<EditableFaceMesh, int>();
            foreach (EditableFaceMesh mesh in _meshes)
            {
                if (!mesh.HasAnyDeltas())
                {
                    continue;
                }

                meshIndex[mesh] = meshes.Count;
                meshes.Add(new PerfectSyncMeshInfo
                {
                    path = mesh.Path, vertexCount = mesh.VertexCount, vertexHash = mesh.VertexHash,
                });
            }

            var names = new List<string>();
            var indices = new List<int>();
            var deltas = new List<Vector3>();
            for (int shape = 0; shape < Names.Length; shape++)
            {
                var entries = new List<PerfectSyncShapeMesh>();
                foreach (KeyValuePair<EditableFaceMesh, int> pair in meshIndex)
                {
                    // この形状の差分が無いメッシュは含めない
                    EditableFaceMesh mesh = pair.Key;
                    if (!mesh.HasDeltas(shape))
                    {
                        continue;
                    }

                    // 0 でない頂点だけを番号順に詰める
                    indices.Clear();
                    deltas.Clear();
                    Vector3[] dense = mesh.GetDeltas(shape);
                    for (int i = 0; i < dense.Length; i++)
                    {
                        if (!EditableFaceMesh.IsZero(dense[i]))
                        {
                            indices.Add(i);
                            deltas.Add(dense[i]);
                        }
                    }

                    PerfectSyncCodec.Encode(indices, deltas, out string indicesText, out string deltasText);
                    entries.Add(new PerfectSyncShapeMesh { mesh = pair.Value, indices = indicesText, deltas = deltasText });
                }

                // どのメッシュにも差分が無い形状は含めない
                if (entries.Count == 0)
                {
                    continue;
                }

                names.Add(Names[shape]);
                shapes.Add(new PerfectSyncShape { name = Names[shape], meshes = entries.ToArray() });
            }

            return new PerfectSyncSet { meshes = meshes.ToArray(), shapes = names.ToArray() };
        }

        private void OnDestroy()
        {
            // アバターの破棄中はレンダラーへ戻さず、作ったメッシュだけを破棄する
            foreach (EditableFaceMesh mesh in _meshes)
            {
                mesh.Dispose(false);
            }
        }
    }
}
