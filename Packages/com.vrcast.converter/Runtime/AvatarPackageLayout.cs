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

        // 任意エントリ: 表情プリセット
        public const string ExpressionsEntry = MetadataPrefix + "expressions.json";

        // 任意エントリ: リップシンク・まぶた設定
        public const string DescriptorEntry = MetadataPrefix + "descriptor.json";

        // 任意エントリ: 揺れもの（PhysBone 近似）
        public const string PhysBonesEntry = MetadataPrefix + "physbones.json";

        // 任意エントリ: Constraint（位置・回転・親子・Aim 等）
        public const string ConstraintsEntry = MetadataPrefix + "constraints.json";

        // 任意エントリ: BlendShape の同期（Modular Avatar の Blendshape Sync）
        public const string BlendShapeSyncEntry = MetadataPrefix + "blendshape_sync.json";

        // 任意エントリ: パーティクルをまとめて切り替える単位（FX で表示を ON/OFF しているオブジェクト）
        public const string ParticleGroupsEntry = MetadataPrefix + "particle_groups.json";

        // 任意エントリ: FX で表示を ON/OFF しているオブジェクト（衣装・小物の切り替え等）
        public const string FxTogglesEntry = MetadataPrefix + "fx_toggles.json";

        // 任意エントリ: パーフェクトシンク化の設定（打った目印・形状ごとの元と強さ・既存 BlendShape の割り当て。アプリが書く）
        public const string PerfectSyncEntry = MetadataPrefix + "perfect_sync.json";

        // 任意エントリ: パーフェクトシンク化で生成した形状（頂点の移動量のバイナリ。アプリが書く）
        public const string PerfectSyncShapesEntry = MetadataPrefix + "perfect_sync_shapes.bin";

        // metadata 1 ファイルの上限サイズ（1 MiB）
        public const long MaxMetadataBytes = 1024 * 1024;

        // 生成した形状のバイナリの上限サイズ（64 MiB）
        public const long MaxPerfectSyncShapesBytes = 64L * 1024 * 1024;

        // bundle 内のアバター Prefab のアセットパス（Exporter が一時的にこのパスへ保存する）
        public const string PrefabAssetPath = "Assets/__VRCastExport/avatar.prefab";

        // FX の初期状態で再生され続ける小物の動きを legacy クリップにしたものの名前の接頭辞
        // （アバタールートの Animation に入れる。Runtime はこの名前のクリップだけを同時に再生する）
        public const string FxMotionClipPrefix = "VRCastFx_";

        // 追加の表情に指定したクリップのうち、人体以外のボーンの動きを legacy クリップにしたものの名前の接頭辞
        // （同じ Animation に入れる。小物の自動再生には含めず、選んでいるあいだだけ再生する）
        public const string ExtraBoneClipPrefix = "VRCastBone_";
    }
}
