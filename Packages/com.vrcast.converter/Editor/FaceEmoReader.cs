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
        public static List<ExpressionExtractor.NamedClip> Read(GameObject avatar)
        {
            var clips = new List<ExpressionExtractor.NamedClip>();
            // FaceEmo は VRCAvatarDescriptor を対象として記録している
            Component descriptor = avatar != null ? VrcDescriptorReader.FindDescriptor(avatar) : null;
            if (descriptor == null)
            {
                return clips;
            }

            // シーン上（非アクティブ含む）の FaceEmo のうち、このアバターを対象にしているものを読む
            foreach (MonoBehaviour launcher in Object.FindObjectsOfType<MonoBehaviour>(true))
            {
                if (!IsType(launcher, LauncherTypeName) || !Targets(launcher, descriptor))
                {
                    continue;
                }

                // 表情メニューは起動用コンポーネントと同じオブジェクトにある
                foreach (MonoBehaviour component in launcher.GetComponents<MonoBehaviour>())
                {
                    if (IsType(component, RepositoryTypeName))
                    {
                        Object menu = GetReference(component, "SerializableMenu");
                        ReadList(GetReference(menu, "Registered"), clips, 0);
                    }
                }
            }

            return clips;
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
            }

            return false;
        }

        private static void ReadList(Object list, List<ExpressionExtractor.NamedClip> clips, int depth)
        {
            // 未設定・入れ子が深すぎるものは読まない
            if (list == null || depth > MaxGroupDepth)
            {
                return;
            }

            using (var serialized = new SerializedObject(list))
            {
                foreach (Object mode in GetReferences(serialized, "Modes"))
                {
                    ReadMode(mode, clips);
                }

                // グループ（サブメニュー）の中も読む
                foreach (Object group in GetReferences(serialized, "Groups"))
                {
                    ReadList(group, clips, depth + 1);
                }
            }
        }

        private static void ReadMode(Object mode, List<ExpressionExtractor.NamedClip> clips)
        {
            if (mode == null)
            {
                return;
            }

            using (var serialized = new SerializedObject(mode))
            {
                // モード本体の表情（「アニメーション名を表示名に使う」ならクリップ名）
                AnimationClip clip = LoadClip(serialized.FindProperty("Animation")?.objectReferenceValue);
                if (clip != null)
                {
                    bool useClipName = serialized.FindProperty("UseAnimationNameAsDisplayName")?.boolValue ?? false;
                    string displayName = serialized.FindProperty("DisplayName")?.stringValue;
                    clips.Add(new ExpressionExtractor.NamedClip
                    {
                        Clip = clip,
                        Name = useClipName ? clip.name : displayName,
                    });
                }

                // ハンドジェスチャーの分岐ごとの表情（トリガーで切り替わる表情も含む）
                foreach (Object branch in GetReferences(serialized, "Branches"))
                {
                    ReadBranch(branch, clips);
                }
            }
        }

        private static void ReadBranch(Object branch, List<ExpressionExtractor.NamedClip> clips)
        {
            if (branch == null)
            {
                return;
            }

            using (var serialized = new SerializedObject(branch))
            {
                foreach (string field in BranchAnimationFields)
                {
                    AnimationClip clip = LoadClip(serialized.FindProperty(field)?.objectReferenceValue);
                    if (clip != null)
                    {
                        clips.Add(new ExpressionExtractor.NamedClip { Clip = clip, Name = clip.name });
                    }
                }
            }
        }

        private static AnimationClip LoadClip(Object animation)
        {
            // FaceEmo はクリップをアセットの GUID で記録している
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

                // 削除済みなら null
                return AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(guid));
            }
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
