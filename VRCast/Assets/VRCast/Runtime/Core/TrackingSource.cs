namespace VRCast.Core
{
    /// <summary>
    /// トラッキングの入力元（settings.json には数値で保存するため並び順を変えない）。
    /// </summary>
    public enum TrackingSource
    {
        // 同梱の MediaPipe トラッカー（顔・腕・手）
        MediaPipe = 0,

        // 同梱の OpenSeeFace（顔のみ）
        OpenSeeFace = 1,
    }
}
