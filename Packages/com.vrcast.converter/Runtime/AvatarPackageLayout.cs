namespace VRCast.AvatarFormat
{
    /// <summary>
    /// .vrcaster (ZIP) 内のファイル配置とフォーマット定数。Exporter と Runtime で共有する。
    /// </summary>
    public static class AvatarPackageLayout
    {
        // パッケージの拡張子
        public const string Extension = ".vrcaster";

        // 現在のフォーマットバージョン（互換性のない変更時に増やす）
        public const int FormatVersion = 0;

        // 必須エントリ: パッケージ情報
        public const string ManifestEntry = "manifest.json";

        // 必須エントリ: アバター Prefab を含む AssetBundle
        public const string BundleEntry = "avatar.bundle";

        // 任意エントリ: VRChat 固有設定を変換した JSON の格納先
        public const string MetadataPrefix = "metadata/";

        // bundle 内のアバター Prefab のアセットパス（Exporter が一時的にこのパスへ保存する）
        public const string PrefabAssetPath = "Assets/__VRCastExport/avatar.prefab";
    }
}
