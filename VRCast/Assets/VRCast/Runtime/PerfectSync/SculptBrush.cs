namespace VRCast.PerfectSync
{
    /// <summary>
    /// 作成モードのブラシの種類（UI の並び順）。
    /// </summary>
    public enum SculptBrush
    {
        // つまむ: 押した位置を画面に平行に動かす
        Grab,

        // 膨らませる: 元の法線方向へ押し出す
        Inflate,

        // へこませる: 元の法線と逆方向へ押し込む
        Deflate,

        // なめらかにする: 差分を隣の頂点の平均へ寄せる
        Smooth,

        // 元に戻す: 差分を 0 へ寄せる
        Erase,
    }
}
