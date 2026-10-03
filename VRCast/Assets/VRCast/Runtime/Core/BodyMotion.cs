namespace VRCast.Core
{
    /// <summary>
    /// 頭の位置に合わせた体の動かし方（settings.json には数値で保存するため並び順を変えない）。
    /// </summary>
    public enum BodyMotion
    {
        // 足を固定して背骨・胸を傾ける
        Lean = 0,

        // 腰ごと体全体を動かす（上下も反映）
        Move = 1,

        // 両方
        LeanAndMove = 2,
    }
}
