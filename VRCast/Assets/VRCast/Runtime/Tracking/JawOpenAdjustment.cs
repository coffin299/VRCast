using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// パーフェクトシンクの口の開き（jawOpen、0〜1）の補正。
    /// Deadzone までの開きは閉じた口とみなし（真顔でも少し開いた値が出る入力元向け）、残りを 0〜1 に広げてから Gain 倍する。
    /// 既定値（default）は Deadzone 0・Gain 0 で、0 以下の Gain は 1 とみなすので何も変えない。
    /// </summary>
    public struct JawOpenAdjustment
    {
        // 閉じた口とみなす開き（0 = 補正なし）
        public float Deadzone;

        // 開きの倍率（1 未満で抑え、1 より大きいと強める）
        public float Gain;

        /// <summary>
        /// 口の開きを補正して 0〜1 で返す。無視する開きが 1 以上なら、受信は 0〜1 なので常に閉じる。
        /// </summary>
        public float Apply(float value)
        {
            float gain = Gain > 0f ? Gain : 1f;
            float opened = value;
            if (Deadzone > 0f)
            {
                opened = Deadzone >= 1f ? 0f : Mathf.InverseLerp(Deadzone, 1f, value);
            }

            return Mathf.Clamp01(opened * gain);
        }
    }
}
