namespace VRCast.Core
{
    /// <summary>
    /// 描画に使う GPU の Windows の優先設定（「グラフィックの設定」と同じ。値は Windows の GpuPreference と一致させる。
    /// settings.json には数値で保存するため並び順を変えない）。
    /// </summary>
    public enum GpuPreference
    {
        // Windows に任せる
        Auto = 0,

        // 省電力（ノート PC の内蔵 GPU など）
        PowerSaving = 1,

        // 高パフォーマンス（外付け GPU など）
        HighPerformance = 2,
    }
}
