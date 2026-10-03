namespace VRCast.Core
{
    /// <summary>
    /// 操作パネルの表示言語（settings.json には数値で保存するため並び順を変えない）。
    /// </summary>
    public enum UiLanguage
    {
        // OS の言語に合わせる（日本語なら日本語、それ以外は英語）
        Auto = 0,
        English = 1,
        Japanese = 2,
    }
}
