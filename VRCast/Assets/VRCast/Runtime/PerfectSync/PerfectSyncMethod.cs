namespace VRCast.PerfectSync
{
    /// <summary>
    /// パーフェクトシンク化で形状を作る方式。
    /// 値は perfect_sync.json の method に保存するため変えない（0 = これまでの式、1 = 形状の転写）。
    /// </summary>
    public enum PerfectSyncMethod
    {
        /// <summary>部位ごとの決まった式。</summary>
        Formula = 0,

        /// <summary>手本の顔の動きを、目印に合わせた薄板スプラインで写す。</summary>
        Transfer = 1,
    }
}
