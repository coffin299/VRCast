using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える 1 つの揺れものチェーンの ON/OFF。
    /// path はアバターのルートからの相対パス（空ならルート自身。physbones.json の rootPath と同じ）。
    /// </summary>
    [Serializable]
    public class PhysBoneToggle
    {
        public string path = string.Empty;
        public bool enabled = true;
    }
}
