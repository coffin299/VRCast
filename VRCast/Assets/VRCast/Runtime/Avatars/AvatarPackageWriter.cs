using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEngine;
using VRCast.AvatarFormat;
using CompressionLevel = System.IO.Compression.CompressionLevel;

namespace VRCast.Avatars
{
    /// <summary>
    /// 既存の .vrcaster の、パーフェクトシンクの形状のエントリだけを差し替える（manifest・bundle・他の metadata はそのまま写す）。
    /// 一時ファイルに書いてから置き換え、初回だけ元のファイルを .bak として残す。
    /// </summary>
    public static class AvatarPackageWriter
    {
        // 元のファイルの控えと、書き込み途中の一時ファイルの接尾辞
        public const string BackupSuffix = ".bak";
        private const string TempSuffix = ".tmp";

        /// <summary>
        /// 形状を書き込む（set が空なら形状のエントリを消す）。shapes は set.shapes と同じ並び。
        /// 控えを今回作ったならそのパス、作らなかった（既にある）なら null を返す。失敗時は AvatarPackageException 等。
        /// </summary>
        public static string ReplacePerfectSync(string packagePath, PerfectSyncSet set, IReadOnlyList<PerfectSyncShape> shapes)
        {
            // 書き込む内容を先に JSON にして検証する（壊れた形状を書かない）
            List<KeyValuePair<string, string>> entries = BuildEntries(set, shapes);

            // 書き込み先が正しいパッケージであることを確かめる（壊れたファイルを上書きしない）
            AvatarPackageReader.ReadPerfectSyncOnly(packagePath);

            // 一時ファイルへ書く（失敗したら消す）
            string tempPath = packagePath + TempSuffix;
            try
            {
                WritePackage(packagePath, tempPath, entries);
            }
            catch
            {
                File.Delete(tempPath);
                throw;
            }

            // 初回だけ元のファイルを控えとして残す
            string backupPath = packagePath + BackupSuffix;
            bool backedUp = false;
            if (!File.Exists(backupPath))
            {
                File.Copy(packagePath, backupPath);
                backedUp = true;
            }

            // 一時ファイルで置き換える
            File.Delete(packagePath);
            File.Move(tempPath, packagePath);
            return backedUp ? backupPath : null;
        }

        /// <summary>
        /// パーフェクトシンクの形状のエントリ（目次・形状ファイル）なら true。
        /// </summary>
        public static bool IsPerfectSyncEntry(string name)
        {
            return name == AvatarPackageLayout.PerfectSyncEntry
                || (name.StartsWith(AvatarPackageLayout.PerfectSyncShapePrefix, StringComparison.Ordinal)
                    && name.EndsWith(AvatarPackageLayout.PerfectSyncShapeSuffix, StringComparison.Ordinal));
        }

        private static List<KeyValuePair<string, string>> BuildEntries(PerfectSyncSet set, IReadOnlyList<PerfectSyncShape> shapes)
        {
            var entries = new List<KeyValuePair<string, string>>();

            // 目次の検証（空なら形状のエントリを書かない = 消す）
            string error = set.Validate();
            if (error != null)
            {
                throw new AvatarPackageException("Invalid perfect sync data: " + error);
            }

            if (set.IsEmpty)
            {
                return entries;
            }

            // 目次と形状の並び・数が一致すること
            if (shapes == null || shapes.Count != set.shapes.Length)
            {
                throw new AvatarPackageException("Perfect sync shapes do not match the index.");
            }

            entries.Add(ToEntry(AvatarPackageLayout.PerfectSyncEntry, JsonUtility.ToJson(set, true)));
            for (int i = 0; i < shapes.Count; i++)
            {
                // 名前が目次と同じで、構造が正しいこと
                PerfectSyncShape shape = shapes[i];
                string shapeError = shape == null ? "missing" : shape.Validate();
                if (shapeError != null || shape.name != set.shapes[i])
                {
                    throw new AvatarPackageException($"Invalid perfect sync shape '{set.shapes[i]}': {shapeError ?? "name"}");
                }

                // 形状ファイルは容量を抑えるため整形しない
                entries.Add(ToEntry(PerfectSyncSet.ShapeEntryName(shape.name), JsonUtility.ToJson(shape)));
            }

            return entries;
        }

        private static KeyValuePair<string, string> ToEntry(string name, string json)
        {
            // Runtime が読める大きさ（metadata 1 ファイルの上限）に収まること
            if (Encoding.UTF8.GetByteCount(json) > AvatarPackageLayout.MaxMetadataBytes)
            {
                throw new AvatarPackageException($"{name} is too large. Reduce the edited area.");
            }

            return new KeyValuePair<string, string>(name, json);
        }

        private static void WritePackage(string sourcePath, string tempPath, List<KeyValuePair<string, string>> entries)
        {
            // 前回の失敗で残った一時ファイルは消す
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            int count = 0;
            using (FileStream sourceStream = File.OpenRead(sourcePath))
            using (var source = new ZipArchive(sourceStream, ZipArchiveMode.Read))
            using (FileStream targetStream = File.Create(tempPath))
            using (var target = new ZipArchive(targetStream, ZipArchiveMode.Create))
            {
                // 形状以外のエントリをそのまま写す（Exporter と同じく無圧縮）
                foreach (ZipArchiveEntry entry in source.Entries)
                {
                    if (IsPerfectSyncEntry(entry.FullName))
                    {
                        continue;
                    }

                    ZipArchiveEntry copy = target.CreateEntry(entry.FullName, CompressionLevel.NoCompression);
                    copy.LastWriteTime = entry.LastWriteTime;
                    using (Stream input = entry.Open())
                    using (Stream output = copy.Open())
                    {
                        input.CopyTo(output);
                    }

                    count++;
                }

                // 新しい形状のエントリを書く
                foreach (KeyValuePair<string, string> entry in entries)
                {
                    using (var writer = new StreamWriter(target.CreateEntry(entry.Key, CompressionLevel.NoCompression).Open()))
                    {
                        writer.Write(entry.Value);
                    }

                    count++;
                }
            }

            // Runtime が読めるエントリ数に収まること（旧 Runtime も同じ上限）
            if (count > AvatarPackageReader.MaxEntries)
            {
                throw new AvatarPackageException($"Too many entries: {count}.");
            }
        }
    }
}
