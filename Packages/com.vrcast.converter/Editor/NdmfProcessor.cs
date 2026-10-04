using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// NDMF（Modular Avatar 等の非破壊改変ツールの基盤）のビルド処理を、アセンブリ参照なしで複製アバターへ適用する。
    /// NDMF が無いプロジェクトでは何もしない。
    /// </summary>
    public static class NdmfProcessor
    {
        // NDMF の手動ベイクと同じ処理を行う公開 API
        private const string ProcessorTypeName = "nadena.dev.ndmf.AvatarProcessor";
        private const string ProcessMethodName = "ProcessAvatar";

        // NDMF が生成アセット（マージ後の Animator Controller・メッシュ等）を保存するフォルダ
        private const string GeneratedAssetsFolder = "Assets/ZZZ_GeneratedAssets";

        /// <summary>
        /// NDMF の処理を root に適用する。NDMF が無ければ false を返し、root は変更しない。
        /// </summary>
        public static bool Process(GameObject root)
        {
            // API が見つからなければ未導入として扱う
            MethodInfo method = FindProcessMethod();
            if (method == null)
            {
                return false;
            }

            try
            {
                method.Invoke(null, new object[] { root });
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                // リフレクションの包み例外ではなく元の例外のメッセージを見せる
                throw new InvalidOperationException("NDMF (Modular Avatar) processing failed: " + e.InnerException.Message, e.InnerException);
            }

            return true;
        }

        /// <summary>
        /// 生成アセットフォルダ直下の既存エントリを返す。後始末で書き出し中に増えた分だけ消すために使う。
        /// </summary>
        public static HashSet<string> ListGeneratedAssets()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // フォルダが無ければ空
            if (!AssetDatabase.IsValidFolder(GeneratedAssetsFolder))
            {
                return result;
            }

            foreach (string entry in Directory.GetFileSystemEntries(GeneratedAssetsFolder))
            {
                // .meta はアセット本体と一緒に消えるので対象外
                if (!entry.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(ToAssetPath(entry));
                }
            }

            return result;
        }

        /// <summary>
        /// existingBefore に無い生成アセットを削除する。フォルダごと新規なら空になったフォルダも消す。
        /// </summary>
        public static void DeleteGeneratedAssets(HashSet<string> existingBefore)
        {
            // 書き出し中に増えたものだけを消し、利用者の手動ベイク結果は残す
            foreach (string entry in ListGeneratedAssets())
            {
                if (!existingBefore.Contains(entry))
                {
                    AssetDatabase.DeleteAsset(entry);
                }
            }

            // 書き出し前に中身が無く、今回の分を消して空になったフォルダは片付ける
            if (existingBefore.Count == 0 && AssetDatabase.IsValidFolder(GeneratedAssetsFolder)
                && Directory.GetFileSystemEntries(GeneratedAssetsFolder).Length == 0)
            {
                AssetDatabase.DeleteAsset(GeneratedAssetsFolder);
            }
        }

        private static MethodInfo FindProcessMethod()
        {
            // 読み込み済みアセンブリから型を探す（NDMF のアセンブリ名はバージョンで変わり得るため型名で探す）
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(ProcessorTypeName, false);
                if (type == null)
                {
                    continue;
                }

                // プラットフォーム指定付き等の別オーバーロードと区別するため引数型を指定
                return type.GetMethod(
                    ProcessMethodName, BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
            }

            return null;
        }

        private static string ToAssetPath(string path)
        {
            // Directory API は OS の区切り文字を返すので AssetDatabase 形式にそろえる
            return path.Replace('\\', '/');
        }
    }
}
