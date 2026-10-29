using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
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
            int blinkLeft = Array.IndexOf(ArKitFace.BlendShapeNames, "eyeBlinkLeft");
            int tongueOut = Array.IndexOf(ArKitFace.BlendShapeNames, "tongueOut");

            // 正式名・大文字始まり・区切り記号・L 表記・FBX の接頭辞付きは同じ名前とみなすこと
            Assert.That(PerfectSyncBlendShapes.Match("eyeBlinkLeft"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("EyeBlinkLeft"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("eye_blink_left"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("eyeBlink_L"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("blendShape1.eyeBlinkLeft"), Is.EqualTo(blinkLeft));

            // 舌出し（iPhone / VMC）も区切り記号の揺れを吸収すること
            Assert.That(tongueOut, Is.GreaterThanOrEqualTo(0));
            Assert.That(PerfectSyncBlendShapes.Match("tongueOut"), Is.EqualTo(tongueOut));
            Assert.That(PerfectSyncBlendShapes.Match("tongue_out"), Is.EqualTo(tongueOut));
            Assert.That(PerfectSyncBlendShapes.Match("TongueOut"), Is.EqualTo(tongueOut));
        }

        [Test]
        public void Boost_EstimatesCheekPuffFromWidenAndGates()
        {
            var scores = new float[ArKitFace.BlendShapeNames.Length];

            // 1.5〜6% の範囲: 真顔（1%）は 0、中間（3.75%）は 0.5、6% 以上は 1 になること
            Assert.That(PerfectSyncBoost.EstimateCheekPuff(0.01f, scores, 1.5f, 6f), Is.EqualTo(0f));
            Assert.That(PerfectSyncBoost.EstimateCheekPuff(0.0375f, scores, 1.5f, 6f), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(PerfectSyncBoost.EstimateCheekPuff(0.1f, scores, 1.5f, 6f), Is.EqualTo(1f));

            // 口を大きく開けているときは推定しないこと（真顔でも出る小さな jawOpen では消さない）
            scores[ArKitFace.JawOpen] = 0.8f;
            Assert.That(PerfectSyncBoost.EstimateCheekPuff(0.1f, scores, 1.5f, 6f), Is.EqualTo(0f));
        }

        [Test]
        public void Boost_StrengthensPoutOnly()
        {
            var boost = new PerfectSyncBoost(2f, 1f, 1f, 1f, 0f, 0f, 0f);

            // ふくれっ面の倍率 2: とがらせは 2 倍、口の左右・顎は掛けないこと
            Assert.That(boost.Apply(ArKitFace.MouthPucker, 0.4f), Is.EqualTo(0.8f).Within(1e-5f));
            Assert.That(boost.Apply(ArKitFace.IndexOf("mouthLeft"), 0.4f), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(boost.Apply(ArKitFace.JawOpen, 0.4f), Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void Boost_UsesLargerOfReceivedAndEstimatedMouthSides()
        {
            int mouthLeft = ArKitFace.IndexOf("mouthLeft");
            int mouthRight = ArKitFace.IndexOf("mouthRight");

            // 映像の右へ 5%（開始 2%・最大 8%）= 右 0.5、左 0
            PerfectSyncBoost.EstimateMouthSides(0.05f, out float imageLeft, out float imageRight);
            Assert.That(imageLeft, Is.EqualTo(0f));
            Assert.That(imageRight, Is.EqualTo(0.5f).Within(1e-4f));

            // 倍率 2: 右は推定 0.5 が受信 0.1 より大きいので 1、左は推定 0 なので受信 0.1 を強めた 0.2、口の左右以外は強めない
            var boost = new PerfectSyncBoost(1f, 2f, 1f, 1f, 0f, imageLeft, imageRight);
            Assert.That(boost.Apply(mouthRight, 0.1f), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(boost.Apply(mouthLeft, 0.1f), Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(boost.Apply(ArKitFace.JawOpen, 0.4f), Is.EqualTo(0.4f).Within(1e-5f));
        }

        [Test]
        public void Boost_UsesLargerOfReceivedAndEstimatedCheekPuff()
        {
            int cheekPuff = ArKitFace.IndexOf("cheekPuff");

            // 倍率 2: 推定 0.3 が受信 0.05 より大きいので 0.6、既定値は強調しないこと
            var boost = new PerfectSyncBoost(1f, 1f, 2f, 1f, 0.3f, 0f, 0f);
            Assert.That(boost.Apply(cheekPuff, 0.05f), Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(default(PerfectSyncBoost).Apply(cheekPuff, 0.05f), Is.EqualTo(0.05f).Within(1e-5f));
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
        public void Match_UnifiedAliasesMapToArKitSlots()
        {
            int blinkLeft = Array.IndexOf(ArKitFace.BlendShapeNames, "eyeBlinkLeft");
            int blinkRight = Array.IndexOf(ArKitFace.BlendShapeNames, "eyeBlinkRight");
            int jawOpen = Array.IndexOf(ArKitFace.BlendShapeNames, "jawOpen");
            int mouthClose = Array.IndexOf(ArKitFace.BlendShapeNames, "mouthClose");
            int mouthFunnel = Array.IndexOf(ArKitFace.BlendShapeNames, "mouthFunnel");
            int mouthPucker = Array.IndexOf(ArKitFace.BlendShapeNames, "mouthPucker");
            int cheekPuff = Array.IndexOf(ArKitFace.BlendShapeNames, "cheekPuff");

            // Unified Expressions の別名は Unified 系統でのみ ARKit 枠へ当たること
            Assert.That(PerfectSyncBlendShapes.Match("EyeClosedLeft", PerfectSyncNaming.Unified), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.Match("EyeClosedRight", PerfectSyncNaming.Unified), Is.EqualTo(blinkRight));
            Assert.That(PerfectSyncBlendShapes.Match("MouthClosed", PerfectSyncNaming.Unified), Is.EqualTo(mouthClose));
            Assert.That(PerfectSyncBlendShapes.Match("LipFunnel", PerfectSyncNaming.Unified), Is.EqualTo(mouthFunnel));
            Assert.That(PerfectSyncBlendShapes.Match("LipPuckerUpperLeft", PerfectSyncNaming.Unified), Is.EqualTo(mouthPucker));
            Assert.That(PerfectSyncBlendShapes.Match("CheekPuffLeft", PerfectSyncNaming.Unified), Is.EqualTo(cheekPuff));
            Assert.That(PerfectSyncBlendShapes.Match("JawOpen", PerfectSyncNaming.Unified), Is.EqualTo(jawOpen));

            // ARKit 系統では UE の別名は拾わない（eyeBlink と EyeClosed の二重駆動を防ぐ）
            Assert.That(PerfectSyncBlendShapes.Match("EyeClosedLeft", PerfectSyncNaming.ArKit), Is.EqualTo(-1));
            Assert.That(PerfectSyncBlendShapes.Match("LipFunnel", PerfectSyncNaming.ArKit), Is.EqualTo(-1));

            // MatchAny はどちらの系統でも拾う
            Assert.That(PerfectSyncBlendShapes.MatchAny("EyeClosedLeft"), Is.EqualTo(blinkLeft));
            Assert.That(PerfectSyncBlendShapes.MatchAny("eyeBlinkLeft"), Is.EqualTo(blinkLeft));
        }

        [Test]
        public void DetectNaming_PrefersArKitThenUnified()
        {
            string[] arKitNames = ArKitFace.BlendShapeNames;
            var arKitRoot = MakeFace("ArKitAvatar", arKitNames);
            var ueNames = new List<string>();
            foreach (string name in arKitNames)
            {
                // ARKit のまばたき・口まわりを UE 別名に置き換えたセット
                if (name == "eyeBlinkLeft")
                {
                    ueNames.Add("EyeClosedLeft");
                }
                else if (name == "eyeBlinkRight")
                {
                    ueNames.Add("EyeClosedRight");
                }
                else if (name == "mouthClose")
                {
                    ueNames.Add("MouthClosed");
                }
                else if (name == "mouthFunnel")
                {
                    ueNames.Add("LipFunnel");
                }
                else if (name == "mouthPucker")
                {
                    ueNames.Add("LipPucker");
                }
                else if (name == "mouthRollUpper")
                {
                    ueNames.Add("LipSuckUpper");
                }
                else if (name == "mouthRollLower")
                {
                    ueNames.Add("LipSuckLower");
                }
                else if (name == "mouthShrugUpper")
                {
                    ueNames.Add("MouthRaiserUpper");
                }
                else if (name == "mouthShrugLower")
                {
                    ueNames.Add("MouthRaiserLower");
                }
                else
                {
                    ueNames.Add(name);
                }
            }

            var ueRoot = MakeFace("UeAvatar", ueNames);
            var mixedRoot = MakeFace("Mixed", new[] { "eyeBlinkLeft", "eyeBlinkRight", "JawOpen", "EyeClosedLeft" });
            try
            {
                // ARKit 名が多いアバターは ARKit、UE 別名が多いアバターは Unified（両方足りても多い方）
                Assert.That(PerfectSyncBlendShapes.DetectNaming(arKitRoot.transform), Is.EqualTo(PerfectSyncNaming.ArKit));
                Assert.That(PerfectSyncBlendShapes.DetectNaming(ueRoot.transform), Is.EqualTo(PerfectSyncNaming.Unified));
                PerfectSyncBlendShapes.CountMatches(ueRoot.transform, out int ueArKit, out int ueUnified);
                Assert.That(ueUnified, Is.GreaterThan(ueArKit));

                // どちらも規定数未満なら件数が多い方（同数なら ARKit）
                Assert.That(PerfectSyncBlendShapes.DetectNaming(mixedRoot.transform), Is.EqualTo(PerfectSyncNaming.ArKit));

                // 割り当てがあれば ARKit 固定
                var bindings = new[]
                {
                    new PerfectSyncBinding { name = "jawOpen", path = "", blendShape = "JawOpen", weight = 1f },
                };
                Assert.That(PerfectSyncBlendShapes.DetectNaming(ueRoot.transform, bindings: bindings),
                    Is.EqualTo(PerfectSyncNaming.ArKit));

                // 設定で系統を固定できること（自動なら UE になるアバターでも ARKit を選べる）
                Assert.That(
                    PerfectSyncBlendShapes.DetectNaming(ueRoot.transform, mode: PerfectSyncNamingMode.ArKit),
                    Is.EqualTo(PerfectSyncNaming.ArKit));
                Assert.That(
                    PerfectSyncBlendShapes.DetectNaming(arKitRoot.transform, mode: PerfectSyncNamingMode.Unified),
                    Is.EqualTo(PerfectSyncNaming.Unified));
            }
            finally
            {
                DestroyFace(arKitRoot);
                DestroyFace(ueRoot);
                DestroyFace(mixedRoot);
            }
        }

        [Test]
        public void Create_UnifiedModeWritesUeNamedShapes()
        {
            // UE 別名だけの顔（まばたき・口）
            var root = MakeFace("UeFace", new[]
            {
                "EyeClosedLeft", "EyeClosedRight", "JawOpen", "LipPucker", "MouthSmileLeft", "MouthSmileRight",
            });
            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            try
            {
                PerfectSyncBlendShapes sync = PerfectSyncBlendShapes.Create(
                    root.transform, naming: PerfectSyncNaming.Unified);
                Assert.That(sync.Naming, Is.EqualTo(PerfectSyncNaming.Unified));
                Assert.That(sync.DrivesBlink, Is.True);
                Assert.That(sync.DrivesJaw, Is.True);

                var scores = new float[ArKitFace.BlendShapeNames.Length];
                scores[Array.IndexOf(ArKitFace.BlendShapeNames, "eyeBlinkLeft")] = 1f;
                scores[Array.IndexOf(ArKitFace.BlendShapeNames, "jawOpen")] = 0.5f;
                scores[Array.IndexOf(ArKitFace.BlendShapeNames, "mouthPucker")] = 1f;
                scores[Array.IndexOf(ArKitFace.BlendShapeNames, "mouthSmileLeft")] = 1f;
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true);

                // 鏡像 OFF: 映像の左まばたき → アバターの右（EyeClosedRight）
                Assert.That(Weight(renderer, "EyeClosedRight"), Is.EqualTo(100f).Within(0.01f));
                Assert.That(Weight(renderer, "JawOpen"), Is.EqualTo(50f).Within(0.01f));
                Assert.That(Weight(renderer, "LipPucker"), Is.EqualTo(100f).Within(0.01f));
                Assert.That(Weight(renderer, "MouthSmileRight"), Is.EqualTo(100f).Within(0.01f));
            }
            finally
            {
                DestroyFace(root);
            }
        }

        [Test]
        public void Create_DoesNotDriveBothNamingsAtOnce()
        {
            // ARKit と UE のまばたきが両方あるとき、ARKit モードでは EyeClosed を動かさない
            var root = MakeFace("Both", new[] { "eyeBlinkLeft", "eyeBlinkRight", "EyeClosedLeft", "EyeClosedRight" });
            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            try
            {
                PerfectSyncBlendShapes arKit = PerfectSyncBlendShapes.Create(
                    root.transform, naming: PerfectSyncNaming.ArKit);
                var scores = new float[ArKitFace.BlendShapeNames.Length];
                scores[Array.IndexOf(ArKitFace.BlendShapeNames, "eyeBlinkLeft")] = 1f;
                arKit.Apply(scores, ArKitRange.MediaPipe, false, 10f, true);
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(100f).Within(0.01f));
                Assert.That(Weight(renderer, "EyeClosedRight"), Is.EqualTo(0f).Within(0.01f));
                arKit.Release();

                PerfectSyncBlendShapes unified = PerfectSyncBlendShapes.Create(
                    root.transform, naming: PerfectSyncNaming.Unified);
                unified.Apply(scores, ArKitRange.MediaPipe, false, 10f, true);
                Assert.That(Weight(renderer, "EyeClosedRight"), Is.EqualTo(100f).Within(0.01f));
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                DestroyFace(root);
            }
        }

        [Test]
        public void SourceIndex_SwapsSidesOnlyWithoutMirror()
        {
            string[] names = ArKitFace.BlendShapeNames;
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
            string[] names = ArKitFace.BlendShapeNames;
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
                scores[Array.IndexOf(names, "eyeBlinkLeft")] = 1f;
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true);
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(100f).Within(0.01f));
                Assert.That(Weight(renderer, "mouthSmileLeft"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "jawOpen"), Is.EqualTo(50f).Within(0.01f));
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(100f).Within(0.01f));

                // 口の開きの補正: 無視する開き以下は閉じ、倍率で抑えられること（ほかの形は変わらない）
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true, default, default,
                    new JawOpenAdjustment { Deadzone = 0.5f });
                Assert.That(Weight(renderer, "jawOpen"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(100f).Within(0.01f));
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true, default, default,
                    new JawOpenAdjustment { Gain = 0.5f });
                Assert.That(Weight(renderer, "jawOpen"), Is.EqualTo(25f).Within(0.01f));

                // 目の開きの補正: 閉じ切ったまばたきは閉じたまま、補正分までの半目は開いた目になること
                var open = new EyeAdjustment { OpenOffset = 0.5f };
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true, open);
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(100f).Within(0.01f));
                scores[Array.IndexOf(names, "eyeBlinkLeft")] = 0.35f;
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true, open);
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(0f).Within(0.01f));
                scores[Array.IndexOf(names, "eyeBlinkLeft")] = 1f;

                // 左右をそろえる補正: 片目だけ閉じても両目が同じ閉じ具合（正面では平均）になること
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, true,
                    new EyeAdjustment { Sync = BlinkSyncMode.Always });
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(50f).Within(0.01f));
                Assert.That(Weight(renderer, "eyeBlinkLeft"), Is.EqualTo(50f).Within(0.01f));

                // まばたきを動かさない設定では、まばたきだけ元の値（0）へ戻り、ほかは書き続けること
                sync.Apply(scores, ArKitRange.MediaPipe, false, 10f, false);
                Assert.That(Weight(renderer, "eyeBlinkRight"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(100f).Within(0.01f));

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

        [Test]
        public void JawOpenAdjustment_IgnoresSmallOpeningAndScales()
        {
            // 既定値は何も変えないこと
            Assert.That(default(JawOpenAdjustment).Apply(0.3f), Is.EqualTo(0.3f).Within(0.0001f));

            // 無視する開きまでは 0、残りは 0〜1 に広げること
            var deadzone = new JawOpenAdjustment { Deadzone = 0.2f, Gain = 1f };
            Assert.That(deadzone.Apply(0.15f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(deadzone.Apply(0.6f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(deadzone.Apply(1f), Is.EqualTo(1f).Within(0.0001f));

            // 倍率で抑え・強め、1 を超えないこと
            Assert.That(new JawOpenAdjustment { Gain = 0.5f }.Apply(0.8f), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(new JawOpenAdjustment { Gain = 2f }.Apply(0.8f), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void IsAvailableFor_SwitchesRequiredShapeCount()
        {
            // まばたきの 2 種類だけを持つ顔メッシュを用意
            var root = new GameObject("Avatar");
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            var deltas = new Vector3[3];
            mesh.AddBlendShapeFrame("eyeBlinkLeft", 100f, deltas, null, null);
            mesh.AddBlendShapeFrame("eyeBlinkRight", 100f, deltas, null, null);
            renderer.sharedMesh = mesh;
            try
            {
                // 規定数の条件では非対応、1 種類でもの条件では対応と判定すること
                PerfectSyncBlendShapes sync = PerfectSyncBlendShapes.Create(root.transform);
                Assert.That(sync.MatchedCount, Is.EqualTo(2));
                Assert.That(sync.IsAvailable, Is.False);
                Assert.That(sync.IsAvailableFor(false), Is.False);
                Assert.That(sync.IsAvailableFor(true), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void ApplyThreshold_IgnoresBelowAndStretchesToFull()
        {
            // 0 は補正しないこと
            Assert.That(PerfectSyncBlendShapes.ApplyThreshold(0.4f, 0f), Is.EqualTo(0.4f).Within(0.0001f));

            // しきい値以下は 0、超えた分は 0〜1 に広がり、1 は最大のまま、しきい値 1 は動かさないこと
            Assert.That(PerfectSyncBlendShapes.ApplyThreshold(0.1f, 0.2f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(PerfectSyncBlendShapes.ApplyThreshold(0.2f, 0.2f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(PerfectSyncBlendShapes.ApplyThreshold(0.6f, 0.2f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(PerfectSyncBlendShapes.ApplyThreshold(1f, 0.2f), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(PerfectSyncBlendShapes.ApplyThreshold(0.8f, 1f), Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void ApplyEyeLookLimit_ClampsOnlyLookShapes()
        {
            int look = ArKitFace.IndexOf("eyeLookUpLeft");
            int smile = ArKitFace.IndexOf("mouthSmileLeft");

            // 上限 1 は切らず、見る方向だけ抑え、ほかのシェイプは上限の影響を受けないこと
            Assert.That(PerfectSyncBlendShapes.ApplyEyeLookLimit(look, 0.8f, 1f), Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(PerfectSyncBlendShapes.ApplyEyeLookLimit(look, 0.8f, 0.4f), Is.EqualTo(0.4f).Within(0.0001f));
            Assert.That(PerfectSyncBlendShapes.ApplyEyeLookLimit(look, 0.2f, 0.4f), Is.EqualTo(0.2f).Within(0.0001f));
            Assert.That(PerfectSyncBlendShapes.ApplyEyeLookLimit(look, 0.8f, 0f), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(PerfectSyncBlendShapes.ApplyEyeLookLimit(smile, 0.8f, 0.4f), Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void DisplayName_UsesUnifiedAliasOnlyForReplacedShapes()
        {
            int blink = ArKitFace.IndexOf("eyeBlinkLeft");
            int jaw = ArKitFace.IndexOf("jawOpen");
            int look = ArKitFace.IndexOf("eyeLookUpLeft");
            int pucker = ArKitFace.IndexOf("mouthPucker");

            // ARKit 系統は ARKit 名。Unified は置き換わる枠だけ別名で、見る方向と顎はそのまま
            Assert.That(PerfectSyncBlendShapes.DisplayName(blink, PerfectSyncNaming.ArKit), Is.EqualTo("eyeBlinkLeft"));
            Assert.That(PerfectSyncBlendShapes.DisplayName(blink, PerfectSyncNaming.Unified), Is.EqualTo("EyeClosedLeft"));
            Assert.That(PerfectSyncBlendShapes.DisplayName(pucker, PerfectSyncNaming.Unified), Is.EqualTo("LipPucker"));
            Assert.That(PerfectSyncBlendShapes.DisplayName(jaw, PerfectSyncNaming.Unified), Is.EqualTo("jawOpen"));
            Assert.That(PerfectSyncBlendShapes.DisplayName(look, PerfectSyncNaming.Unified), Is.EqualTo("eyeLookUpLeft"));
        }

        [Test]
        public void Apply_ThresholdThenEyeLookLimit()
        {
            string[] names = ArKitFace.BlendShapeNames;
            GameObject root = MakeFace("Avatar", names);
            var renderer = root.GetComponent<SkinnedMeshRenderer>();
            try
            {
                PerfectSyncBlendShapes sync = PerfectSyncBlendShapes.Create(root.transform);
                int smileLeft = Array.IndexOf(names, "mouthSmileLeft");
                int smileRight = Array.IndexOf(names, "mouthSmileRight");
                int lookUp = Array.IndexOf(names, "eyeLookUpLeft");
                int lookDown = Array.IndexOf(names, "eyeLookDownLeft");
                int lookIn = Array.IndexOf(names, "eyeLookInLeft");
                int brow = Array.IndexOf(names, "browDownLeft");
                int jaw = Array.IndexOf(names, "jawOpen");

                var scores = new float[names.Length];
                scores[smileLeft] = 0.1f;
                scores[smileRight] = 1f;
                scores[lookUp] = 1f;
                scores[lookDown] = 0.2f;
                scores[lookIn] = 1f;
                scores[brow] = 0.6f;
                scores[jaw] = 1f;

                var thresholds = new float[names.Length];
                thresholds[smileLeft] = 0.2f;
                thresholds[lookIn] = 0.5f;
                thresholds[brow] = 0.2f;
                thresholds[jaw] = 1f;

                // 鏡像 ON で左右はそのまま。しきい値のあとで見る方向だけ上限 0.4 に切ること
                sync.Apply(scores, ArKitRange.MediaPipe, true, 10f, true, default, default, default, thresholds, 0.4f);
                Assert.That(Weight(renderer, "mouthSmileLeft"), Is.EqualTo(0f).Within(0.01f));
                Assert.That(Weight(renderer, "mouthSmileRight"), Is.EqualTo(100f).Within(0.01f));
                Assert.That(Weight(renderer, "eyeLookUpLeft"), Is.EqualTo(40f).Within(0.01f));
                Assert.That(Weight(renderer, "eyeLookDownLeft"), Is.EqualTo(20f).Within(0.01f));
                Assert.That(Weight(renderer, "eyeLookInLeft"), Is.EqualTo(40f).Within(0.01f));
                Assert.That(Weight(renderer, "browDownLeft"), Is.EqualTo(50f).Within(0.01f));
                Assert.That(Weight(renderer, "jawOpen"), Is.EqualTo(0f).Within(0.01f));

                // 書き込み値ではなく、しきい値の直前の受信値を出せること
                Assert.That(sync.TryPreThreshold(smileLeft, out float before), Is.True);
                Assert.That(before, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(sync.TryPreThreshold(lookIn, out float lookBefore), Is.True);
                Assert.That(lookBefore, Is.EqualTo(1f).Within(0.0001f));
            }
            finally
            {
                DestroyFace(root);
            }
        }

        private static float Weight(SkinnedMeshRenderer renderer, string name)
        {
            // 名前で BlendShape の現在値を読む
            return renderer.GetBlendShapeWeight(renderer.sharedMesh.GetBlendShapeIndex(name));
        }

        private static GameObject MakeFace(string name, IEnumerable<string> shapeNames)
        {
            var root = new GameObject(name);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            var deltas = new Vector3[3];
            foreach (string shape in shapeNames)
            {
                mesh.AddBlendShapeFrame(shape, 100f, deltas, null, null);
            }

            renderer.sharedMesh = mesh;
            return root;
        }

        private static void DestroyFace(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            var renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            UnityEngine.Object.DestroyImmediate(root);
            if (mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }
}
