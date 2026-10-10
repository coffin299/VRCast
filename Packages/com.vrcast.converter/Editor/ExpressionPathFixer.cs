using System.Collections.Generic;
using System.Text;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 表情の BlendShape のパスを、改変（NDMF・MA・メッシュ統合等）を適用した後のアバターに合わせて直す。
    /// FaceEmo 等は改変前のアバターから読むため、メッシュの場所が変わるとアプリで見つからず表情一覧に出ない。
    /// </summary>
    public static class ExpressionPathFixer
    {
        // 見つからなかった値をログに出す数（表情ごと）
        private const int MaxLoggedValues = 3;

        // Avatar Descriptor に顔のメッシュが無いときに顔とみなすオブジェクト名（小文字で比較）
        private static readonly string[] FaceMeshNames = { "body", "face", "head" };

        /// <summary>
        /// 直した値の数と、アプリで 1 つも見つからず表情一覧に出ない表情（名前と見つからなかった値）。
        /// </summary>
        public struct Result
        {
            public int FixedValues;
            public List<string> Unresolved;
        }

        /// <param name="facePaths">顔のメッシュのパス（リップシンク・まぶたのメッシュ。空は無視）</param>
        public static Result Fix(ExpressionSet expressions, Transform root, params string[] facePaths)
        {
            var result = new Result { Unresolved = new List<string>() };
            // 非アクティブを含む全メッシュ（BlendShape 名から探し直す候補）
            SkinnedMeshRenderer[] renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            List<SkinnedMeshRenderer> faces = FindFaces(root, renderers, facePaths);

            foreach (ExpressionPreset preset in expressions.presets)
            {
                bool resolved = false;
                var missing = new List<string>();
                foreach (BlendShapeValue value in preset.values)
                {
                    // そのままのパスで見つかれば直さない
                    Transform node = string.IsNullOrEmpty(value.path) ? root : root.Find(value.path);
                    if (Has(node, value.blendShape))
                    {
                        resolved = true;
                        continue;
                    }

                    // 見つからなければ別のメッシュ、それも無ければ顔のメッシュへ付け替える（決められなければそのまま）
                    if (Relocate(root, renderers, faces, value))
                    {
                        result.FixedValues++;
                        resolved = true;
                    }
                    else
                    {
                        missing.Add($"{value.path} / {value.blendShape}");
                    }
                }

                // 1 つも見つからない表情はアプリの一覧に出ないため、見つからなかった値の例と一緒に知らせる
                if (!resolved)
                {
                    result.Unresolved.Add(Describe(preset.name, missing));
                }
            }

            return result;
        }

        private static bool Relocate(Transform root, SkinnedMeshRenderer[] renderers, List<SkinnedMeshRenderer> faces,
            BlendShapeValue value)
        {
            // 同じ BlendShape を持つメッシュ（同名を優先・顔を優先・唯一のもの）
            SkinnedMeshRenderer target = FindMoved(renderers, faces, value);
            if (target != null)
            {
                value.path = ReflectionUtility.PathOf(target.transform, root);
                return true;
            }

            // どのメッシュにも同じ名前が無ければ、顔のメッシュで大文字・小文字や区切り文字の違いだけの BlendShape を探す
            foreach (SkinnedMeshRenderer face in faces)
            {
                string actual = FindSimilarBlendShape(face.sharedMesh, value.blendShape);
                if (actual != null)
                {
                    value.path = ReflectionUtility.PathOf(face.transform, root);
                    value.blendShape = actual;
                    return true;
                }
            }

            return false;
        }

        private static SkinnedMeshRenderer FindMoved(SkinnedMeshRenderer[] renderers, List<SkinnedMeshRenderer> faces,
            BlendShapeValue value)
        {
            // 元のパスの末尾（オブジェクト名）が同じものを優先する
            string name = value.path.Substring(value.path.LastIndexOf('/') + 1);
            SkinnedMeshRenderer sameName = null;
            SkinnedMeshRenderer only = null;
            int sameNameCount = 0;
            int count = 0;
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                if (!Has(renderer.transform, value.blendShape))
                {
                    continue;
                }

                count++;
                only = renderer;
                if (renderer.name == name)
                {
                    sameNameCount++;
                    sameName = renderer;
                }
            }

            if (sameNameCount == 1)
            {
                return sameName;
            }

            // 同名で決められなければ、その BlendShape を持つ顔のメッシュ（表情は顔を動かすものとみなす）
            foreach (SkinnedMeshRenderer face in faces)
            {
                if (Has(face.transform, value.blendShape))
                {
                    return face;
                }
            }

            // 顔にも無ければ、BlendShape を持つものが 1 つだけのときに限る（複数なら取り違えを避けて直さない）
            return count == 1 ? only : null;
        }

        private static List<SkinnedMeshRenderer> FindFaces(Transform root, SkinnedMeshRenderer[] renderers,
            string[] facePaths)
        {
            // Avatar Descriptor の顔のメッシュ（リップシンク → まぶたの順。重複は除く）
            var faces = new List<SkinnedMeshRenderer>();
            foreach (string path in facePaths)
            {
                Transform node = string.IsNullOrEmpty(path) ? null : root.Find(path);
                var renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer != null && renderer.sharedMesh != null && !faces.Contains(renderer))
                {
                    faces.Add(renderer);
                }
            }

            // 設定が無ければ、よくある顔のメッシュの名前で探す
            if (faces.Count == 0)
            {
                foreach (string faceName in FaceMeshNames)
                {
                    foreach (SkinnedMeshRenderer renderer in renderers)
                    {
                        if (renderer.sharedMesh != null && renderer.name.ToLowerInvariant() == faceName
                            && !faces.Contains(renderer))
                        {
                            faces.Add(renderer);
                        }
                    }
                }
            }

            return faces;
        }

        private static string FindSimilarBlendShape(Mesh mesh, string blendShape)
        {
            // 区切り文字・空白・大文字小文字を無視して 1 つだけ一致するもの（複数なら取り違えを避けて null）
            string key = Normalize(blendShape);
            if (key.Length == 0)
            {
                return null;
            }

            string found = null;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string candidate = mesh.GetBlendShapeName(i);
                if (Normalize(candidate) != key)
                {
                    continue;
                }

                if (found != null)
                {
                    return null;
                }

                found = candidate;
            }

            return found;
        }

        private static string Normalize(string name)
        {
            // 比較用に、英数字・かな等以外（空白・_・-・. 等）を除いて小文字にする
            var builder = new StringBuilder();
            foreach (char c in name ?? string.Empty)
            {
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.ToString();
        }

        private static string Describe(string presetName, List<string> missing)
        {
            // 「表情名 (パス / BlendShape, ...)」の形で、多すぎる分は件数だけにする
            int shown = Mathf.Min(missing.Count, MaxLoggedValues);
            string values = string.Join(", ", missing.GetRange(0, shown));
            if (missing.Count > shown)
            {
                values += $", +{missing.Count - shown}";
            }

            return $"{presetName} ({values})";
        }

        private static bool Has(Transform node, string blendShape)
        {
            // メッシュがあり、その BlendShape を持つか
            var renderer = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
            return renderer != null && renderer.sharedMesh != null
                && !string.IsNullOrEmpty(blendShape)
                && renderer.sharedMesh.GetBlendShapeIndex(blendShape) >= 0;
        }
    }
}
