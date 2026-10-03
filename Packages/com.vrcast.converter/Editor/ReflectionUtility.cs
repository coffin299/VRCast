using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// VRChat SDK 型をアセンブリ参照なしで読むためのリフレクション補助。
    /// </summary>
    internal static class ReflectionUtility
    {
        // public インスタンスフィールドのみ参照
        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>
        /// public インスタンスフィールドの値を返す。対象が null（Unity の偽 null 含む）やフィールド無しなら null。
        /// </summary>
        public static object GetField(object target, string fieldName)
        {
            // Unity の偽 null も含めて null として扱う
            if (target == null || (target is Object unityObject && unityObject == null))
            {
                return null;
            }

            FieldInfo field = target.GetType().GetField(fieldName, InstanceFields);
            return field?.GetValue(target);
        }

        /// <summary>
        /// float フィールドを返す。無ければ既定値。
        /// </summary>
        public static float GetFloat(object target, string fieldName, float fallback)
        {
            return GetField(target, fieldName) is float value ? value : fallback;
        }

        /// <summary>
        /// bool フィールドを返す。無ければ既定値。
        /// </summary>
        public static bool GetBool(object target, string fieldName, bool fallback)
        {
            return GetField(target, fieldName) is bool value ? value : fallback;
        }

        /// <summary>
        /// Vector3 フィールドを返す。無ければ既定値。
        /// </summary>
        public static Vector3 GetVector3(object target, string fieldName, Vector3 fallback)
        {
            return GetField(target, fieldName) is Vector3 value ? value : fallback;
        }

        /// <summary>
        /// アバタールートからの相対パス（ルート自身は空）。メタデータの Transform 参照に使う。
        /// </summary>
        public static string PathOf(Transform target, Transform root)
        {
            return AnimationUtility.CalculateTransformPath(target, root);
        }

        /// <summary>
        /// root 以下（非アクティブ含む）から型名が一致するコンポーネントを列挙する。
        /// </summary>
        public static List<Component> FindComponents(GameObject root, string typeName)
        {
            var result = new List<Component>();
            foreach (Component component in root.GetComponentsInChildren<Component>(true))
            {
                // Missing Script（null）は除外し、名前空間に依存せず型名で判定
                if (component != null && component.GetType().Name == typeName)
                {
                    result.Add(component);
                }
            }

            return result;
        }
    }
}
