namespace VRCast.Core
{
    /// <summary>
    /// P コアと E コアがある CPU（Intel 12 世代以降・Core Ultra 等）で、VRCast 本体と同梱トラッカーに使わせるコア
    /// （settings.json には数値で保存するため並び順を変えない）。
    /// </summary>
    public enum HybridCoreSelection
    {
        // Windows に任せる
        Auto = 0,

        // E コアだけ（P コアをゲームに譲る。処理は遅くなる）
        EfficiencyOnly = 1,

        // P コアだけ（速いがゲームと取り合う）
        PerformanceOnly = 2,
    }
}
