namespace VRCast.Core
{
    /// <summary>
    /// 同梱の MediaPipe トラッカーの動作（settings.json には数値で保存するため並び順を変えない）。
    /// </summary>
    public enum TrackerMode
    {
        // なめらか: カメラの fps まで推定し、体・手は 2 フレームに 1 回（毎フレーム全部推定していた頃の毎秒 20 回と同程度以下の負荷）
        Smooth = 0,

        // エコ: 推定を毎秒 20 回までにし、体・手は 3 フレームに 1 回（負荷は「なめらか」の半分程度）
        Eco = 1,
    }
}
