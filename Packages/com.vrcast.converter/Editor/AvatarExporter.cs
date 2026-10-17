using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCast.AvatarFormat;
using CompressionLevel = System.IO.Compression.CompressionLevel;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// アバター GameObject を .vrcaster へ書き出す。
    /// 元オブジェクトは変更せず、複製を除去処理して一時 Prefab → AssetBundle → ZIP の順に生成する。
    /// </summary>
    public static class AvatarExporter
    {
        // 一時 Prefab を置くフォルダ（PrefabAssetPath の親）
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__VRCastExport";

        // Runtime は Windows スタンドアロンのみ対象
        private const BuildTarget BundleTarget = BuildTarget.StandaloneWindows64;

        public struct Report
        {
            public string OutputPath;
            public AvatarManifest Manifest;
            public bool IsHumanoid;
            public int BakedFxClips;
            public bool KeptSceneBlendShapes;
            public int ExpressionCount;
            public int ExtraExpressionClips;
            public int ExtraExpressionCount;
            public int ExtraExpressionIncluded;
            public int FaceEmoClips;
            public int FaceEmoExpressionCount;
            public int FaceEmoLaunchers;
            public int FaceEmoMenuClips;
            public int FaceEmoOtherLaunchers;
            public int FaceEmoSkipped;
            public int HiddenExpressionCount;
            public int ExpressionPathsFixed;
            public int UnresolvedExpressions;
            public List<string> UnresolvedDetails;
            public string LipSyncMode;
            public bool HasBlink;
            public bool HasWink;
            public int PhysBoneCount;
            public int ConstraintCount;
            public int BlendShapeSyncCount;
            public int ParticleSystemCount;
            public int PropAnimators;
            public int DroppedPropAnimators;
            public int FxMotionClips;
            public AvatarPreprocessor.Mode Preprocess;
            public int OptimizerBlendShapeSettingsDisabled;
            public int ModularAvatarFallbackFixes;
            public int OutfitProblems;
            public int OutfitPosesFixed;
            public bool IncludedInactiveObjects;
            public int InactiveObjects;
            public int RemovedInactiveObjects;
            public int FxToggleCount;
            public int MissingBoneRenderers;
            public int ReadableMeshes;
            public int ShaderVariants;
            public int ShaderVariantShaders;
            public ComponentStripper.Result Strip;
        }

        /// <summary>
        /// 書き出し前の検証。問題があればエラーメッセージ、無ければ null を返す。
        /// </summary>
        public static string Validate(GameObject source)
        {
            // 対象未指定
            if (source == null)
            {
                return ExporterLoc.T(
                    "Avatar GameObject is not set.",
                    "アバターの GameObject が指定されていません。",
                    "아바타 GameObject가 지정되지 않았습니다.",
                    "未指定虚拟形象的 GameObject。",
                    "未指定虛擬形象的 GameObject。");
            }

            // ルートに Animator が無いと Humanoid として扱えない
            var animator = source.GetComponent<Animator>();
            if (animator == null || animator.avatar == null)
            {
                return ExporterLoc.T(
                    "Root object must have an Animator with an Avatar.",
                    "ルートのオブジェクトに、Avatar が設定された Animator が必要です。",
                    "루트 오브젝트에 Avatar가 설정된 Animator가 필요합니다.",
                    "根对象需要设置了 Avatar 的 Animator。",
                    "根物件需要設定了 Avatar 的 Animator。");
            }

            // 一時フォルダが既に存在する場合はユーザー資産を消さないよう中断
            string tempFolder = $"{TempFolderParent}/{TempFolderName}";
            if (AssetDatabase.IsValidFolder(tempFolder))
            {
                return ExporterLoc.T(
                    $"Temporary folder '{tempFolder}' already exists. Remove it and retry.",
                    $"一時フォルダ「{tempFolder}」が既にあります。削除してからやり直してください。",
                    $"임시 폴더 '{tempFolder}'가 이미 있습니다. 삭제한 후 다시 시도하세요.",
                    $"临时文件夹“{tempFolder}”已存在。请删除后重试。",
                    $"暫存資料夾「{tempFolder}」已存在。請刪除後重試。");
            }

            return null;
        }

        /// <summary>
        /// keepSceneBlendShapes が true なら、FX の初期状態ではなくシーン上の BlendShape の値を書き出す。
        /// extraExpressionClips は FX に無い表情として追加するクリップ（BlendShape だけを動かすものを取り込む）。
        /// シーンに FaceEmo の設定があれば、その表情を FaceEmo の名前で優先して取り込む。
        /// includeInactiveObjects が true なら、エディタで非アクティブにしたものも OFF のまま書き出す（false なら消す）。
        /// log を渡すと、書き出し中の出来事（ほかのツールの警告・エラーを含む）をそこへ記録する。
        /// fixOutfitPoses が true なら、アバターと姿勢が合わない衣装（T / A ポーズの違い）のボーンを複製の上でアバターの向きへ直す。
        /// </summary>
        public static Report Export(GameObject source, string outputPath, bool keepSceneBlendShapes = true,
            IReadOnlyList<AnimationClip> extraExpressionClips = null, bool includeInactiveObjects = true,
            ExportLog log = null, bool fixOutfitPoses = false)
        {
            // 工程を続けて実行する（ウィンドウからは Start で 1 工程ずつ進める）
            using (ExportJob job = Start(source, outputPath, keepSceneBlendShapes, extraExpressionClips,
                       includeInactiveObjects, log, fixOutfitPoses))
            {
                while (job.MoveNext())
                {
                }

                job.ThrowIfFailed();
                return job.Report;
            }
        }

        /// <summary>
        /// 書き出しを始める。返した ExportJob の MoveNext を呼ぶたびに 1 工程ずつ進む（引数は Export と同じ）。
        /// 途中で Dispose すると中断し、複製と一時ファイルを片付ける。
        /// </summary>
        public static ExportJob Start(GameObject source, string outputPath, bool keepSceneBlendShapes = true,
            IReadOnlyList<AnimationClip> extraExpressionClips = null, bool includeInactiveObjects = true,
            ExportLog log = null, bool fixOutfitPoses = false)
        {
            var result = new ExportJob.ResultBox();
            return new ExportJob(
                ExportSteps(source, outputPath, keepSceneBlendShapes, extraExpressionClips, includeInactiveObjects,
                    fixOutfitPoses, result),
                log ?? new ExportLog(), result);
        }

        private static IEnumerator<ExportJob.Step> ExportSteps(GameObject source, string outputPath, bool keepSceneBlendShapes,
            IReadOnlyList<AnimationClip> extraExpressionClips, bool includeInactiveObjects, bool fixOutfitPoses,
            ExportJob.ResultBox result)
        {
            // 事前検証に失敗したら例外で中断
            string error = Validate(source);
            if (error != null)
            {
                throw new InvalidOperationException(error);
            }

            // 出力先の拡張子を統一
            if (!outputPath.EndsWith(AvatarPackageLayout.Extension, StringComparison.OrdinalIgnoreCase))
            {
                outputPath += AvatarPackageLayout.Extension;
            }

            var report = new Report
            {
                OutputPath = outputPath,
                KeptSceneBlendShapes = keepSceneBlendShapes,
                IncludedInactiveObjects = includeInactiveObjects,
            };
            GameObject clone = null;
            string bundleDir = FileUtil.GetUniqueTempPathInProject();
            string tempFolder = $"{TempFolderParent}/{TempFolderName}";

            // NDMF が書き出し中に生成したアセットだけを後で消すため、既存分を控える
            HashSet<string> generatedAssetsBefore = NdmfProcessor.ListGeneratedAssets();

            // FaceEmo は元のアバターを対象として記録しているため、複製の前に元から読む
            var faceEmoTrace = new List<string>();
            FaceEmoReader.Result faceEmo = FaceEmoReader.Read(source, faceEmoTrace);
            List<ExpressionExtractor.NamedClip> faceEmoClips = faceEmo.Clips;

            try
            {
                yield return ExportJob.Step.Preparing;

                // 元オブジェクトを壊さないよう複製し、原点に配置
                clone = Object.Instantiate(source);
                clone.name = source.name;
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

                // エディタで非アクティブにしたもの（FX の焼き込みで表示状態が変わる前、改変の適用前に扱う）は、
                // 書き出す指定なら控えて後で OFF に戻し、書き出さない指定ならここで消す（パーティクルは OFF のまま残す）
                List<GameObject> sceneInactive = includeInactiveObjects
                    ? ComponentStripper.CollectInactiveObjects(clone)
                    : new List<GameObject>();
                if (!includeInactiveObjects)
                {
                    report.RemovedInactiveObjects = ComponentStripper.RemoveInactiveObjects(clone);
                }

                // NDMF が MA コンポーネントを消す前に、衣装・小物の統合先を控える
                ModularAvatarFallback.Plan maPlan = ModularAvatarFallback.Capture(clone);
                BlendShapeSyncExtractor.Plan syncPlan = BlendShapeSyncExtractor.Capture(clone);

                // 表情の BlendShape が最適化で消えないよう、最適化ツールの BlendShape の削除を複製でだけ止める
                report.OptimizerBlendShapeSettingsDisabled = OptimizerGuard.KeepBlendShapes(clone);

                yield return ExportJob.Step.ApplyingTools;

                // VRCFury・Modular Avatar 等の改変を VRChat のアップロード時と同じく複製へ適用（どれも無ければ何もしない）
                report.Preprocess = AvatarPreprocessor.Process(clone);
                LogPreprocess(report.Preprocess);

                // NDMF で統合されなかった衣装・小物のボーンをアバターのボーンへ付け替え、追従させる
                ModularAvatarFallback.Result maFallback = ModularAvatarFallback.Apply(maPlan);
                report.ModularAvatarFallbackFixes = maFallback.FixedCount;

                yield return ExportJob.Step.ReadingAvatar;

                // 衣装の姿勢がアバターと合っていない（指定時は直す）・体に追従しないメッシュがあれば知らせる
                OutfitPoseChecker.Result outfitCheck = OutfitPoseChecker.Check(clone, fixOutfitPoses);
                report.OutfitProblems = outfitCheck.Problems;
                report.OutfitPosesFixed = outfitCheck.Fixed;

                // 以降は改変適用後の複製から読む（マージ後の FX・移動後のボーンを反映するため）
                Component descriptor = VrcDescriptorReader.FindDescriptor(clone);
                AnimatorController fx = descriptor != null ? VrcDescriptorReader.GetFxController(descriptor) : null;

                // リップシンク・まぶた設定（パスは複製ルート基準）
                AvatarDescriptorData descriptorData = descriptor != null
                    ? VrcDescriptorReader.GetDescriptorData(descriptor, clone.transform)
                    : new AvatarDescriptorData();
                report.LipSyncMode = descriptorData.lipSync.mode;
                report.HasBlink = descriptorData.eyelids.blinkBlendShapes.Length > 0;
                report.HasWink = !string.IsNullOrEmpty(descriptorData.eyelids.winkLeftBlendShape);

                // 揺れもの（FX の焼き込みで表示状態が変わる前に読む）
                PhysBoneSet physBones = PhysBoneExtractor.Extract(clone);
                report.PhysBoneCount = physBones.bones.Length;

                // Constraint（FX の焼き込みで表示状態が変わる前に読む）
                ConstraintSet constraints = ConstraintExtractor.Extract(clone);
                report.ConstraintCount = constraints.constraints.Length;

                // FX で表示 ON/OFF されるオブジェクト（パーティクルをまとめて切り替える単位にする）
                HashSet<Transform> fxToggled = fx != null
                    ? FxDefaultStateBaker.CollectToggledObjects(clone, fx, maFallback.MovedObjects)
                    : new HashSet<Transform>();

                // VRChat 上の初期状態に近づけるため、除去前に FX の既定状態を焼き込む
                // （パラメーターの既定値は小物の動きの取り込みでも使うため、Avatar Descriptor を外す前に読んでおく）
                Dictionary<string, float> parameterDefaults = descriptor != null
                    ? VrcDescriptorReader.GetExpressionParameterDefaults(descriptor)
                    : new Dictionary<string, float>();

                // Descriptor の設定で口を動かせないアバターは、FX の Viseme で動かすアニメーションから口の形を読む
                if (descriptorData.lipSync.mode == LipSyncData.ModeNone && fx != null)
                {
                    descriptorData.lipSync = FxLipSyncReader.Read(clone, fx, parameterDefaults, maFallback.MovedObjects);
                    report.LipSyncMode = descriptorData.lipSync.mode;
                }

                LogFace(report);

                if (fx != null)
                {
                    report.BakedFxClips = FxDefaultStateBaker.Bake(
                        clone, fx, parameterDefaults, maFallback.MovedObjects, keepSceneBlendShapes);
                }

                // エディタで非アクティブにしたものは、FX の初期状態で表示されるものも OFF にそろえる（アプリで ON にできる）
                report.InactiveObjects = ComponentStripper.Deactivate(sceneInactive);

                yield return ExportJob.Step.ReadingExpressions;

                // 改変適用後の Expression Menu の FaceEmo の下から、VRChat と同じ道筋で表情を読む（名前・パスとも VRChat と同じ）
                List<ExpressionExtractor.NamedClip> menuClips =
                    ExpressionMenuReader.Read(descriptor, fx, faceEmoTrace);
                report.FaceEmoMenuClips = menuClips.Count;
                faceEmoClips = MergeNamedClips(menuClips, faceEmoClips);

                // FaceEmo・FX・追加指定のクリップから表情プリセットを抽出
                ExpressionExtractor.Result extracted =
                    ExpressionExtractor.Extract(fx, extraExpressionClips, faceEmoClips);
                ExpressionSet expressions = extracted.Set;
                report.ExpressionCount = expressions.presets.Length;
                report.ExtraExpressionClips = extraExpressionClips?.Count ?? 0;
                report.ExtraExpressionCount = extracted.ExtraAdded;
                report.ExtraExpressionIncluded = extracted.ExtraIncluded;
                report.FaceEmoClips = faceEmoClips.Count;
                report.FaceEmoExpressionCount = extracted.NamedAdded;
                report.FaceEmoLaunchers = faceEmo.Launchers;
                report.FaceEmoOtherLaunchers = faceEmo.OtherLaunchers;
                report.FaceEmoSkipped = extracted.NamedSkipped.Count;
                report.HiddenExpressionCount = extracted.HiddenAdded;

                // FaceEmo の表情が足りないときに原因を追えるよう、読んだメニューと取り込まなかった表情をログに出す
                if (faceEmoTrace.Count > 0)
                {
                    LogFaceEmo(faceEmoTrace, extracted.NamedSkipped);
                }

                yield return ExportJob.Step.Cleaning;

                // 許可リスト外のコンポーネント等を除去
                report.Strip = ComponentStripper.Strip(clone);
                report.IsHumanoid = clone.GetComponent<Animator>().isHuman;

                // 書き出すパーティクルの数（アプリで ON/OFF できる）と、まとめて切り替える単位
                report.ParticleSystemCount = clone.GetComponentsInChildren<ParticleSystem>(true).Length;
                ParticleGroupSet particleGroups = new ParticleGroupSet
                {
                    paths = BuildToggledPaths(clone.transform, fxToggled, true),
                };

                // FX で切り替えるオブジェクト（アプリのオブジェクトの一覧で目立たせ、絞り込めるようにする）
                FxToggleSet fxToggles = new FxToggleSet { paths = BuildToggledPaths(clone.transform, fxToggled, false) };
                report.FxToggleCount = fxToggles.paths.Length;

                // ボーンを失ったメッシュ（頂点が原点へ伸びる）を警告
                report.MissingBoneRenderers = ComponentStripper.WarnMissingBones(clone);

                // 改変前から読んだ表情（FaceEmo 等）のパスを、改変・除去の後のメッシュに合わせる
                ExpressionPathFixer.Result pathFix = ExpressionPathFixer.Fix(expressions, clone.transform,
                    source.transform, descriptorData.lipSync.meshPath, descriptorData.eyelids.meshPath);
                report.ExpressionPathsFixed = pathFix.FixedValues;
                report.UnresolvedExpressions = pathFix.Unresolved.Count;
                report.UnresolvedDetails = pathFix.Unresolved;
                if (pathFix.Unresolved.Count > 0)
                {
                    ExportLog.Warn(
                        $"{pathFix.Unresolved.Count} expression(s) will not appear in the app because their meshes were not found.",
                        $"{pathFix.Unresolved.Count} 個の表情は動かすメッシュが見つからないため、アプリの表情一覧に出ません。",
                        string.Join("\n", pathFix.Unresolved));
                }

                // BlendShape の同期（改変・除去の後のパスで。まばたき・口パク等の直接の書き込みにも同期させるため）
                BlendShapeSyncSet blendShapeSync = BlendShapeSyncExtractor.Build(syncPlan);
                report.BlendShapeSyncCount = blendShapeSync.bindings.Length;

                yield return ExportJob.Step.SavingPrefab;

                // AssetBundle 化のため一時 Prefab として保存
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);

                // 小物のアニメーション（小物の Animator と、FX の初期状態で再生され続ける小物の動き）を VRChat と同じく自動で再生させる
                var props = new PropAnimationExporter.Result();
                PropAnimationExporter.PrepareAnimators(clone, tempFolder, ref props);
                PropAnimationExporter.AddFxMotions(clone, fx, parameterDefaults, maFallback.MovedObjects, tempFolder,
                    ref props);
                report.PropAnimators = props.Animators;
                report.DroppedPropAnimators = props.DroppedAnimators;
                report.FxMotionClips = props.FxMotions;

                // アプリのパーフェクトシンク化で頂点を読めるよう、BlendShape を持つメッシュを読み書きできる複製にする
                report.ReadableMeshes = ReadableMeshCopier.Apply(clone, tempFolder);
                PrefabUtility.SaveAsPrefabAsset(clone, AvatarPackageLayout.PrefabAssetPath, out bool saved);
                if (!saved)
                {
                    throw new InvalidOperationException("Failed to save temporary prefab.");
                }

                // シェーダーバリアントを集め、bundle ビルド時に削られないようにする
                // （Editor プレビューはプロジェクト側のシェーダーに差し替わることがあり、アプリだけ砂嵐／マゼンタになるのを防ぐ）
                ShaderVariantCollector.Result variants = ShaderVariantCollector.Create(clone, tempFolder);
                report.ShaderVariants = variants.Variants;
                report.ShaderVariantShaders = variants.Shaders;
                if (variants.Variants > 0)
                {
                    ExportLog.Info(
                        $"Included {variants.Variants} shader variant(s) from {variants.Shaders} shader(s) so the app keeps the look.",
                        $"シェーダー {variants.Shaders} 種・バリアント {variants.Variants} 件を同梱し、アプリでも見た目が保たれるようにしました。");
                }

                yield return ExportJob.Step.BuildingBundle;

                // Prefab・シェーダーバリアントと依存アセット（Mesh / Material / Shader / Texture）を bundle 化
                string bundlePath = BuildBundle(bundleDir, variants.AssetPath);

                yield return ExportJob.Step.Writing;

                // manifest を生成して検証
                report.Manifest = new AvatarManifest
                {
                    name = source.name,
                    unityVersion = Application.unityVersion,
                    bundleSha256 = HashUtility.ComputeSha256Hex(bundlePath),
                    bundleSize = new FileInfo(bundlePath).Length,
                    createdAt = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                };
                string manifestError = report.Manifest.Validate();
                if (manifestError != null)
                {
                    throw new InvalidOperationException(manifestError);
                }

                // 中身のある metadata だけを Runtime と同じ規則で検証して同梱
                var metadata = new Dictionary<string, IMetadata>();
                if (expressions.presets.Length > 0)
                {
                    metadata[AvatarPackageLayout.ExpressionsEntry] = expressions;
                }

                if (report.LipSyncMode != LipSyncData.ModeNone || report.HasBlink)
                {
                    metadata[AvatarPackageLayout.DescriptorEntry] = descriptorData;
                }

                if (physBones.bones.Length > 0)
                {
                    metadata[AvatarPackageLayout.PhysBonesEntry] = physBones;
                }

                if (constraints.constraints.Length > 0)
                {
                    metadata[AvatarPackageLayout.ConstraintsEntry] = constraints;
                }

                if (blendShapeSync.bindings.Length > 0)
                {
                    metadata[AvatarPackageLayout.BlendShapeSyncEntry] = blendShapeSync;
                }

                if (particleGroups.paths.Length > 0)
                {
                    metadata[AvatarPackageLayout.ParticleGroupsEntry] = particleGroups;
                }

                if (fxToggles.paths.Length > 0)
                {
                    metadata[AvatarPackageLayout.FxTogglesEntry] = fxToggles;
                }

                foreach (KeyValuePair<string, IMetadata> entry in metadata)
                {
                    string metadataError = entry.Value.Validate();
                    if (metadataError != null)
                    {
                        throw new InvalidOperationException($"Invalid {entry.Key}: {metadataError}");
                    }
                }

                // ZIP にまとめて出力
                WritePackage(outputPath, report.Manifest, bundlePath, metadata);
                ExportLog.Info(
                    $"Exported ({report.Manifest.bundleSize / (1024f * 1024f):F1} MB).",
                    $"書き出しました（{report.Manifest.bundleSize / (1024f * 1024f):F1} MB）。",
                    outputPath);
                result.Report = report;
            }
            finally
            {
                // 複製と一時ファイルは成否に関わらず必ず後始末
                if (clone != null)
                {
                    Object.DestroyImmediate(clone);
                }

                AssetDatabase.DeleteAsset(tempFolder);
                FileUtil.DeleteFileOrDirectory(bundleDir);

                // bundle 化が済んだので NDMF の生成アセットも片付ける
                NdmfProcessor.DeleteGeneratedAssets(generatedAssetsBefore);
            }
        }

        private static List<ExpressionExtractor.NamedClip> MergeNamedClips(
            List<ExpressionExtractor.NamedClip> menuClips, List<ExpressionExtractor.NamedClip> faceEmoClips)
        {
            // メニューから読めた表情を先に置き、FaceEmo の設定から読んだ同じ名前の表情は除く（メニューの方が改変後のクリップのため）
            var names = new HashSet<string>();
            foreach (ExpressionExtractor.NamedClip clip in menuClips)
            {
                names.Add(clip.Name?.Trim() ?? string.Empty);
            }

            var merged = new List<ExpressionExtractor.NamedClip>(menuClips);
            foreach (ExpressionExtractor.NamedClip clip in faceEmoClips)
            {
                if (!names.Contains(clip.Name?.Trim() ?? string.Empty))
                {
                    merged.Add(clip);
                }
            }

            return merged;
        }

        private static string[] BuildToggledPaths(Transform root, HashSet<Transform> fxToggled, bool particlesOnly)
        {
            // 除去の後も残っている FX の切り替え先（particlesOnly ならパーティクルを含むものだけ）を、パスの順に並べて書く
            var paths = new List<string>();
            foreach (Transform toggled in fxToggled)
            {
                // 消えたもの・ルート自身・（指定時は）パーティクルを含まないものは対象にしない
                if (toggled == null || toggled == root || !toggled.IsChildOf(root)
                    || (particlesOnly && toggled.GetComponentInChildren<ParticleSystem>(true) == null))
                {
                    continue;
                }

                // 長すぎるパスは検証で書き出し全体が失敗しないよう除く（その分は Runtime の推定に任せる）
                string path = ReflectionUtility.PathOf(toggled, root);
                if (ExpressionSet.IsValidString(path, false))
                {
                    paths.Add(path);
                }
            }

            // 書き出しのたびに内容が変わらないよう並びを固定し、上限を超える分は捨てる
            paths.Sort(StringComparer.Ordinal);
            if (paths.Count > PathListSet.MaxPaths)
            {
                paths.RemoveRange(PathListSet.MaxPaths, paths.Count - PathListSet.MaxPaths);
            }

            return paths.ToArray();
        }

        private static void LogPreprocess(AvatarPreprocessor.Mode mode)
        {
            switch (mode)
            {
                case AvatarPreprocessor.Mode.VrcSdk:
                    ExportLog.Info("Applied avatar tools with the VRChat SDK upload processing (VRCFury / NDMF etc.).",
                        "VRChat SDK のアップロード時の処理で改変ツール（VRCFury・NDMF など）を適用しました。");
                    break;
                case AvatarPreprocessor.Mode.NdmfOnly:
                    ExportLog.Info("Applied NDMF tools only (Modular Avatar etc.). VRCFury is not applied without the VRChat SDK.",
                        "NDMF のツール（Modular Avatar など）だけを適用しました。VRChat SDK が無いと VRCFury は適用されません。");
                    break;
                default:
                    ExportLog.Info("No avatar tools (VRCFury / NDMF) were applied.",
                        "改変ツール（VRCFury・NDMF）は適用していません。");
                    break;
            }
        }

        private static void LogFace(Report report)
        {
            // 口・まばたきに使えるものが見つからなければ、アプリで動かないことを知らせる
            if (report.LipSyncMode == LipSyncData.ModeNone)
            {
                ExportLog.Warn("No lip sync settings that VRCast can use were found (the mouth will not move with the microphone).",
                    "VRCast で使えるリップシンクの設定が見つかりませんでした（マイクで口が動きません）。");
            }
            else
            {
                ExportLog.Info($"Lip sync: {report.LipSyncMode}", $"リップシンク: {report.LipSyncMode}");
            }

            if (!report.HasBlink)
            {
                ExportLog.Warn("No blink blend shape was found (it can be chosen in the app's Face tab).",
                    "まばたきの BlendShape が見つかりませんでした（アプリの顔・表情タブで選べます）。");
            }
        }

        private static void LogFaceEmo(List<string> trace, List<string> skipped)
        {
            // 1 行ごとに分けて Console にまとめて出す（メニューの構成 → 取り込まなかった表情の順）
            var lines = new List<string>(trace);
            foreach (string item in skipped)
            {
                lines.Add("skipped: " + item);
            }

            Debug.Log("[VRCast][Exporter][FaceEmo]\n" + string.Join("\n", lines));
        }

        private static string BuildBundle(string bundleDir, string shaderVariantPath)
        {
            // 出力先を作成
            Directory.CreateDirectory(bundleDir);

            // Prefab と（あれば）ShaderVariantCollection を明示指定して bundle を作る
            // （プロジェクト内の bundle 名設定は無視される。SVC を同じ bundle に入れるとバリアントが残りやすい）
            var assets = new List<string> { AvatarPackageLayout.PrefabAssetPath };
            if (!string.IsNullOrEmpty(shaderVariantPath))
            {
                assets.Add(shaderVariantPath);
            }

            var builds = new[]
            {
                new AssetBundleBuild
                {
                    assetBundleName = AvatarPackageLayout.BundleEntry,
                    assetNames = assets.ToArray(),
                },
            };

            // LZ4 圧縮で部分読込を可能にし、エラーは厳格に扱う
            const BuildAssetBundleOptions options =
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode;
            AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(bundleDir, builds, options, BundleTarget);
            if (manifest == null)
            {
                throw new InvalidOperationException("AssetBundle build failed. See Console for details.");
            }

            // 生成された bundle のパスを返す
            string bundlePath = Path.Combine(bundleDir, AvatarPackageLayout.BundleEntry);
            if (!File.Exists(bundlePath))
            {
                throw new FileNotFoundException("AssetBundle output not found.", bundlePath);
            }

            return bundlePath;
        }

        private static void WritePackage(
            string outputPath, AvatarManifest manifest, string bundlePath, IReadOnlyDictionary<string, IMetadata> metadata)
        {
            // 書き込み途中の破損ファイルを残さないよう一時ファイルへ書いてから置き換える
            string tempPath = outputPath + ".tmp";
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }

            using (FileStream stream = File.Create(tempPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                // bundle は LZ4 圧縮済みで、Runtime 側の Deflate 依存も避けるため無圧縮で格納
                WriteTextEntry(zip, AvatarPackageLayout.ManifestEntry, JsonUtility.ToJson(manifest, true));

                // 任意の metadata（JsonUtility.ToJson はインターフェース経由でも実行時の型でシリアライズする）
                foreach (KeyValuePair<string, IMetadata> entry in metadata)
                {
                    WriteTextEntry(zip, entry.Key, JsonUtility.ToJson(entry.Value, true));
                }

                // bundle 本体をコピー
                ZipArchiveEntry bundleEntry = zip.CreateEntry(AvatarPackageLayout.BundleEntry, CompressionLevel.NoCompression);
                using (Stream entryStream = bundleEntry.Open())
                using (FileStream bundleStream = File.OpenRead(bundlePath))
                {
                    bundleStream.CopyTo(entryStream);
                }
            }

            // 既存ファイルを置き換え
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            File.Move(tempPath, outputPath);
        }

        private static void WriteTextEntry(ZipArchive zip, string entryName, string text)
        {
            // テキストエントリも bundle と同じく無圧縮で格納
            ZipArchiveEntry entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(entry.Open()))
            {
                writer.Write(text);
            }
        }
    }
}
