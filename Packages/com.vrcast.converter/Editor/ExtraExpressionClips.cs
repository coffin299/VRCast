using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// FX に無い表情として書き出すクリップの指定（AnimationClip またはフォルダ）。
    /// 指定はアバターごとに EditorPrefs へアセットの GUID で保存する。
    /// </summary>
    public static class ExtraExpressionClips
    {
        // 保存先の EditorPrefs キーの接頭辞（後ろにアバターの GlobalObjectId を付ける）
        private const string KeyPrefix = "VRCast.Converter.ExtraExpressionClips.";

        // GUID の区切り文字（GUID には含まれない）
        private const char Separator = ';';

        // FBX 内にある Unity のプレビュー用クリップの名前の接頭辞
        private const string PreviewClipPrefix = "__preview__";

        /// <summary>
        /// アバターに保存した指定を読み込む（未指定・アセットが消えたものは除く）。
        /// </summary>
        public static List<Object> Load(GameObject avatar)
        {
            var entries = new List<Object>();
            // アバター未指定なら空
            if (avatar == null)
            {
                return entries;
            }

            string saved = EditorPrefs.GetString(KeyOf(avatar), string.Empty);
            foreach (string guid in saved.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries))
            {
                // 削除・移動先の分からないアセットは読み飛ばす
                Object entry = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
                if (IsAccepted(entry))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>
        /// 指定をアバターに保存する（空なら保存値を消す）。
        /// </summary>
        public static void Save(GameObject avatar, IReadOnlyList<Object> entries)
        {
            // アバター未指定なら保存先が無い
            if (avatar == null)
            {
                return;
            }

            var guids = new List<string>();
            foreach (Object entry in entries)
            {
                // 削除済みのものは保存しない
                if (entry == null)
                {
                    continue;
                }

                // アセットとして保存されているものだけ GUID にできる
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry));
                if (!string.IsNullOrEmpty(guid))
                {
                    guids.Add(guid);
                }
            }

            // 空なら EditorPrefs にキーを残さない
            string key = KeyOf(avatar);
            if (guids.Count == 0)
            {
                EditorPrefs.DeleteKey(key);
                return;
            }

            EditorPrefs.SetString(key, string.Join(Separator.ToString(), guids));
        }

        /// <summary>
        /// 指定に使えるもの（AnimationClip のアセットかフォルダ）なら true。
        /// </summary>
        public static bool IsAccepted(Object entry)
        {
            // 未設定・シーン上のオブジェクトは不可
            if (entry == null || !EditorUtility.IsPersistent(entry))
            {
                return false;
            }

            return entry is AnimationClip || AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(entry));
        }

        /// <summary>
        /// 指定を AnimationClip の一覧へ展開する（フォルダはサブフォルダも含めた中のクリップ。重複は除く）。
        /// </summary>
        public static List<AnimationClip> Collect(IEnumerable<Object> entries)
        {
            var clips = new List<AnimationClip>();
            var seen = new HashSet<AnimationClip>();
            foreach (Object entry in entries)
            {
                // クリップはそのまま追加
                if (entry is AnimationClip clip)
                {
                    AddClip(clip, clips, seen);
                    continue;
                }

                // 削除済み・フォルダ以外は無視
                string folder = entry != null ? AssetDatabase.GetAssetPath(entry) : string.Empty;
                if (!AssetDatabase.IsValidFolder(folder))
                {
                    continue;
                }

                // フォルダ内のクリップを持つアセット（.anim・FBX など）からクリップを取り出す
                foreach (string guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
                {
                    foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)))
                    {
                        if (asset is AnimationClip found)
                        {
                            AddClip(found, clips, seen);
                        }
                    }
                }
            }

            return clips;
        }

        private static void AddClip(AnimationClip clip, List<AnimationClip> clips, HashSet<AnimationClip> seen)
        {
            // プレビュー用クリップと重複は入れない
            if (clip.name.StartsWith(PreviewClipPrefix, StringComparison.Ordinal) || !seen.Add(clip))
            {
                return;
            }

            clips.Add(clip);
        }

        private static string KeyOf(GameObject avatar)
        {
            // シーン上のオブジェクトと Prefab の両方を一意に表せる ID をキーにする
            return KeyPrefix + GlobalObjectId.GetGlobalObjectIdSlow(avatar);
        }
    }
}
