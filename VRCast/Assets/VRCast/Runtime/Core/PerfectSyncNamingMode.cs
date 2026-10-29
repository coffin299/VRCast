namespace VRCast.Core
{
    /// <summary>
    /// パーフェクトシンクの名前系統の選び方（設定用。実際に動いている系統は Tracking.PerfectSyncNaming）。
    /// </summary>
    public enum PerfectSyncNamingMode
    {
        /// <summary>アバターの一致数から自動で選ぶ（パーフェクトシンク化済みなら手動生成）。</summary>
        Auto = 0,

        /// <summary>常に ARKit 名。</summary>
        ArKit = 1,

        /// <summary>常に Unified Expressions 名。</summary>
        Unified = 2,

        /// <summary>手動生成・お手製（ARKit 名を優先し、足りない枠だけ Unified 名も使う）。</summary>
        Handmade = 3,
    }
}
