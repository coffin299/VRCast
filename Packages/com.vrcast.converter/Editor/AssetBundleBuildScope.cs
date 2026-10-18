using System;
using UnityEditor;
using UnityEngine;

namespace VRCast.Converter.Editor
{
    /// <summary>
    /// AssetBundle 書き出しのあいだだけ、見た目を壊しやすいプロジェクト設定を抑える。
    /// ・シェーダーバリアントの削り込み（ライトマップ／フォグ／インスタンス）
    /// ・Optimize Mesh Data（UV1 以降が削られ、lilToon 等が砂嵐になる Unity 2022 の既知問題）
    /// 終わったら必ず元に戻す。
    /// </summary>
    internal sealed class AssetBundleBuildScope : IDisposable
    {
        private readonly ShaderStrippingScope _shaderStripping;
        private readonly bool _stripUnusedMeshComponents;
        private bool _disposed;

        public AssetBundleBuildScope()
        {
            _stripUnusedMeshComponents = PlayerSettings.stripUnusedMeshComponents;
            if (_stripUnusedMeshComponents)
            {
                PlayerSettings.stripUnusedMeshComponents = false;
            }

            _shaderStripping = new ShaderStrippingScope();
        }

        /// <summary>
        /// Optimize Mesh Data を書き出し中に OFF にしたとき true（ログ用）。
        /// </summary>
        public bool DisabledMeshOptimization => _stripUnusedMeshComponents;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _shaderStripping.Dispose();
            if (_stripUnusedMeshComponents)
            {
                PlayerSettings.stripUnusedMeshComponents = true;
            }
        }
    }
}
