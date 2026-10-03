namespace VRCast.Tracking
{
    /// <summary>
    /// パケット解析で共通の数値検査。
    /// </summary>
    internal static class TrackingMath
    {
        /// <summary>
        /// すべて有限値（NaN・無限大を含まない）なら true。
        /// </summary>
        public static bool AllFinite(params float[] values)
        {
            foreach (float value in values)
            {
                // 1 つでも非有限なら不正
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
