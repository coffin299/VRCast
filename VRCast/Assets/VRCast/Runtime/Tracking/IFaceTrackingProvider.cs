namespace VRCast.Tracking
{
    /// <summary>
    /// フェイストラッキングの入力元。Driver は Provider の種類を知らずに最新フレームを取得する。
    /// </summary>
    public interface IFaceTrackingProvider
    {
        /// <summary>
        /// 直近の状態（UI 表示用）。
        /// </summary>
        string Status { get; }

        /// <summary>
        /// 直近 1 秒に受信したフレーム数（途絶・無効時は 0）。
        /// </summary>
        int FramesPerSecond { get; }

        /// <summary>
        /// 有効かつ新しいフレームがあれば true。途絶・無効時は false。
        /// </summary>
        bool TryGetFrame(out FaceTrackingFrame frame);
    }
}
