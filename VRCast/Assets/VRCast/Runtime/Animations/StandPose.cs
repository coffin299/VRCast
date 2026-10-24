using UnityEngine;

namespace VRCast.Animations
{
    /// <summary>
    /// マスコットの既定の立ちポーズのある時刻の角度（アバタールート基準、度）。
    /// </summary>
    public struct StandPoseSample
    {
        // 腰の左右の傾きとひねり（正 = アバターの右へ重心）
        public float HipRoll;
        public float HipYaw;

        // 腰と逆向きの背骨の傾き
        public float SpineRoll;

        // 吸ったときの胸の反り（正 = 後ろ）
        public float ChestPitch;

        // 遊脚側の膝の曲げ（正 = 曲げる）。重心と逆の脚
        public float LeftKnee;
        public float RightKnee;

        // 遊脚と逆の腕を少し前へ（正 = 前）
        public float LeftArm;
        public float RightArm;

        // 頭の小さい揺れ（X = うなずき、Y = 左右）。視線の上書き中は使わない
        public Vector3 Head;
    }

    /// <summary>
    /// 既定の立ちポーズ。重心を左右の脚へゆっくり移し、遊んでいる膝を少し曲げ、胸は別に呼吸する。
    /// 状態を持たない計算だけを置く。
    /// </summary>
    public static class StandPose
    {
        // 重心移動の 1 往復（秒）。このあと同じ姿勢に戻る
        public const float Period = 6f;

        // 呼吸 1 回の秒数
        public const float BreathPeriod = 3.6f;

        // 強さ 1 のときの最大角度（度）
        public const float MaxHipRoll = 3.4f;
        public const float MaxHipYaw = 1.4f;
        public const float MaxSpineRoll = 2.2f;
        public const float MaxChestPitch = 1.5f;
        public const float MaxKnee = 11f;
        public const float MaxArm = 4f;
        public const float MaxHeadYaw = 1.6f;
        public const float MaxHeadPitch = 0.8f;

        /// <summary>
        /// 時刻（秒）と強さ（0 = 動かさない、1 = 標準）から角度を求める。
        /// </summary>
        public static StandPoseSample Evaluate(float time, float strength)
        {
            var sample = new StandPoseSample();
            float amount = Mathf.Max(0f, strength);

            // 重心: -1（左）〜 1（右）。両端で速さが 0 になるようにする
            float shift = Mathf.Sin(2f * Mathf.PI * time / Period);

            // 右へ乗せたとき腰は右へ傾け、背骨は逆へ戻す
            sample.HipRoll = shift * MaxHipRoll * amount;
            sample.HipYaw = shift * MaxHipYaw * amount;
            sample.SpineRoll = -shift * MaxSpineRoll * amount;

            // 重心を乗せた脚と逆の膝を曲げる（0〜1 に切ってから角度にする）
            sample.LeftKnee = Mathf.Max(shift, 0f) * MaxKnee * amount;
            sample.RightKnee = Mathf.Max(-shift, 0f) * MaxKnee * amount;

            // 遊脚側の腕を少し前へ出す
            sample.LeftArm = shift * MaxArm * amount;
            sample.RightArm = -shift * MaxArm * amount;

            // 呼吸は重心より速く、吸い終わりで胸が最も反る
            float breath = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * time / BreathPeriod);
            sample.ChestPitch = breath * MaxChestPitch * amount;

            // 頭は重心の半分くらいの速さで小さく振る
            sample.Head = new Vector3(
                Mathf.Sin(2f * Mathf.PI * time / (Period * 0.5f)) * MaxHeadPitch * amount,
                Mathf.Sin(2f * Mathf.PI * time / (Period * 1.5f)) * MaxHeadYaw * amount,
                0f);

            return sample;
        }
    }
}
