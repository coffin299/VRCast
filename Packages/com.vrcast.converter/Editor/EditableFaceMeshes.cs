using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using VRCast.AvatarFormat;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// VRCast の「パーフェクトシンク作成モード」で頂点を読めるよう、書き出し用の複製の顔まわりのメッシュを
    /// 読み取り可能（Read/Write 有効）な複製へ差し替える。元のメッシュ・インポート設定は変更しない。
    /// 対象: リップシンク・まぶた用のメッシュと、Head 以下のボーンを使い BlendShape を持つか歯・舌・口らしい名前のメッシュ。
    /// </summary>
    public static class EditableFaceMeshes
    {
        // 歯・舌・口のメッシュとみなす名前の一部（大文字小文字は無視）
        private static readonly string[] MouthKeywords = { "teeth", "tooth", "tongue", "mouth", "歯", "舌", "口" };

        /// <summary>
        /// clone 内の対象メッシュを、folder に保存した読み取り可能な複製へ差し替え、差し替えたレンダラーの数を返す。
        /// </summary>
        public static int Apply(GameObject clone, AvatarDescriptorData descriptor, string folder)
        {
            // Head ボーン（Humanoid でなければ Descriptor のメッシュだけを対象にする）
            var animator = clone.GetComponent<Animator>();
            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

            // リップシンク・まぶた用のメッシュのパス（空は除く）
            var facePaths = new HashSet<string>(StringComparer.Ordinal);
            AddPath(facePaths, descriptor.lipSync.meshPath);
            AddPath(facePaths, descriptor.eyelids.meshPath);

            // 同じメッシュを複数のレンダラーが使う場合は 1 つの複製を共有する
            var copies = new Dictionary<Mesh, Mesh>();
            int replaced = 0;
            foreach (SkinnedMeshRenderer renderer in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // メッシュ無し・元から読み取り可能・顔まわりでないものは対象外
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null || mesh.isReadable || !IsFaceMesh(renderer, clone.transform, head, facePaths))
                {
                    continue;
                }

                // 初めてのメッシュなら読み取り可能な複製を一時フォルダに作る
                if (!copies.TryGetValue(mesh, out Mesh copy))
                {
                    copy = CreateReadableCopy(mesh, $"{folder}/EditableMesh{copies.Count}.asset");
                    copies[mesh] = copy;
                }

                // 差し替えで BlendShape の値が消えないよう控えてから戻す
                var weights = new float[mesh.blendShapeCount];
                for (int i = 0; i < weights.Length; i++)
                {
                    weights[i] = renderer.GetBlendShapeWeight(i);
                }

                renderer.sharedMesh = copy;
                for (int i = 0; i < weights.Length; i++)
                {
                    renderer.SetBlendShapeWeight(i, weights[i]);
                }

                replaced++;
            }

            return replaced;
        }

        private static void AddPath(HashSet<string> paths, string path)
        {
            // 未設定（空）は対象にしない
            if (!string.IsNullOrEmpty(path))
            {
                paths.Add(path);
            }
        }

        private static bool IsFaceMesh(SkinnedMeshRenderer renderer, Transform root, Transform head, HashSet<string> facePaths)
        {
            // リップシンク・まぶた用のメッシュは必ず対象
            if (facePaths.Contains(ReflectionUtility.PathOf(renderer.transform, root)))
            {
                return true;
            }

            // Head 以下のボーンを使わないメッシュ（体・服など）は対象外
            if (head == null || !UsesHeadBones(renderer, head))
            {
                return false;
            }

            // BlendShape を持つか、歯・舌・口らしい名前なら対象（髪など形の変わらないものは除く）
            return renderer.sharedMesh.blendShapeCount > 0 || LooksLikeMouth(renderer.name) || LooksLikeMouth(renderer.sharedMesh.name);
        }

        private static bool UsesHeadBones(SkinnedMeshRenderer renderer, Transform head)
        {
            // ボーンの中に Head 自身か、その子孫があれば true
            foreach (Transform bone in renderer.bones)
            {
                if (bone != null && (bone == head || bone.IsChildOf(head)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool LooksLikeMouth(string name)
        {
            // 名前の一部に歯・舌・口の語があれば true
            foreach (string keyword in MouthKeywords)
            {
                if (name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static Mesh CreateReadableCopy(Mesh mesh, string assetPath)
        {
            // エディタ上では Read/Write 無効のメッシュも複製できる。名前は元のまま
            Mesh copy = Object.Instantiate(mesh);
            copy.name = mesh.name;
            AssetDatabase.CreateAsset(copy, assetPath);

            // 複製は元の「読み取り不可」を引き継ぐため、シリアライズされた値を直接書き換える
            var serialized = new SerializedObject(copy);
            SerializedProperty readable = serialized.FindProperty("m_IsReadable");
            if (readable != null)
            {
                readable.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            return copy;
        }
    }
}
