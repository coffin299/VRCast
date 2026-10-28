namespace VRCast.Core
{
    /// <summary>
    /// 描画の垂直同期の方式。
    /// </summary>
    public enum VSyncMode
    {
        /// <summary>同期しない（FPS 上限を使う）。</summary>
        Off = 0,

        /// <summary>毎リフレッシュに合わせる（ティアリングを抑える。FPS 上限は使わない）。</summary>
        On = 1,

        /// <summary>
        /// NVIDIA Fast Sync（ドライバー側。アプリは待たず、完成した最新フレームを出す。
        /// NVIDIA 以外では選べない。反映に再起動が必要なことがある）。
        /// </summary>
        FastSync = 2,

        /// <summary>
        /// 可変リフレッシュ（G-Sync / FreeSync）。アプリは待たず FPS 上限内で動かす。
        /// モニターとドライバー側の VRR が有効なときだけ効果がある。NVIDIA ではアプリ許可を書く。
        /// </summary>
        Vrr = 3,
    }
}
