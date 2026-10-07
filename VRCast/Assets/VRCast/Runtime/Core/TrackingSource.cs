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

        // 外部アプリ（iPhone の Waidayo 等）が VMC プロトコルで送る顔の値（顔のみ。LAN から受信）
        Vmc = 2,

        // iPhone の iFacialMocap が独自形式で送る顔の値（顔のみ。LAN から受信し、送信開始の合図をこちらから送る）
        IFacialMocap = 3,
    }

    /// <summary>
    /// 入力元ごとにできることの判定（入力元の種類での分岐をここへ集める）。
    /// </summary>
    public static class TrackingSourceInfo
    {
        /// <summary>
        /// VRCast が同梱トラッカー（カメラ）を起動して受信する入力元なら true。
        /// </summary>
        public static bool UsesBundledTracker(TrackingSource source)
        {
            return source == TrackingSource.MediaPipe || source == TrackingSource.OpenSeeFace;
        }

        /// <summary>
        /// 腕・手のトラッキングを受信できる入力元なら true。
        /// </summary>
        public static bool HasArms(TrackingSource source)
        {
            return source == TrackingSource.MediaPipe;
        }

        /// <summary>
        /// ARKit 互換の BlendShape の値（パーフェクトシンク・表情の反映に使う）を受信できる入力元なら true。
        /// </summary>
        public static bool HasArKit(TrackingSource source)
        {
            return source != TrackingSource.OpenSeeFace;
        }

        /// <summary>
        /// 別の PC・スマートフォンから LAN 経由で受信する入力元なら true（127.0.0.1 以外でも待ち受ける）。
        /// </summary>
        public static bool ReceivesFromNetwork(TrackingSource source)
        {
            return !UsesBundledTracker(source);
        }

        /// <summary>
        /// 入力元の受信ポート（VMC は設定のポート、iFacialMocap はアプリが決めた固定ポート、同梱トラッカーは trackingPort）。
        /// </summary>
        public static int PortOf(AppSettings settings)
        {
            switch (settings.trackingSource)
            {
                case TrackingSource.Vmc:
                    return settings.vmcPort;
                case TrackingSource.IFacialMocap:
                    return AppSettings.IFacialMocapPort;
                default:
                    return settings.trackingPort;
            }
        }
    }
}
