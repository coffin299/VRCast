using UnityEngine;

namespace VRCast.AvatarFormat
{
    /// <summary>
    /// 書き出し時に控えた、VRChat の立ち・待機クリップ。アプリのマスコットモードで選ぶ。
    /// 許可リストに入っているので、読込後も残る。
    /// </summary>
    public sealed class StandClipHolder : MonoBehaviour
    {
        // clips と同じ長さ。画面に出す名前
        public AnimationClip[] clips = System.Array.Empty<AnimationClip>();
        public string[] labels = System.Array.Empty<string>();
    }
}
