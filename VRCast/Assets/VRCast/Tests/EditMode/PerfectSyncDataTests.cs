using System.IO;
using NUnit.Framework;
using VRCast.AvatarFormat;

namespace VRCast.Tests
{
    /// <summary>
    /// パーフェクトシンク化の保存データ（形状のバイナリ・設定の JSON）の読み書きと検証を確認する。
    /// </summary>
    public class PerfectSyncDataTests
    {
        [Test]
        public void ShapeData_RoundTripsThroughBytes()
        {
            // 2 つのメッシュの形状を書いて読み戻す
            var data = new PerfectSyncShapeData();
            data.Shapes.Add(new PerfectSyncShape("Body", "jawOpen", new[] { 0, 5 }, new[] { 0f, -0.1f, 0f, 0.2f, 0f, 0.01f }));
            data.Shapes.Add(new PerfectSyncShape("Teeth", "jawOpen", new[] { 3 }, new[] { 0f, -0.05f, 0f }));

            PerfectSyncShapeData read = PerfectSyncShapeData.FromBytes(data.ToBytes());

            // 件数・名前・頂点・移動量がそのまま戻ること
            Assert.That(read.Shapes, Has.Count.EqualTo(2));
            Assert.That(read.Shapes[0].Path, Is.EqualTo("Body"));
            Assert.That(read.Shapes[0].Name, Is.EqualTo("jawOpen"));
            Assert.That(read.Shapes[0].Vertices, Is.EqualTo(new[] { 0, 5 }));
            Assert.That(read.Shapes[0].Deltas, Is.EqualTo(new[] { 0f, -0.1f, 0f, 0.2f, 0f, 0.01f }));
            Assert.That(read.Shapes[1].Path, Is.EqualTo("Teeth"));
        }

        [Test]
        public void ShapeData_FromBytes_RejectsBrokenData()
        {
            // 識別子違い・途中で切れたデータは InvalidDataException になること
            Assert.Throws<InvalidDataException>(() => PerfectSyncShapeData.FromBytes(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }));

            var data = new PerfectSyncShapeData();
            data.Shapes.Add(new PerfectSyncShape("Body", "jawOpen", new[] { 0 }, new[] { 0f, -0.1f, 0f }));
            byte[] bytes = data.ToBytes();
            byte[] truncated = new byte[bytes.Length - 6];
            System.Array.Copy(bytes, truncated, truncated.Length);
            Assert.Throws<InvalidDataException>(() => PerfectSyncShapeData.FromBytes(truncated));
            Assert.Throws<InvalidDataException>(() => PerfectSyncShapeData.FromBytes(new byte[0]));
        }

        [Test]
        public void Shape_Validate_RejectsInvalidContent()
        {
            // 移動量の数の不一致・負の頂点番号・NaN・名前なしは不正
            Assert.That(new PerfectSyncShape("Body", "jawOpen", new[] { 0 }, new[] { 0f }).Validate(), Is.Not.Null);
            Assert.That(new PerfectSyncShape("Body", "jawOpen", new[] { -1 }, new[] { 0f, 0f, 0f }).Validate(), Is.Not.Null);
            Assert.That(new PerfectSyncShape("Body", "jawOpen", new[] { 0 }, new[] { float.NaN, 0f, 0f }).Validate(),
                Is.Not.Null);
            Assert.That(new PerfectSyncShape("Body", string.Empty, new[] { 0 }, new[] { 0f, 0f, 0f }).Validate(), Is.Not.Null);
            Assert.That(new PerfectSyncShape(string.Empty, "jawOpen", new[] { 0 }, new[] { 0f, 0f, 0f }).Validate(), Is.Null);
        }

        [Test]
        public void ShapeData_Validate_AcceptsValidShapesAndRejectsNull()
        {
            // 正しい形状だけなら保存でき、空の要素があれば不正
            var data = new PerfectSyncShapeData();
            data.Shapes.Add(new PerfectSyncShape("Body", "jawOpen", new[] { 0 }, new[] { 0f, -0.01f, 0f }));
            Assert.That(data.Validate(), Is.Null);
            data.Shapes.Add(null);
            Assert.That(data.Validate(), Is.Not.Null);
        }

        [Test]
        public void Set_Validate_ChecksRanges()
        {
            // 既定の空の設定は有効で、パーフェクトシンク版ではない扱い
            var set = new PerfectSyncSet();
            Assert.That(set.Validate(), Is.Null);
            Assert.That(set.IsEmpty, Is.True);

            // 目印・形状・割り当てがある設定は有効
            set.landmarks = new[] { new PerfectSyncLandmark { id = 0, triangle = 3, u = 0.2f, v = 0.3f } };
            set.shapes = new[] { new PerfectSyncShapeSetting { name = "jawOpen", source = PerfectSyncSet.SourceGenerated } };
            set.bindings = new[] { new PerfectSyncBinding { name = "eyeBlinkLeft", path = "Body", blendShape = "blink_L" } };
            Assert.That(set.Validate(), Is.Null);
            Assert.That(set.IsEmpty, Is.False);

            // 三角形の外の重心座標・範囲外の出どころ・強すぎる割り当て・範囲外の細かさは不正
            set.landmarks[0].u = 0.8f;
            Assert.That(set.Validate(), Is.Not.Null);
            set.landmarks[0].u = 0.2f;
            set.shapes[0].source = 5;
            Assert.That(set.Validate(), Is.Not.Null);
            set.shapes[0].source = PerfectSyncSet.SourceNone;
            set.bindings[0].weight = PerfectSyncSet.MaxStrength + 1f;
            Assert.That(set.Validate(), Is.Not.Null);
            set.bindings[0].weight = 1f;
            set.detail = 3;
            Assert.That(set.Validate(), Is.Not.Null);
            set.detail = 1;

            // 無い部位は定義済みのビット（0〜7）だけ
            set.absentParts = PerfectSyncSet.MaxAbsentParts;
            Assert.That(set.Validate(), Is.Null);
            set.absentParts = PerfectSyncSet.MaxAbsentParts + 1;
            Assert.That(set.Validate(), Is.Not.Null);
            set.absentParts = -1;
            Assert.That(set.Validate(), Is.Not.Null);
            set.absentParts = 0;

            // 作り方は式か転写だけ。項目が無いファイルは式（0）として有効
            Assert.That(set.method, Is.EqualTo(PerfectSyncSet.MethodFormula));
            set.method = PerfectSyncSet.MethodTransfer;
            Assert.That(set.Validate(), Is.Null);
            set.method = PerfectSyncSet.MethodTransfer + 1;
            Assert.That(set.Validate(), Is.Not.Null);
        }
    }
}
