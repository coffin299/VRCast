using System;
using System.Collections;
using System.Collections.Generic;
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
            // Descriptor の Eyelids 設定を優先し、無ければ顔メッシュの BlendShape 名から推定
            EyelidData data = GetConfiguredEyelids(descriptor, root) ?? GuessEyelids(descriptor, root) ?? new EyelidData();
            GuessWinks(data, root);
            return data;
        }

        private static void GuessWinks(EyelidData data, Transform root)
        {
            // まばたき用メッシュが無ければウインクも無し
            if (data.blinkBlendShapes.Length == 0)
            {
                return;
            }

            // 同じメッシュ上で左右の組が揃った最初の候補を採用
            Transform node = string.IsNullOrEmpty(data.meshPath) ? root : root.Find(data.meshPath);
            var mesh = node != null ? node.GetComponent<SkinnedMeshRenderer>() : null;
            if (mesh == null || mesh.sharedMesh == null)
            {
                return;
            }

            foreach (string[] candidate in WinkCandidates)
            {
                string[] names = FindBlendShapes(mesh.sharedMesh, candidate);
                if (names != null)
                {
                    data.winkLeftBlendShape = names[0];
                    data.winkRightBlendShape = names[1];
                    return;
                }
            }
        }

        private static EyelidData GetConfiguredEyelids(Component descriptor, Transform root)
        {
            // customEyeLookSettings（構造体）を取得し、BlendShape 方式のみ扱う
            object eyeLook = GetField(descriptor, "customEyeLookSettings");
            if (eyeLook == null || GetField(eyeLook, "eyelidType")?.ToString() != "Blendshapes")
            {
                return null;
            }

            // メッシュと [blink, lookingUp, lookingDown] のインデックス
            var mesh = GetField(eyeLook, "eyelidsSkinnedMesh") as SkinnedMeshRenderer;
            if (mesh == null || mesh.sharedMesh == null
                || !(GetField(eyeLook, "eyelidsBlendshapes") is int[] indices) || indices.Length == 0)
            {
                return null;
            }

            // blink のインデックスが範囲内なら名前に変換（Runtime は名前で解決する）
            int blink = indices[0];
            if (blink < 0 || blink >= mesh.sharedMesh.blendShapeCount)
            {
                return null;
            }

            return new EyelidData
            {
                meshPath = AnimationUtility.CalculateTransformPath(mesh.transform, root),
                blinkBlendShapes = new[] { mesh.sharedMesh.GetBlendShapeName(blink) },
            };
        }

        private static EyelidData GuessEyelids(Component descriptor, Transform root)
        {
            // FX アニメーションでまばたきするアバター向けに、Viseme 用の顔メッシュから探す
            var mesh = GetField(descriptor, "VisemeSkinnedMesh") as SkinnedMeshRenderer;
            if (mesh == null || mesh.sharedMesh == null)
            {
                return null;
            }

            // 候補を優先順に試し、全 BlendShape が見つかった最初の組を採用
            foreach (string[] candidate in BlinkCandidates)
            {
                string[] names = FindBlendShapes(mesh.sharedMesh, candidate);
                if (names != null)
                {
                    return new EyelidData
                    {
                        meshPath = AnimationUtility.CalculateTransformPath(mesh.transform, root),
                        blinkBlendShapes = names,
                    };
                }
            }

            return null;
        }

        private static string[] FindBlendShapes(Mesh mesh, string[] candidate)
        {
            var names = new string[candidate.Length];
            for (int i = 0; i < candidate.Length; i++)
            {
                // 大文字小文字を無視して一致する BlendShape を探し、実際の名前を記録
                names[i] = null;
                for (int j = 0; j < mesh.blendShapeCount; j++)
                {
                    string name = mesh.GetBlendShapeName(j);
                    if (string.Equals(name, candidate[i], StringComparison.OrdinalIgnoreCase))
                    {
                        names[i] = name;
                        break;
                    }
                }

                // 1 つでも欠けたら不採用
                if (names[i] == null)
                {
                    return null;
                }
            }

            return names;
        }

        // まばたき BlendShape 名の候補（優先順。左右別の組は両方揃った場合のみ採用）
        private static readonly string[][] BlinkCandidates =
        {
            new[] { "まばたき" },
            new[] { "blink" },
            new[] { "eye_blink" },
            new[] { "eyes_close" },
            new[] { "eye_close" },
            new[] { "Fcl_EYE_Close" },
            new[] { "eyeBlinkLeft", "eyeBlinkRight" },
            new[] { "blink_L", "blink_R" },
            new[] { "Blink_Left", "Blink_Right" },
        };

        // ウインク BlendShape 名の候補（[アバターの左目, 右目]、優先順。MMD 系の「ウィンク」は左目）
        private static readonly string[][] WinkCandidates =
        {
            new[] { "ウィンク", "ウィンク右" },
            new[] { "wink_L", "wink_R" },
            new[] { "Wink_Left", "Wink_Right" },
            new[] { "winkL", "winkR" },
            new[] { "eyeBlinkLeft", "eyeBlinkRight" },
            new[] { "blink_L", "blink_R" },
            new[] { "Blink_Left", "Blink_Right" },
            new[] { "eye_close_L", "eye_close_R" },
            new[] { "Fcl_EYE_Close_L", "Fcl_EYE_Close_R" },
        };

        private static object GetField(object target, string fieldName)
        {
            // 共通のリフレクション補助へ委譲
            return ReflectionUtility.GetField(target, fieldName);
        }
    }
}
