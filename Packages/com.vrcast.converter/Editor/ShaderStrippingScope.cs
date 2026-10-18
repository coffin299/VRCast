using System;
using UnityEditor;
using UnityEngine.Rendering;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// AssetBundle 書き出しのあいだだけ、Graphics 設定のシェーダー削り込みを緩める。
    /// Automatic のままだとシーン未使用のライトマップ／フォグ／インスタンス用バリアントが削られ、
    /// アプリ側で砂嵐やマゼンタになることがある。終わったら必ず元に戻す。
    /// </summary>
    internal sealed class ShaderStrippingScope : IDisposable
    {
        // 0 = Automatic / Strip Unused, 1 = Custom / Strip All, 2 = Keep All（Instancing のみ）
        private const int CustomMode = 1;
        private const int InstancingKeepAll = 2;

        private readonly SerializedObject _graphics;
        private readonly int _lightmap;
        private readonly int _fog;
        private readonly int _instancing;
        private readonly bool[] _lightmapKeep;
        private readonly bool[] _fogKeep;
        private bool _disposed;

        private static readonly string[] LightmapKeepNames =
        {
            "m_LightmapKeepPlain",
            "m_LightmapKeepDirCombined",
            "m_LightmapKeepDynamicPlain",
            "m_LightmapKeepDynamicDirCombined",
            "m_LightmapKeepShadowMask",
            "m_LightmapKeepSubtractive",
        };

        private static readonly string[] FogKeepNames =
        {
            "m_FogKeepLinear",
            "m_FogKeepExp",
            "m_FogKeepExp2",
        };

        public ShaderStrippingScope()
        {
            _graphics = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            _lightmap = ReadEnum("m_LightmapStripping");
            _fog = ReadEnum("m_FogStripping");
            _instancing = ReadEnum("m_InstancingStripping");
            _lightmapKeep = ReadBools(LightmapKeepNames);
            _fogKeep = ReadBools(FogKeepNames);

            // Custom + 全モード残す / Instancing は Keep All
            WriteEnum("m_LightmapStripping", CustomMode);
            WriteEnum("m_FogStripping", CustomMode);
            WriteEnum("m_InstancingStripping", InstancingKeepAll);
            WriteBools(LightmapKeepNames, true);
            WriteBools(FogKeepNames, true);
            _graphics.ApplyModifiedPropertiesWithoutUndo();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            WriteEnum("m_LightmapStripping", _lightmap);
            WriteEnum("m_FogStripping", _fog);
            WriteEnum("m_InstancingStripping", _instancing);
            WriteBools(LightmapKeepNames, _lightmapKeep);
            WriteBools(FogKeepNames, _fogKeep);
            _graphics.ApplyModifiedPropertiesWithoutUndo();
        }

        private int ReadEnum(string name)
        {
            SerializedProperty property = _graphics.FindProperty(name);
            return property != null ? property.enumValueIndex : 0;
        }

        private void WriteEnum(string name, int value)
        {
            SerializedProperty property = _graphics.FindProperty(name);
            if (property != null)
            {
                property.enumValueIndex = value;
            }
        }

        private bool[] ReadBools(string[] names)
        {
            var values = new bool[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                SerializedProperty property = _graphics.FindProperty(names[i]);
                values[i] = property != null && property.boolValue;
            }

            return values;
        }

        private void WriteBools(string[] names, bool value)
        {
            for (int i = 0; i < names.Length; i++)
            {
                SerializedProperty property = _graphics.FindProperty(names[i]);
                if (property != null)
                {
                    property.boolValue = value;
                }
            }
        }

        private void WriteBools(string[] names, bool[] values)
        {
            for (int i = 0; i < names.Length; i++)
            {
                SerializedProperty property = _graphics.FindProperty(names[i]);
                if (property != null)
                {
                    property.boolValue = values[i];
                }
            }
        }
    }
}
