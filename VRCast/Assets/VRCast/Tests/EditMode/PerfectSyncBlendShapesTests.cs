using System;
using NUnit.Framework;
using UnityEngine;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// パーフェクトシンクの BlendShape 名の照合と、左右の対応・書き込みを検証する。
    /// </summary>
    public class PerfectSyncBlendShapesTests
    {
        [Test]
        public void Match_AcceptsCommonNamingVariants()
        {
            int blinkLeft = Array.IndexOf(MediaPipePacket.BlendShapeNames, "eyeBlinkLeft");

            // 正式名・大文字始まり・区切り記号・L 表記・FBX の接頭辞付きは同じ名前とみなすこと
            Assert.That(PerfectSyncBlendShapes.Match("eyeBlinkLeft"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("EyeBlinkLeft"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("eye_blink_left"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("eyeBlink_L"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("blendShape1.eyeBlinkLeft"), Is.EqualTo(blinkLeft));
        }

        [Test]
        public void Match_RejectsOtherNames()
        {
            // ARKit 名でない名前・空文字は対象外
            Assert.That(PerfectSyncBlendShapes.Match("vrc.v_aa"), Is.EqualTo(-1));
            Assert.That(PerfectSyncBlendShapes.Match("blink"), Is.EqualTo(-1));
            Assert.That(PerfectSyncBlendShapes.Match(string.Empty), Is.EqualTo(-1));
            Assert.That(PerfectSyncBlendShapes.Match(null), Is.EqualTo(-1));
        }

        [Test]
        public void SourceIndex_SwapsSidesOnlyWithoutMirror()
        {
            string[] names = MediaPipePacket.BlendShapeNames;
            int smileLeft = Array.IndexOf(names, "mouthSmileLeft");
            int smileRight = Array.IndexOf(names, "mouthSmileRight");
            int jawOpen = Array.IndexOf(names, "jawOpen");

            // 受信値の左右は映像基準のため、鏡像 OFF では入れ替え、鏡像 ON ではそのまま。左右の無い名前は常にそのまま
            Assert.That(PerfectSyncBlendShapes.SourceIndex(smileLeft, false), Is.EqualTo(smileRight));
            Assert.That(PerfectSyncBlendShapes.SourceIndex(smileRight, false), Is.EqualTo(smileLeft));
            Assert.That(PerfectSyncBlendShapes.SourceIndex(smileLeft, true), Is.EqualTo(smileLeft));
            Assert.That(PerfectSyncBlendShapes.SourceIndex(jawOpen, false), Is.EqualTo(jawOpen));
        }

        [Test]
        public void Create_FindsShapesAndApplyWritesWeights()
        {
            // ARKit 名を全部持つ顔メッシュを用意
            string[] names = MediaPipePacket.BlendShapeNames;
            var root = new GameObject("Avatar");
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            var deltas = new Vector3[3];
            foreach (string name in names)
            {
                mesh.AddBlendShapeFrame(name, 100f, deltas, null, null);
            }

            renderer.sharedMesh = mesh;
            try
            {
                // 全種類を見つけ、まばたき・顎を動かせると判定すること
                PerfectSyncBlendShapes sync = PerfectSyncBlendShapes.Create(root.transform);
                Assert.That(sync.MatchedCount, Is.EqualTo(names.Length));
                Assert.That(sync.IsAvailable, Is.True);
                Assert.That(sync.DrivesBlink, Is.True);
                Assert.That(sync.DrivesJaw, Is.True);

                // 平滑化を飛ばす長い経過時間で、映像上の左の笑顔（鏡像 OFF ではアバターの右）と顎を最大にして書く
                var scores = new float[names.Length];
                scores[Array.IndexOf(names, "mouthSmileLeft")] = 1f;
                scores[Array.IndexOf(names, "jawOpen")] = 0.5f;
                sync.Apply(scores, false, 10f);
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(100f).Within(0.01f));
                Assert.That(Weight(renderer, "mouthSmileLeft"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "jawOpen"), Is.EqualTo(50f).Within(0.01f));

                // 解除すると元の値（0）へ戻ること
                sync.Release();
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "jawOpen"), Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static float Weight(SkinnedMeshRenderer renderer, string name)
        {
            // 名前で BlendShape の現在値を読む
            return renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(name));
        }
    }
}
