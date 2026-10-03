using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor.Animations;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// VRChat SDK に依存せず、リフレクションで VRCAvatarDescriptor の設定値を読む。
    /// SDK が無いプロジェクトでは常に「見つからない」として振る舞う。
    /// </summary>
    public static class VrcDescriptorReader
    {
        // 対象コンポーネントの型名（名前空間は SDK バージョンで変わり得るため型名のみで判定）
        private const string DescriptorTypeName = "VRCAvatarDescriptor";

        // CustomAnimLayer.type の FX を表す列挙名
        private const string FxLayerTypeName = "FX";

        // リフレクションで参照するフィールド名
        private const BindingFlags InstanceFields = BindingFlags.Public | BindingFlags.Instance;

        public static Component FindDescriptor(GameObject root)
        {
            // ルート上のコンポーネントから型名で探す
            foreach (Component component in root.GetComponents<Component>())
            {
                if (component != null && component.GetType().Name == DescriptorTypeName)
                {
                    return component;
                }
            }

            return null;
        }

        /// <summary>
        /// カスタム設定された FX レイヤーの AnimatorController を返す。無ければ null。
        /// </summary>
        public static AnimatorController GetFxController(Component descriptor)
        {
            // baseAnimationLayers 配列を取得
            if (!(GetField(descriptor, "baseAnimationLayers") is IEnumerable layers))
            {
                return null;
            }

            foreach (object layer in layers)
            {
                // FX 以外、または既定（未設定）レイヤーはスキップ
                if (layer == null || GetField(layer, "type")?.ToString() != FxLayerTypeName)
                {
                    continue;
                }

                if (GetField(layer, "isDefault") is bool isDefault && isDefault)
                {
                    return null;
                }

                // Override Controller 等は対象外（AnimatorController のみ扱う）
                return GetField(layer, "animatorController") as AnimatorController;
            }

            return null;
        }

        /// <summary>
        /// Expression Parameters の名前と既定値の対応を返す。無ければ空。
        /// </summary>
        public static Dictionary<string, float> GetExpressionParameterDefaults(Component descriptor)
        {
            var result = new Dictionary<string, float>();

            // expressionParameters (ScriptableObject) → parameters 配列
            object expressionParameters = GetField(descriptor, "expressionParameters");
            if (expressionParameters == null || !(GetField(expressionParameters, "parameters") is IEnumerable parameters))
            {
                return result;
            }

            foreach (object parameter in parameters)
            {
                // 名前が空のエントリは無視
                if (parameter == null || !(GetField(parameter, "name") is string name) || string.IsNullOrEmpty(name))
                {
                    continue;
                }

                // defaultValue は float で保持される（Bool/Int も数値化済み）
                if (GetField(parameter, "defaultValue") is float value)
                {
                    result[name] = value;
                }
            }

            return result;
        }

        private static object GetField(object target, string fieldName)
        {
            // Unity の偽 null も含めて null として扱う
            if (target == null || (target is UnityEngine.Object unityObject && unityObject == null))
            {
                return null;
            }

            // public インスタンスフィールドのみ参照
            FieldInfo field = target.GetType().GetField(fieldName, InstanceFields);
            return field?.GetValue(target);
        }
    }
}
