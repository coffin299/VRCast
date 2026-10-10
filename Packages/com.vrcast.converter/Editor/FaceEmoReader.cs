using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// シーン上の FaceEmo の設定から、アバターの表情（モード名と元のクリップ）を読む。
    /// FaceEmo のアセンブリは参照せず、型名と SerializedObject で読む（FaceEmo が無ければ空）。
    /// </summary>
    public static class FaceEmoReader
    {
        // FaceEmo の起動用コンポーネント（対象アバターの設定を持つ）と、表情メニューを持つコンポーネント
        private const string LauncherTypeName = "Suzuryg.FaceEmo.Components.FaceEmoLauncherComponent";
        private const string RepositoryTypeName = "Suzuryg.FaceEmo.Components.Data.MenuRepositoryComponent";

        // 分岐（ハンドジェスチャー）ごとのアニメーションのフィールド
        private static readonly string[] BranchAnimationFields =
        {
            "BaseAnimation", "LeftHandAnimation", "RightHandAnimation", "BothHandsAnimation",
        };

        // グループの入れ子の上限（壊れたデータでの無限再帰を防ぐ）
        private const int MaxGroupDepth = 16;

        /// <summary>
        /// avatar を対象にした FaceEmo の表情を、メニューの順に返す（見つからなければ空）。
        /// モードは FaceEmo の表示名、ハンドジェスチャーの分岐は FaceEmo のメニューと同じくクリップ名を使う。
        /// </summary>
        /// <summary>
        /// 読んだ表情と、見つけた FaceEmo の設定の数（このアバター用 / 別のアバター用）。
        /// </summary>
        public struct Result
        {
            public List<ExpressionExtractor.NamedClip> Clips;
            public int Launchers;
            public int OtherLaunchers;
        }

        /// <param name="trace">読んだメニューの中身（フォルダ・モード・読めなかったクリップ）を書き足す一覧（不要なら null）</param>
        public static Result Read(GameObject avatar, List<string> trace = null)
        {
            var result = new Result { Clips = new List<ExpressionExtractor.NamedClip>() };
            // FaceEmo は VRCAvatarDescriptor を対象として記録している
            Component descriptor = avatar != null ? VrcDescriptorReader.FindDescriptor(avatar) : null;
            if (descriptor == null)
            {
                return result;
            }

            // シーン上（非アクティブ含む）の FaceEmo のうち、このアバターを対象にしているものを読む
            var others = new List<MonoBehaviour>();
            foreach (MonoBehaviour launcher in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (!IsType(launcher, LauncherTypeName))
                {
                    continue;
                }

                // 別のアバター用の FaceEmo は後で候補にするため控える
                if (!Targets(launcher, descriptor))
                {
                    others.Add(launcher);
                    continue;
                }

                result.Launchers++;
                ReadLauncher(launcher, result.Clips, trace, string.Empty);
            }

            // 対象が一致するものが無ければ、対象の指定が壊れた（別 PC で開いた等）とみなして候補から 1 つ選んで読む
            MonoBehaviour fallback = result.Launchers == 0 ? PickFallback(others, avatar.name) : null;
            foreach (MonoBehaviour other in others)
            {
                if (other == fallback)
                {
                    result.Launchers++;
                    ReadLauncher(other, result.Clips, trace, " (target mismatch, used as fallback)");
                }
                else
                {
                    // 読まなかった FaceEmo は数とログに残す
                    result.OtherLaunchers++;
                    trace?.Add($"launcher: {other.gameObject.name} (targets another avatar, ignored)");
                }
            }

            return result;
        }

        private static void ReadLauncher(MonoBehaviour launcher, List<ExpressionExtractor.NamedClip> clips,
            List<string> trace, string note)
        {
            // 表情メニューは起動用コンポーネントと同じオブジェクトにある
            trace?.Add($"launcher: {launcher.gameObject.name}{note}");
            foreach (MonoBehaviour component in launcher.GetComponents<MonoBehaviour>())
            {
                if (IsType(component, RepositoryTypeName))
                {
                    Object menu = GetReference(component, "SerializableMenu");
                    ReadList(GetReference(menu, "Registered"), string.Empty, clips, trace, 0);
                }
            }
        }

        private static MonoBehaviour PickFallback(List<MonoBehaviour> candidates, string avatarName)
        {
            // シーンに FaceEmo が 1 つだけなら、それがこのアバター用とみなす
            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            // 複数あるなら、控えている対象のパスの末尾がアバター名と同じものが 1 つだけのときに限り選ぶ
            MonoBehaviour match = null;
            foreach (MonoBehaviour candidate in candidates)
            {
                Object setting = GetReference(candidate, "AV3Setting");
                string path = GetString(setting, "TargetAvatarPath");
                string last = path.Substring(path.LastIndexOf('/') + 1);
                if (last != avatarName)
                {
                    continue;
                }

                // 同名が複数あると取り違えるため選ばない
                if (match != null)
                {
                    return null;
                }

                match = candidate;
            }

            return match;
        }

        private static bool IsType(Component component, string fullName)
        {
            // Missing Script は null 扱いになる
            return component != null && component.GetType().FullName == fullName;
        }

        private static bool Targets(Component launcher, Component descriptor)
        {
            // AV3Setting.TargetAvatar（主）か SubTargetAvatars（同じ設定を使う別アバター）に含まれるか
            Object setting = GetReference(launcher, "AV3Setting");
            if (setting == null)
            {
                return false;
            }

            using (var serialized = new SerializedObject(setting))
            {
                if (serialized.FindProperty("TargetAvatar")?.objectReferenceValue == descriptor)
                {
                    return true;
                }

                foreach (Object sub in GetReferences(serialized, "SubTargetAvatars"))
                {
                    if (sub == descriptor)
                    {
                        return true;
                    }
                }

                // 参照が外れている（Prefab の入れ替え・シーンの開き直し等）ときは、FaceEmo が控えている階層のパスで照合する
                string path = FullPath(descriptor.transform);
                if (serialized.FindProperty("TargetAvatarPath")?.stringValue == path)
                {
                    return true;
                }

                SerializedProperty subPaths = serialized.FindProperty("SubTargetAvatarPaths");
                for (int i = 0; subPaths != null && subPaths.isArray && i < subPaths.arraySize; i++)
                {
                    if (subPaths.GetArrayElementAtIndex(i).stringValue == path)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string FullPath(Transform transform)
        {
            // FaceEmo と同じ「/親/子」の形（ルートから自身まで）
            var names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                names.Add(current.name);
            }

            names.Reverse();
            return "/" + string.Join("/", names);
        }

        private static void ReadList(Object list, string path, List<ExpressionExtractor.NamedClip> clips,
            List<string> trace, int depth)
        {
            // 未設定・入れ子が深すぎるものは読まない
            if (list == null || depth > MaxGroupDepth)
            {
                trace?.Add($"{path}: {(list == null ? "missing list" : "too deep")}");
                return;
            }

            using (var serialized = new SerializedObject(list))
            {
                List<Object> modes = GetReferences(serialized, "Modes");
                List<Object> groups = GetReferences(serialized, "Groups");
                trace?.Add($"{Label(path, "(menu)")}: {modes.Count} modes, {groups.Count} groups");

                foreach (Object mode in modes)
                {
                    ReadMode(mode, path, clips, trace);
                }

                // グループ（サブメニュー）の中も読む
                foreach (Object group in groups)
                {
                    ReadList(group, Join(path, GetString(group, "DisplayName")), clips, trace, depth + 1);
                }
            }
        }

        private static void ReadMode(Object mode, string path, List<ExpressionExtractor.NamedClip> clips,
            List<string> trace)
        {
            if (mode == null)
            {
                trace?.Add($"{Label(path, "(menu)")}: missing mode");
                return;
            }

            using (var serialized = new SerializedObject(mode))
            {
                // モード本体の表情（「アニメーション名を表示名に使う」ならクリップ名）
                string displayName = serialized.FindProperty("DisplayName")?.stringValue;
                string modePath = Join(path, displayName);
                AnimationClip clip = LoadClip(serialized.FindProperty("Animation")?.objectReferenceValue, modePath,
                    trace);
                if (clip != null)
                {
                    bool useClipName = serialized.FindProperty("UseAnimationNameAsDisplayName")?.boolValue ?? false;
                    clips.Add(new ExpressionExtractor.NamedClip
                    {
                        Clip = clip,
                        Name = useClipName ? clip.name : displayName,
                        Source = modePath,
                    });
                }

                // ハンドジェスチャーの分岐ごとの表情（トリガーで切り替わる表情も含む）
                List<Object> branches = GetReferences(serialized, "Branches");
                for (int i = 0; i < branches.Count; i++)
                {
                    ReadBranch(branches[i], $"{modePath} / branch {i + 1}", clips, trace);
                }
            }
        }

        private static void ReadBranch(Object branch, string path, List<ExpressionExtractor.NamedClip> clips,
            List<string> trace)
        {
            if (branch == null)
            {
                return;
            }

            using (var serialized = new SerializedObject(branch))
            {
                foreach (string field in BranchAnimationFields)
                {
                    string fieldPath = $"{path} {field}";
                    AnimationClip clip = LoadClip(serialized.FindProperty(field)?.objectReferenceValue, fieldPath, trace);
                    if (clip != null)
                    {
                        clips.Add(new ExpressionExtractor.NamedClip { Clip = clip, Name = clip.name, Source = fieldPath });
                    }
                }
            }
        }

        private static AnimationClip LoadClip(Object animation, string path, List<string> trace)
        {
            // FaceEmo はクリップをアセットの GUID で記録している（未設定は何もしない）
            if (animation == null)
            {
                return null;
            }

            using (var serialized = new SerializedObject(animation))
            {
                string guid = serialized.FindProperty("GUID")?.stringValue;
                if (string.IsNullOrEmpty(guid))
                {
                    return null;
                }

                // 削除済み・クリップでないアセットは読めなかったとして記録する
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
                if (clip == null)
                {
                    trace?.Add($"{path}: clip not found (GUID {guid}, path '{assetPath}')");
                }

                return clip;
            }
        }

        private static string GetString(Object target, string field)
        {
            // 未設定・フィールドが無いなら空
            if (target == null)
            {
                return string.Empty;
            }

            using (var serialized = new SerializedObject(target))
            {
                return serialized.FindProperty(field)?.stringValue ?? string.Empty;
            }
        }

        private static string Join(string path, string name)
        {
            // 「親 / 子」の形にする（名前の無いものは ? で表す）
            string label = string.IsNullOrEmpty(name) ? "?" : name;
            return string.IsNullOrEmpty(path) ? label : $"{path} / {label}";
        }

        private static string Label(string path, string root)
        {
            return string.IsNullOrEmpty(path) ? root : path;
        }

        private static Object GetReference(Object target, string field)
        {
            // 未設定・フィールドが無い（FaceEmo の版違い）なら null
            if (target == null)
            {
                return null;
            }

            using (var serialized = new SerializedObject(target))
            {
                return serialized.FindProperty(field)?.objectReferenceValue;
            }
        }

        private static List<Object> GetReferences(SerializedObject serialized, string field)
        {
            // 参照のリスト（フィールドが無い・リストでなければ空）
            var references = new List<Object>();
            SerializedProperty list = serialized.FindProperty(field);
            if (list == null || !list.isArray)
            {
                return references;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                if (element.propertyType == SerializedPropertyType.ObjectReference)
                {
                    references.Add(element.objectReferenceValue);
                }
            }

            return references;
        }
    }
}
