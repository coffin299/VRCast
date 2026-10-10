using System;
using UnityEditor;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// 書き出す複製に付いた最適化ツールの設定のうち、表情に使う BlendShape を消してしまうものを止める。
    /// AAO の Trace and Optimize は FX で動かない BlendShape を固定・削除するため、FaceEmo で編集した後に
    /// 「アバターに適用」し直していない表情等の BlendShape が消え、アプリで表情を動かせなくなる。
    /// 最適化ツールのアセンブリは参照せず、型名と SerializedObject で設定する（無ければ何もしない）。
    /// </summary>
    public static class OptimizerGuard
    {
        // AAO の自動最適化コンポーネント（Anatawa12.AvatarOptimizer.TraceAndOptimize。型名だけで探す）
        private const string TraceAndOptimizeTypeName = "TraceAndOptimize";

        // BlendShape の最適化の設定名に含まれる語（版により optimizeBlendShape / freezeBlendShape 等）
        private const string BlendShapeKeyword = "BlendShape";

        /// <summary>
        /// root 以下（非アクティブ含む）の BlendShape の最適化を止め、止めた設定の数を返す。NDMF の処理より前に呼ぶ。
        /// </summary>
        public static int KeepBlendShapes(GameObject root)
        {
            int changed = 0;
            foreach (Component component in ReflectionUtility.FindComponents(root, TraceAndOptimizeTypeName))
            {
                using (var serialized = new SerializedObject(component))
                {
                    // 直下の bool の設定のうち、名前に BlendShape を含み ON のものを OFF にする
                    SerializedProperty property = serialized.GetIterator();
                    bool enterChildren = true;
                    while (property.NextVisible(enterChildren))
                    {
                        enterChildren = false;
                        if (property.propertyType == SerializedPropertyType.Boolean && property.boolValue
                            && property.name.IndexOf(BlendShapeKeyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            property.boolValue = false;
                            changed++;
                        }
                    }

                    // 複製だけを書き換える（元のアバターと Prefab には影響しない）
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }

            return changed;
        }
    }
}
