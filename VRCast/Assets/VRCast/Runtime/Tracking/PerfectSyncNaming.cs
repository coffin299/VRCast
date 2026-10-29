namespace VRCast.Tracking
{
    /// <summary>
    /// パーフェクトシンクでアバターの BlendShape を探すときの名前の系統。
    /// トラッキングの値はどれも ARKit 互換 52 種のまま。
    /// </summary>
    public enum PerfectSyncNaming
    {
        /// <summary>ARKit / Perfect Sync 名（eyeBlinkLeft 等）。</summary>
        ArKit = 0,

        /// <summary>VRCFaceTracking Unified Expressions 名（EyeClosedLeft 等）。</summary>
        Unified = 1,

        /// <summary>手動生成・お手製（ARKit 名優先、足りない枠だけ Unified 名）。</summary>
        Handmade = 2,
    }
}
