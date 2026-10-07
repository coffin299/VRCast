using System;
using System.Collections.Generic;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/perfectsync.json の内容。VRCast で作ったパーフェクトシンク用の形状の目次
    /// （差分を当てるメッシュの照合情報と、形状名の一覧）。形状の中身は形状ごとの別ファイル。
    /// </summary>
    [Serializable]
    public class PerfectSyncSet : IMetadata
    {
        // この節の形式の版（上げると、それより古い Runtime は形状を無視する）
        public const int CurrentVersion = 1;

        // 信頼できない入力に対する上限
        public const int MaxMeshes = 16;
        public const int MaxShapes = 64;
        public const int MaxVertexCount = 1000000;
        public const int MaxNameLength = 64;

        // 頂点位置のハッシュの桁数（FNV-1a 64 bit の 16 進）
        public const int VertexHashLength = 16;

        public int version = CurrentVersion;
        public PerfectSyncMeshInfo[] meshes = Array.Empty<PerfectSyncMeshInfo>();
        public string[] shapes = Array.Empty<string>();

        /// <summary>
        /// 形状が 1 つも無ければ true。
        /// </summary>
        public bool IsEmpty => shapes == null || shapes.Length == 0;

        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        public string Validate()
        {
            // 知らない版は読まない
            if (version != CurrentVersion)
            {
                return $"Unsupported version: {version}.";
            }

            // 配列の欠落と件数の上限
            if (meshes == null || meshes.Length > MaxMeshes)
            {
                return $"meshes must be 0-{MaxMeshes} items.";
            }

            if (shapes == null || shapes.Length > MaxShapes)
            {
                return $"shapes must be 0-{MaxShapes} items.";
            }

            foreach (PerfectSyncMeshInfo mesh in meshes)
            {
                // パス（空 = ルート）、頂点数、ハッシュ
                if (mesh == null || !ExpressionSet.IsValidString(mesh.path, true))
                {
                    return "Mesh path is invalid.";
                }

                if (mesh.vertexCount <= 0 || mesh.vertexCount > MaxVertexCount)
                {
                    return $"Mesh '{mesh.path}' has an invalid vertex count.";
                }

                if (!IsLowerHex(mesh.vertexHash, VertexHashLength))
                {
                    return $"Mesh '{mesh.path}' has an invalid vertex hash.";
                }
            }

            // 形状名は規則どおりで重複なし
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string shape in shapes)
            {
                if (!IsValidShapeName(shape) || !seen.Add(shape))
                {
                    return "Shape name is invalid or duplicated.";
                }
            }

            return null;
        }

        /// <summary>
        /// 形状名として使えるなら true（英字で始まる英数字、MaxNameLength 文字以内。ファイル名にも使うため記号は不可）。
        /// </summary>
        public static bool IsValidShapeName(string name)
        {
            // 空・長すぎる名前は不可
            if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
            {
                return false;
            }

            // 先頭は英字
            if (!IsAsciiLetter(name[0]))
            {
                return false;
            }

            // 残りは英数字のみ
            foreach (char c in name)
            {
                if (!IsAsciiLetter(c) && (c < '0' || c > '9'))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 形状名に対応する形状ファイルのエントリ名。
        /// </summary>
        public static string ShapeEntryName(string name)
        {
            return AvatarPackageLayout.PerfectSyncShapePrefix + name + AvatarPackageLayout.PerfectSyncShapeSuffix;
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IsLowerHex(string value, int length)
        {
            // 桁数が違えば不可
            if (value == null || value.Length != length)
            {
                return false;
            }

            // 0-9 と a-f だけ
            foreach (char c in value)
            {
                if ((c < '0' || c > '9') && (c < 'a' || c > 'f'))
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>
    /// 差分を当てるメッシュ（SkinnedMeshRenderer）の照合情報。
    /// </summary>
    [Serializable]
    public class PerfectSyncMeshInfo
    {
        public string path = string.Empty;
        public int vertexCount;
        public string vertexHash = string.Empty;
    }

    /// <summary>
    /// metadata/perfectsync_&lt;名前&gt;.json の内容。1 つの形状の、メッシュごとの頂点の差分。
    /// </summary>
    [Serializable]
    public class PerfectSyncShape : IMetadata
    {
        public string name = string.Empty;
        public PerfectSyncShapeMesh[] meshes = Array.Empty<PerfectSyncShapeMesh>();

        /// <summary>
        /// 構造だけを検証する（差分の中身は目次の頂点数と合わせて PerfectSyncCodec.TryDecode で確かめる）。
        /// </summary>
        public string Validate()
        {
            // 名前は目次と同じ規則
            if (!PerfectSyncSet.IsValidShapeName(name))
            {
                return "Shape name is invalid.";
            }

            // メッシュの件数の上限
            if (meshes == null || meshes.Length > PerfectSyncSet.MaxMeshes)
            {
                return $"meshes must be 0-{PerfectSyncSet.MaxMeshes} items.";
            }

            // メッシュの位置は範囲内で重複なし、差分の文字列は必須
            var seen = new HashSet<int>();
            foreach (PerfectSyncShapeMesh mesh in meshes)
            {
                if (mesh == null || mesh.mesh < 0 || mesh.mesh >= PerfectSyncSet.MaxMeshes || !seen.Add(mesh.mesh))
                {
                    return $"Shape '{name}' has an invalid mesh index.";
                }

                if (mesh.indices == null || mesh.deltas == null)
                {
                    return $"Shape '{name}' has missing data.";
                }
            }

            return null;
        }
    }

    /// <summary>
    /// 1 つのメッシュに対する差分（頂点番号と位置の差分、どちらも base64）。
    /// </summary>
    [Serializable]
    public class PerfectSyncShapeMesh
    {
        public int mesh;
        public string indices = string.Empty;
        public string deltas = string.Empty;
    }
}
