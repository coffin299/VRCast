namespace VRCast.Tracking
{
    /// <summary>
    /// パーフェクトシンクでアバターの BlendShape を探すときの名前の系統。
    /// トラッキングの値はどちらも ARKit 互換 52 種のまま。
    /// </summary>
    public enum PerfectSyncNaming
    {
        /// <summary>ARKit / Perfect Sync 名（eyeBlinkLeft 等）。</summary>
        ArKit = 0,

        /// <summary>VRCFaceTracking Unified Expressions 名（EyeClosedLeft 等）。</summary>
        Unified = 1,
    }
}
