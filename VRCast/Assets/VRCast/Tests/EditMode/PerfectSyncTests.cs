using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using VRCast.PerfectSync;

namespace VRCast.Tests
{
    /// <summary>
    /// VRCast で作るパーフェクトシンクの形状の保存形式（コーデック・検証・読み込み・書き込み）と、頂点の照合を確認する。
    /// </summary>
    public class PerfectSyncTests
    {
        // 照合用の頂点ハッシュ（形式だけ正しい値）とメッシュの頂点数
        private const string Hash = "0123456789abcdef";
        private const int VertexCount = 10;

        private string _directory;

        [SetUp]
        public void SetUp()
        {
            // テストごとに独立した一時ディレクトリを使う
            _directory = Path.Combine(Path.GetTempPath(), "VRCastPerfectSyncTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            // 一時ディレクトリを後始末
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void Codec_RoundTrip_KeepsIndicesAndDeltas()
        {
            int[] indices = { 0, 3, 9 };
            Vector3[] deltas = { new Vector3(0.001f, -0.002f, 0.003f), Vector3.zero, new Vector3(-0.01f, 0.02f, 0f) };

            PerfectSyncCodec.Encode(indices, deltas, out string indicesText, out string deltasText);
            bool ok = PerfectSyncCodec.TryDecode(indicesText, deltasText, VertexCount,
                out int[] decodedIndices, out Vector3[] decodedDeltas, out string error);

            // 番号はそのまま、差分は half float の精度で戻ること
            Assert.That(ok, Is.True, error);
            Assert.That(decodedIndices, Is.EqualTo(indices));
            for (int i = 0; i < deltas.Length; i++)
            {
                Assert.That(Vector3.Distance(decodedDeltas[i], deltas[i]), Is.LessThan(1e-5f));
            }
        }

        [Test]
        public void Codec_DescendingIndices_Rejected()
        {
            PerfectSyncCodec.Encode(new[] { 5, 2 }, new[] { Vector3.one * 0.01f, Vector3.one * 0.01f },
                out string indicesText, out string deltasText);

            Assert.That(PerfectSyncCodec.TryDecode(indicesText, deltasText, VertexCount, out _, out _, out _), Is.False);
        }

        [Test]
        public void Codec_IndexOutsideMesh_Rejected()
        {
            PerfectSyncCodec.Encode(new[] { VertexCount }, new[] { Vector3.one * 0.01f },
                out string indicesText, out string deltasText);

            Assert.That(PerfectSyncCodec.TryDecode(indicesText, deltasText, VertexCount, out _, out _, out _), Is.False);
        }

        [Test]
        public void Codec_MismatchedOrBrokenData_Rejected()
        {
            PerfectSyncCodec.Encode(new[] { 1, 2 }, new[] { Vector3.one * 0.01f, Vector3.one * 0.01f },
                out string indicesText, out string deltasText);
            PerfectSyncCodec.Encode(new[] { 1 }, new[] { Vector3.one * 0.01f }, out _, out string shortDeltas);

            // 番号と差分の数の不一致・base64 でない文字列は不正
            Assert.That(PerfectSyncCodec.TryDecode(indicesText, shortDeltas, VertexCount, out _, out _, out _), Is.False);
            Assert.That(PerfectSyncCodec.TryDecode("not base64!", deltasText, VertexCount, out _, out _, out _), Is.False);
        }

        [Test]
        public void HashVertices_ChangesWhenVertexMoves()
        {
            var vertices = new[] { new Vector3(0f, 1f, 2f), new Vector3(0.1f, 0.2f, 0.3f) };
            string first = PerfectSyncCodec.HashVertices(vertices);

            // 同じ入力は同じ値、1 mm 動かすと別の値（小文字 16 進 16 桁）
            Assert.That(PerfectSyncCodec.HashVertices(vertices), Is.EqualTo(first));
            vertices[1].x += 0.001f;
            Assert.That(PerfectSyncCodec.HashVertices(vertices), Is.Not.EqualTo(first));
            Assert.That(first, Does.Match("^[0-9a-f]{16}$"));
        }

        [Test]
        public void Set_Valid_PassesValidation()
        {
            Assert.That(CreateSet("eyeBlinkLeft").Validate(), Is.Null);
        }

        [Test]
        public void Set_InvalidContents_FailValidation()
        {
            // 知らない版・不正なハッシュ・重複した形状名
            PerfectSyncSet version = CreateSet("eyeBlinkLeft");
            version.version = PerfectSyncSet.CurrentVersion + 1;
            PerfectSyncSet hash = CreateSet("eyeBlinkLeft");
            hash.meshes[0].vertexHash = "XYZ";
            PerfectSyncSet duplicate = CreateSet("eyeBlinkLeft", "eyeBlinkLeft");

            Assert.That(version.Validate(), Is.Not.Null);
            Assert.That(hash.Validate(), Is.Not.Null);
            Assert.That(duplicate.Validate(), Is.Not.Null);
        }

        [TestCase("eyeBlinkLeft", true)]
        [TestCase("jawOpen2", true)]
        [TestCase("", false)]
        [TestCase("1abc", false)]
        [TestCase("eye-blink", false)]
        [TestCase("../evil", false)]
        public void IsValidShapeName_Cases(string name, bool expected)
        {
            Assert.That(PerfectSyncSet.IsValidShapeName(name), Is.EqualTo(expected));
        }

        [Test]
        public void ReadPerfectSyncOnly_ValidEntries_LoadsShapes()
        {
            string path = WritePackage(CreateSet("jawOpen"), CreateShape("jawOpen", new[] { 2, 4 }));

            PerfectSyncData data = AvatarPackageReader.ReadPerfectSyncOnly(path);

            // 目次と形状の差分がそのまま読めること
            Assert.That(data.Meshes, Has.Count.EqualTo(1));
            Assert.That(data.Meshes[0].path, Is.EqualTo("Body"));
            Assert.That(data.Shapes, Has.Count.EqualTo(1));
            Assert.That(data.Shapes[0].Name, Is.EqualTo("jawOpen"));
            Assert.That(data.Shapes[0].Meshes[0].Indices, Is.EqualTo(new[] { 2, 4 }));
        }

        [Test]
        public void ReadPerfectSyncOnly_WithoutEntries_ReturnsEmpty()
        {
            string path = WritePackage(null);

            Assert.That(AvatarPackageReader.ReadPerfectSyncOnly(path).IsEmpty, Is.True);
        }

        [Test]
        public void ReadPerfectSyncOnly_MissingOrInvalidShape_Skipped()
        {
            // 形状ファイルが無いもの・頂点数を超える番号を持つものは飛ばし、正しいものだけ読む
            string path = WritePackage(CreateSet("jawOpen", "mouthClose", "eyeBlinkLeft"),
                CreateShape("jawOpen", new[] { 1 }), CreateShape("eyeBlinkLeft", new[] { VertexCount + 5 }));

            PerfectSyncData data = AvatarPackageReader.ReadPerfectSyncOnly(path);

            Assert.That(data.Shapes, Has.Count.EqualTo(1));
            Assert.That(data.Shapes[0].Name, Is.EqualTo("jawOpen"));
        }

        [Test]
        public void ReadPerfectSyncOnly_UnsupportedVersion_ReturnsEmpty()
        {
            PerfectSyncSet set = CreateSet("jawOpen");
            set.version = PerfectSyncSet.CurrentVersion + 1;
            string path = WritePackage(set, CreateShape("jawOpen", new[] { 1 }));

            Assert.That(AvatarPackageReader.ReadPerfectSyncOnly(path).IsEmpty, Is.True);
        }

        [Test]
        public void ReplacePerfectSync_FirstSave_AddsEntriesAndKeepsBackup()
        {
            string path = WritePackage(null);
            byte[] original = File.ReadAllBytes(path);

            string backup = AvatarPackageWriter.ReplacePerfectSync(path, CreateSet("jawOpen"),
                new[] { CreateShape("jawOpen", new[] { 3 }) });

            // 形状が追加され、bundle はそのまま、元のファイルが .bak に残ること
            Assert.That(backup, Is.EqualTo(path + AvatarPackageWriter.BackupSuffix));
            Assert.That(File.ReadAllBytes(backup), Is.EqualTo(original));
            Assert.That(PackageTestFiles.ReadEntry(path, AvatarPackageLayout.BundleEntry), Is.EqualTo(PackageTestFiles.DummyBundle));
            Assert.That(AvatarPackageReader.ReadPerfectSyncOnly(path).Shapes[0].Name, Is.EqualTo("jawOpen"));
        }

        [Test]
        public void ReplacePerfectSync_SecondSave_ReplacesShapesWithoutNewBackup()
        {
            string path = WritePackage(CreateSet("jawOpen"), CreateShape("jawOpen", new[] { 3 }));
            AvatarPackageWriter.ReplacePerfectSync(path, CreateSet("jawOpen"), new[] { CreateShape("jawOpen", new[] { 3 }) });

            string backup = AvatarPackageWriter.ReplacePerfectSync(path, CreateSet("mouthClose"),
                new[] { CreateShape("mouthClose", new[] { 5 }) });

            // 控えは作り直さず、前の形状のファイルは消えること
            Assert.That(backup, Is.Null);
            List<string> names = PackageTestFiles.EntryNames(path);
            Assert.That(names, Does.Not.Contain(PerfectSyncSet.ShapeEntryName("jawOpen")));
            Assert.That(names, Does.Contain(PerfectSyncSet.ShapeEntryName("mouthClose")));
            Assert.That(names, Does.Contain(AvatarPackageLayout.ManifestEntry));
        }

        [Test]
        public void ReplacePerfectSync_EmptySet_RemovesEntries()
        {
            string path = WritePackage(CreateSet("jawOpen"), CreateShape("jawOpen", new[] { 3 }));

            AvatarPackageWriter.ReplacePerfectSync(path, new PerfectSyncSet(), Array.Empty<PerfectSyncShape>());

            // 形状のエントリが残らず、他のエントリは残ること
            List<string> names = PackageTestFiles.EntryNames(path);
            Assert.That(names.Exists(AvatarPackageWriter.IsPerfectSyncEntry), Is.False);
            Assert.That(names, Does.Contain(AvatarPackageLayout.BundleEntry));
        }

        [Test]
        public void ReplacePerfectSync_InvalidShape_ThrowsAndKeepsFile()
        {
            string path = WritePackage(null);
            byte[] original = File.ReadAllBytes(path);

            // 目次と形状の名前が違えば書かないこと
            Assert.Throws<AvatarPackageException>(() => AvatarPackageWriter.ReplacePerfectSync(
                path, CreateSet("jawOpen"), new[] { CreateShape("mouthClose", new[] { 3 }) }));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
            Assert.That(File.Exists(path + AvatarPackageWriter.BackupSuffix), Is.False);
        }

        [Test]
        public void Weld_SamePosition_SharesRepresentative()
        {
            var positions = new[] { Vector3.zero, Vector3.one, Vector3.zero, Vector3.one * 2f };
            var mask = new[] { true, true, true, false };

            int[] reps = VertexMatching.Weld(positions, mask, 1e-5f);

            // 同じ位置は小さい番号に、対象外は -1
            Assert.That(reps, Is.EqualTo(new[] { 0, 1, 0, -1 }));
        }

        [Test]
        public void FindMirror_SymmetricPoints_FindsPartnersAcrossX()
        {
            // X = 0 で左右対称な点（中央の点は自分自身が相手）
            var positions = new[]
            {
                new Vector3(-1f, 0.1f, 0.2f), new Vector3(1f, 0.1f, 0.2f),
                new Vector3(-0.5f, 0.7f, 0.3f), new Vector3(0.5f, 0.7f, 0.3f),
                new Vector3(0f, 1.3f, 0.9f),
            };
            int[] reps = VertexMatching.Weld(positions, null, 1e-5f);

            MirrorResult mirror = VertexMatching.FindMirror(positions, reps, 1e-4f);

            Assert.That(mirror.Axis, Is.EqualTo(0));
            Assert.That(mirror.Partners, Is.EqualTo(new[] { 1, 0, 3, 2, 4 }));
            Assert.That(mirror.MirrorVector(new Vector3(1f, 2f, 3f)), Is.EqualTo(new Vector3(-1f, 2f, 3f)));
        }

        private static PerfectSyncSet CreateSet(params string[] shapes)
        {
            // メッシュ 1 つ（Body）の目次
            return new PerfectSyncSet
            {
                meshes = new[] { new PerfectSyncMeshInfo { path = "Body", vertexCount = VertexCount, vertexHash = Hash } },
                shapes = shapes,
            };
        }

        private static PerfectSyncShape CreateShape(string name, int[] indices)
        {
            // 指定の頂点を少しずつ動かす差分
            var deltas = new Vector3[indices.Length];
            for (int i = 0; i < deltas.Length; i++)
            {
                deltas[i] = new Vector3(0f, 0.001f * (i + 1), 0f);
            }

            PerfectSyncCodec.Encode(indices, deltas, out string indicesText, out string deltasText);
            return new PerfectSyncShape
            {
                name = name,
                meshes = new[] { new PerfectSyncShapeMesh { mesh = 0, indices = indicesText, deltas = deltasText } },
            };
        }

        private string WritePackage(PerfectSyncSet set, params PerfectSyncShape[] shapes)
        {
            // 目次（null なら無し）と形状ファイルを metadata に加える
            var extras = new List<KeyValuePair<string, string>>();
            if (set != null)
            {
                extras.Add(new KeyValuePair<string, string>(AvatarPackageLayout.PerfectSyncEntry, JsonUtility.ToJson(set)));
            }

            foreach (PerfectSyncShape shape in shapes)
            {
                extras.Add(new KeyValuePair<string, string>(PerfectSyncSet.ShapeEntryName(shape.name), JsonUtility.ToJson(shape)));
            }

            string path = Path.Combine(_directory, "test" + AvatarPackageLayout.Extension);
            PackageTestFiles.Write(path, PackageTestFiles.CreateManifest(PackageTestFiles.DummyBundle),
                PackageTestFiles.DummyBundle, extras);
            return path;
        }
    }
}
