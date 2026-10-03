using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRCast.AvatarFormat;

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

        /// <summary>
        /// リップシンク・まぶた設定を Runtime 用データに変換する。パスは root からの相対パス。
        /// </summary>
        public static AvatarDescriptorData GetDescriptorData(Component descriptor, Transform root)
        {
            return new AvatarDescriptorData
            {
                lipSync = GetLipSync(descriptor, root),
                eyelids = GetEyelids(descriptor, root),
            };
        }

        private static LipSyncData GetLipSync(Component descriptor, Transform root)
        {
            var data = new LipSyncData();

            // Viseme / 口開閉に使うメッシュが無ければ none
            var mesh = GetField(descriptor, "VisemeSkinnedMesh") as SkinnedMeshRenderer;
            if (mesh == null)
            {
                return data;
            }

            data.meshPath = AnimationUtility.CalculateTransformPath(mesh.transform, root);

            // LipSyncStyle の列挙名で分岐（ボーン方式等は未対応）
            switch (GetField(descriptor, "lipSync")?.ToString())
            {
                case "VisemeBlendShape":
                    // 15 個揃っている場合のみ採用
                    if (GetField(descriptor, "VisemeBlendShapes") is string[] visemes
                        && visemes.Length == AvatarDescriptorData.VisemeCount)
                    {
                        data.mode = LipSyncData.ModeVisemeBlendShape;
                        data.visemes = Array.ConvertAll(visemes, name => name ?? string.Empty);
                    }

                    break;

                case "JawFlapBlendShape":
                    // 口開閉 BlendShape 名がある場合のみ採用
                    if (GetField(descriptor, "MouthOpenBlendShapeName") is string mouthOpen
                        && !string.IsNullOrEmpty(mouthOpen))
                    {
                        data.mode = LipSyncData.ModeJawFlapBlendShape;
                        data.mouthOpenBlendShape = mouthOpen;
                    }

                    break;
            }

            return data;
        }

        private static EyelidData GetEyelids(Component descriptor, Transform root)
        {
            var data = new EyelidData();

            // customEyeLookSettings（構造体）を取得し、BlendShape 方式のみ扱う
            object eyeLook = GetField(descriptor, "customEyeLookSettings");
            if (eyeLook == null || GetField(eyeLook, "eyelidType")?.ToString() != "Blendshapes")
            {
                return data;
            }

            // メッシュと [blink, lookingUp, lookingDown] のインデックス
            var mesh = GetField(eyeLook, "eyelidsSkinnedMesh") as SkinnedMeshRenderer;
            if (mesh == null || mesh.sharedMesh == null
                || !(GetField(eyeLook, "eyelidsBlendshapes") is int[] indices) || indices.Length == 0)
            {
                return data;
            }

            // blink のインデックスが範囲内なら名前に変換（Runtime は名前で解決する）
            int blink = indices[0];
            if (blink < 0 || blink >= mesh.sharedMesh.blendShapeCount)
            {
                return data;
            }

            data.meshPath = AnimationUtility.CalculateTransformPath(mesh.transform, root);
            data.blinkBlendShape = mesh.sharedMesh.GetBlendShapeName(blink);
            return data;
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
