namespace VRCast.Core
{
    /// <summary>
    /// VRCast 本体と同梱トラッカーのプロセスの優先度（settings.json には数値で保存するため並び順を変えない）。
    /// リアルタイムはマウス・音声まで止まる恐れがあるため用意しない。
    /// </summary>
    public enum ProcessPriority
    {
        // 通常より低い（ゲームを優先したいとき）
        BelowNormal = 0,

        // 通常（Windows の既定）
        Normal = 1,

        // 通常より高い
        AboveNormal = 2,

        // 高（他のアプリが重くなることがある）
        High = 3,
    }
}
