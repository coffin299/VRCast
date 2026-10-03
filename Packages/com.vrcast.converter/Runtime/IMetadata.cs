namespace VRCast.AvatarFormat
{
    /// <summary>
    /// metadata/*.json のモデル共通の検証インターフェース。
    /// </summary>
    public interface IMetadata
    {
        /// <summary>
        /// 内容を検証し、問題があればエラーメッセージを、無ければ null を返す。
        /// </summary>
        string Validate();
    }
}
