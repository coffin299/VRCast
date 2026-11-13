using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Avatars;

namespace VRCast.Tests
{
    /// <summary>
    /// AvatarPackageReader の検証処理（信頼できない入力の拒否）を確認する。
    /// 実際の AssetBundle は使わず、ダミーバイト列で ZIP 構造とハッシュ検証のみを扱う。
    /// </summary>
    public class AvatarPackageReaderTests
    {
        // ダミー bundle の中身
        private static readonly byte[] DummyBundle = { 1, 2, 3, 4, 5, 6, 7, 8 };

        private string _directory;
        private string _cacheRoot;

        [SetUp]
        public void SetUp()
        {
            // テストごとに独立した一時ディレクトリを使う
            _directory = Path.Combine(Path.GetTempPath(), "VRCastPkgTests_" + Guid.NewGuid().ToString("N"));
            _cacheRoot = Path.Combine(_directory, "cache");
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
        public void IsSameUnityLine_ComparesYearAndMinorOnly()
        {
            // 同じ系列のパッチ版の差は互換、系列が違えば非互換とみなすこと
            Assert.That(AvatarPackageReader.IsSameUnityLine("2022.3.22f1", "2022.3.62f3"), Is.True);
            Assert.That(AvatarPackageReader.IsSameUnityLine("2022.3.22f1", "6000.0.30f1"), Is.False);
            Assert.That(AvatarPackageReader.IsSameUnityLine("2022.3.22f1", "2022.2.0f1"), Is.False);
            Assert.That(AvatarPackageReader.IsSameUnityLine(null, "2022.3.22f1"), Is.False);
        }

        [Test]
        public void Extract_ValidPackage_ExtractsBundle()
        {
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle);

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            // 展開された bundle が元のバイト列と一致すること
            Assert.That(File.ReadAllBytes(package.BundlePath), Is.EqualTo(DummyBundle));
            Assert.That(package.Manifest.name, Is.EqualTo("TestAvatar"));
        }

        [Test]
        public void Extract_SecondTime_ReusesCache()
        {
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle);

            // 2 回展開しても同じキャッシュパスになること
            string first = AvatarPackageReader.Extract(path, _cacheRoot).BundlePath;
            string second = AvatarPackageReader.Extract(path, _cacheRoot).BundlePath;
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void Extract_HashMismatch_Throws()
        {
            // manifest のハッシュと異なる中身を格納
            AvatarManifest manifest = CreateManifest(DummyBundle);
            byte[] tampered = { 8, 7, 6, 5, 4, 3, 2, 1 };
            string path = WritePackage(manifest, tampered);

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_MissingManifest_Throws()
        {
            string path = WritePackage(null, DummyBundle);

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_UnsupportedFormatVersion_Throws()
        {
            AvatarManifest manifest = CreateManifest(DummyBundle);
            manifest.formatVersion = AvatarPackageLayout.FormatVersion + 1;
            string path = WritePackage(manifest, DummyBundle);

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_TraversalEntry_Throws()
        {
            // 正常なパッケージに上位参照エントリを追加
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle, "../evil.txt");

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_NotZip_Throws()
        {
            string path = Path.Combine(_directory, "broken" + AvatarPackageLayout.Extension);
            File.WriteAllText(path, "not a zip");

            Assert.Throws<AvatarPackageException>(() => AvatarPackageReader.Extract(path, _cacheRoot));
        }

        [Test]
        public void Extract_WithExpressions_ParsesPresets()
        {
            // 1 プリセット・1 値の表情データを同梱
            var expressions = new ExpressionSet
            {
                presets = new[]
                {
                    new ExpressionPreset
                    {
                        name = "Smile",
                        values = new[] { new BlendShapeValue { path = "Body", blendShape = "smile", weight = 100f } },
                    },
                },
            };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ExpressionsEntry, JsonUtility.ToJson(expressions));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            // 内容がそのまま読めること
            Assert.That(package.Expressions.presets, Has.Length.EqualTo(1));
            Assert.That(package.Expressions.presets[0].name, Is.EqualTo("Smile"));
            Assert.That(package.Expressions.presets[0].values[0].weight, Is.EqualTo(100f));
        }

        [Test]
        public void Extract_WithBoneClip_ParsesPreset()
        {
            // ボーンだけのプリセットも読めること
            const string json =
                "{\"presets\":[{\"name\":\"Tail\",\"values\":[],\"boneClip\":\"VRCastBone_000_Tail\",\"bonePaths\":[\"Tail\"]}]}";
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ExpressionsEntry, json);

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Expressions.presets[0].boneClip, Is.EqualTo("VRCastBone_000_Tail"));
            Assert.That(package.Expressions.presets[0].bonePaths, Is.EqualTo(new[] { "Tail" }));
        }

        [Test]
        public void Extract_InvalidBonePath_IgnoredAsEmpty()
        {
            // 空のボーンパスは検証で弾かれること
            const string json =
                "{\"presets\":[{\"name\":\"Tail\",\"values\":[],\"boneClip\":\"VRCastBone_000_Tail\",\"bonePaths\":[\"\"]}]}";
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ExpressionsEntry, json);

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Expressions.presets, Is.Empty);
        }

        [Test]
        public void Extract_InvalidExpressionsJson_IgnoredAsEmpty()
        {
            // 壊れた表情データでもアバター自体は読めること
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ExpressionsEntry, "{ broken");

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Expressions.presets, Is.Empty);
        }

        [Test]
        public void Extract_OutOfRangeExpressionWeight_IgnoredAsEmpty()
        {
            // 重みが範囲外のプリセットは検証で弾かれること
            const string json =
                "{\"presets\":[{\"name\":\"Bad\",\"values\":[{\"path\":\"\",\"blendShape\":\"a\",\"weight\":500}]}]}";
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ExpressionsEntry, json);

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Expressions.presets, Is.Empty);
        }

        [Test]
        public void Extract_WithDescriptor_ParsesLipSyncAndEyelids()
        {
            // JawFlap 方式とまぶた設定を同梱
            var descriptor = new AvatarDescriptorData
            {
                lipSync = new LipSyncData
                {
                    mode = LipSyncData.ModeJawFlapBlendShape,
                    meshPath = "Body",
                    mouthOpenBlendShape = "mouth_open",
                },
                eyelids = new EyelidData
                {
                    meshPath = "Body",
                    blinkBlendShapes = new[] { "blinkL", "blinkR" },
                    winkLeftBlendShape = "blinkL",
                    winkRightBlendShape = "blinkR",
                },
            };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.DescriptorEntry, JsonUtility.ToJson(descriptor));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            // 内容がそのまま読めること
            Assert.That(package.Descriptor.lipSync.mode, Is.EqualTo(LipSyncData.ModeJawFlapBlendShape));
            Assert.That(package.Descriptor.lipSync.mouthOpenBlendShape, Is.EqualTo("mouth_open"));
            Assert.That(package.Descriptor.eyelids.blinkBlendShapes, Is.EqualTo(new[] { "blinkL", "blinkR" }));
            Assert.That(package.Descriptor.eyelids.winkLeftBlendShape, Is.EqualTo("blinkL"));
            Assert.That(package.Descriptor.eyelids.winkRightBlendShape, Is.EqualTo("blinkR"));
        }

        [Test]
        public void Extract_UnknownLipSyncMode_IgnoredAsEmpty()
        {
            // 未知のモードは検証で弾かれ、既定（none）になること
            const string json = "{\"lipSync\":{\"mode\":\"jawFlapBone\",\"visemes\":[]},\"eyelids\":{}}";
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.DescriptorEntry, json);

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Descriptor.lipSync.mode, Is.EqualTo(LipSyncData.ModeNone));
        }

        [Test]
        public void Extract_WithVisemePoses_ParsesPerVisemeBlendShapes()
        {
            // FX のアニメーションから読んだ口の形（aa だけ顔と歯の 2 メッシュを動かす）
            var poses = new VisemePose[AvatarDescriptorData.VisemeCount];
            for (int i = 0; i < poses.Length; i++)
            {
                poses[i] = new VisemePose();
            }

            poses[AvatarDescriptorData.VisemeAa] = new VisemePose
            {
                values = new[]
                {
                    new BlendShapeValue { path = "Body", blendShape = "mouth_a", weight = 100f },
                    new BlendShapeValue { path = "Teeth", blendShape = "open", weight = 40f },
                },
            };
            var descriptor = new AvatarDescriptorData
            {
                lipSync = new LipSyncData { mode = LipSyncData.ModeVisemeAnimation, visemePoses = poses },
            };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.DescriptorEntry, JsonUtility.ToJson(descriptor));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            LipSyncData lipSync = package.Descriptor.lipSync;
            Assert.That(lipSync.mode, Is.EqualTo(LipSyncData.ModeVisemeAnimation));
            Assert.That(lipSync.visemePoses, Has.Length.EqualTo(AvatarDescriptorData.VisemeCount));
            BlendShapeValue[] aa = lipSync.visemePoses[AvatarDescriptorData.VisemeAa].values;
            Assert.That(aa, Has.Length.EqualTo(2));
            Assert.That(aa[1].path, Is.EqualTo("Teeth"));
            Assert.That(aa[1].weight, Is.EqualTo(40f));
        }

        [Test]
        public void Extract_VisemeAnimationWithoutPoses_IgnoredAsEmpty()
        {
            // visemeAnimation 方式なのに口の形が 15 件無いものは検証で弾かれること
            const string json = "{\"lipSync\":{\"mode\":\"visemeAnimation\",\"visemes\":[],\"visemePoses\":[]},\"eyelids\":{}}";
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.DescriptorEntry, json);

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Descriptor.lipSync.mode, Is.EqualTo(LipSyncData.ModeNone));
        }

        [Test]
        public void Extract_WithPhysBones_ParsesChainsAndColliders()
        {
            // コライダー 1 つを参照する PhysBone を同梱
            var physBones = new PhysBoneSet
            {
                bones = new[] { new PhysBoneData { rootPath = "Hips/Hair", pull = 0.3f, colliders = new[] { 0 } } },
                colliders = new[]
                {
                    new PhysBoneColliderData { path = "Hips/Head", shape = PhysBoneColliderData.ShapeCapsule, radius = 0.1f, height = 0.3f },
                },
            };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.PhysBonesEntry, JsonUtility.ToJson(physBones));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            // 内容がそのまま読めること
            Assert.That(package.PhysBones.bones, Has.Length.EqualTo(1));
            Assert.That(package.PhysBones.bones[0].pull, Is.EqualTo(0.3f));
            Assert.That(package.PhysBones.colliders[0].shape, Is.EqualTo(PhysBoneColliderData.ShapeCapsule));
        }

        [Test]
        public void Extract_PhysBoneWithUnknownCollider_IgnoredAsEmpty()
        {
            // 存在しないコライダー番号を参照する PhysBone は検証で弾かれること
            var physBones = new PhysBoneSet { bones = new[] { new PhysBoneData { colliders = new[] { 5 } } } };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.PhysBonesEntry, JsonUtility.ToJson(physBones));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.PhysBones.bones, Is.Empty);
        }

        [Test]
        public void Extract_WithConstraints_ParsesConstraintsAndSources()
        {
            // ソース 1 つの Parent Constraint を同梱
            var constraints = new ConstraintSet
            {
                constraints = new[]
                {
                    new ConstraintData
                    {
                        type = ConstraintData.TypeParent,
                        targetPath = "Item",
                        sources = new[] { new ConstraintSourceData { path = "Hips/Hand", positionOffset = Vector3.up } },
                    },
                },
            };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ConstraintsEntry, JsonUtility.ToJson(constraints));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Constraints.constraints, Has.Length.EqualTo(1));
            Assert.That(package.Constraints.constraints[0].type, Is.EqualTo(ConstraintData.TypeParent));
            Assert.That(package.Constraints.constraints[0].sources[0].positionOffset, Is.EqualTo(Vector3.up));
        }

        [Test]
        public void Extract_ConstraintWithUnknownType_IgnoredAsEmpty()
        {
            // 未知の種類を含む constraints.json は全体が空扱いになること
            var constraints = new ConstraintSet { constraints = new[] { new ConstraintData { type = "twist" } } };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ConstraintsEntry, JsonUtility.ToJson(constraints));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Constraints.constraints, Is.Empty);
        }

        [Test]
        public void Extract_WithBlendShapeSync_ParsesBindings()
        {
            // 顔の口の BlendShape をチェーンへ同期する設定を同梱
            var sync = new BlendShapeSyncSet
            {
                bindings = new[]
                {
                    new BlendShapeSyncBinding
                    {
                        sourcePath = "Body", sourceBlendShape = "vrc.v_aa", targetPath = "Chain", targetBlendShape = "mouth",
                    },
                },
            };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.BlendShapeSyncEntry, JsonUtility.ToJson(sync));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.BlendShapeSync.bindings, Has.Length.EqualTo(1));
            Assert.That(package.BlendShapeSync.bindings[0].targetBlendShape, Is.EqualTo("mouth"));
        }

        [Test]
        public void Extract_BlendShapeSyncWithoutName_IgnoredAsEmpty()
        {
            // BlendShape 名の無い同期を含む blendshape_sync.json は全体が空扱いになること
            var sync = new BlendShapeSyncSet { bindings = new[] { new BlendShapeSyncBinding { sourcePath = "Body" } } };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.BlendShapeSyncEntry, JsonUtility.ToJson(sync));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.BlendShapeSync.bindings, Is.Empty);
        }

        [Test]
        public void Extract_WithParticleGroups_ParsesPaths()
        {
            // FX で切り替えるパーティクルのまとまりを同梱
            var groups = new ParticleGroupSet { paths = new[] { "Effects/Aura" } };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ParticleGroupsEntry, JsonUtility.ToJson(groups));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.ParticleGroups.paths, Is.EqualTo(new[] { "Effects/Aura" }));
        }

        [Test]
        public void Extract_ParticleGroupsWithEmptyPath_IgnoredAsEmpty()
        {
            // ルート自身（空のパス）を含む particle_groups.json は全体が空扱いになること
            var groups = new ParticleGroupSet { paths = new[] { string.Empty } };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.ParticleGroupsEntry, JsonUtility.ToJson(groups));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.ParticleGroups.paths, Is.Empty);
        }

        [Test]
        public void Extract_WithFxToggles_ParsesPaths()
        {
            // FX で切り替えるオブジェクトを同梱し、無いパッケージでは空になること
            var toggles = new FxToggleSet { paths = new[] { "Outfit/Jacket", "Hat" } };
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle,
                AvatarPackageLayout.FxTogglesEntry, JsonUtility.ToJson(toggles));

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.FxToggles.paths, Is.EqualTo(new[] { "Outfit/Jacket", "Hat" }));
        }

        [Test]
        public void Extract_WithoutExpressions_ReturnsEmpty()
        {
            string path = WritePackage(CreateManifest(DummyBundle), DummyBundle);

            AvatarPackage package = AvatarPackageReader.Extract(path, _cacheRoot);

            Assert.That(package.Expressions.presets, Is.Empty);
        }

        [TestCase("manifest.json", true)]
        [TestCase("avatar.bundle", true)]
        [TestCase("metadata/", true)]
        [TestCase("metadata/physbones.json", true)]
        [TestCase("metadata/constraints.json", true)]
        [TestCase("metadata/perfect_sync.json", true)]
        [TestCase("metadata/perfect_sync_shapes.bin", true)]
        [TestCase("metadata/other.bin", false)]
        [TestCase("metadata/sub/x.json", false)]
        [TestCase("metadata/x.txt", false)]
        [TestCase("../manifest.json", false)]
        [TestCase("C:/evil.json", false)]
        [TestCase("metadata\\x.json", false)]
        [TestCase("other.dll", false)]
        public void IsAllowedEntryName_Cases(string name, bool expected)
        {
            Assert.That(AvatarPackageReader.IsAllowedEntryName(name), Is.EqualTo(expected));
        }

        private static AvatarManifest CreateManifest(byte[] bundle)
        {
            // bundle の内容から正しいハッシュとサイズを設定
            return TestPackageFiles.CreateManifest(bundle);
        }

        private string WritePackage(
            AvatarManifest manifest, byte[] bundle, string extraEntry = null, string extraContent = "\0")
        {
            // 追加エントリ（不正名や metadata の検証用）は文字列を UTF-8 で入れる
            string path = Path.Combine(_directory, "test" + AvatarPackageLayout.Extension);
            TestPackageFiles.Write(path, manifest, bundle, extraEntry, System.Text.Encoding.UTF8.GetBytes(extraContent));
            return path;
        }
    }
}
