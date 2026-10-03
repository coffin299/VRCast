namespace VRCast.Core
{
    /// <summary>
    /// 利用者が入力したパス文字列の整形。
    /// </summary>
    public static class PathUtility
    {
        /// <summary>
        /// 前後の空白と、エクスプローラーの「パスのコピー」で付く前後の " を除く。null は空文字。
        /// </summary>
        public static string NormalizeInput(string path)
        {
            return path?.Trim().Trim('"') ?? string.Empty;
        }
    }
}
