namespace VRCast.Core
{
    /// <summary>
    /// 目の視線をボーンで回すか、BlendShape キーで動かすか。
    /// </summary>
    public enum EyeLookDriveMode
    {
        /// <summary>目ボーンがあればボーン、無ければキー。</summary>
        Auto = 0,

        /// <summary>目ボーンで回す（キーは動かさない）。</summary>
        Bones = 1,

        /// <summary>eyeLook などの BlendShape キーで動かす（ボーンは回さない）。</summary>
        Keys = 2,
    }
}
