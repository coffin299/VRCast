namespace VRCast.Tracking
{
    /// <summary>
    /// 腕・手のトラッキングの入力元。顔のみの Provider（OpenSeeFace）では常に false を返す。
    /// </summary>
    public interface IBodyTrackingProvider
    {
        /// <summary>
        /// 有効かつ新しいフレームがあれば true。途絶・無効・非対応時は false。
        /// </summary>
        bool TryGetBody(out BodyTrackingFrame frame);
    }
}
