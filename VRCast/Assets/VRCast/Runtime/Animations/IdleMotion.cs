using UnityEngine;

namespace VRCast.Animations
{
    /// <summary>
    /// 待機モーションのある時刻の角度（アバタールート基準、度）。
    /// </summary>
    public struct IdleMotionPose
    {
        // 吸ったときに胸を後ろへ反らす角度（正 = 後ろ）
        public float ChestPitch;

        // 吸ったときに肩を上げる角度（正 = 上）
        public float ShoulderRaise;

        // 上半身の左右の傾き（Z 軸まわり）とひねり（Y 軸まわり）
        public float TorsoRoll;
        public float TorsoYaw;

        // 頭のゆらぎ（X = うなずき、Y = 左右、Z = かしげ）
        public Vector3 Head;
    }

    /// <summary>
    /// 待機モーション（呼吸・体の揺れ・頭のゆらぎ）の角度を時刻から求める。状態を持たない計算だけを置く。
    /// 強さ 1 で人が立って静止しているときの程度（1〜2 度）になるようにしている。
    /// </summary>
    public static class IdleMotion
    {
        // 呼吸 1 回の秒数と、そのうち吸う時間の割合（吸うほうが速い）
        public const float BreathPeriod = 4.5f;
        public const float InhaleRatio = 0.4f;

        // 体の揺れ（左右）とひねりの周期（秒）。周期をずらして同じ動きの繰り返しに見えないようにする
        public const float SwayPeriod = 9f;
        public const float TwistPeriod = 13f;

        // 頭のゆらぎの速さ（ノイズを進める量、1 秒あたり）
        public const float HeadNoiseSpeed = 0.15f;

        // 強さ 1 のときの最大角度（度）
        public const float MaxChestPitch = 1.2f;
        public const float MaxShoulderRaise = 1.5f;
        public const float MaxTorsoRoll = 1.5f;
        public const float MaxTorsoYaw = 1f;
        public static readonly Vector3 MaxHead = new Vector3(1.2f, 2f, 0.8f);

        // 揺れに重ねる 2 つ目の波の大きさ（主の波に対する比）と周期の比
        private const float SwayDetail = 0.3f;
        private const float SwayDetailPeriodRatio = 0.37f;

        // 頭のノイズの各軸の読み取り位置（軸ごとに別の値になるよう離す）
        private const float NoiseRowX = 11.3f;
        private const float NoiseRowY = 47.9f;
        private const float NoiseRowZ = 83.1f;

        /// <summary>
        /// 時刻（秒、速さの倍率を掛けた後）と各強さ（0 = 動かさない、1 = 標準）から角度を求める。
        /// </summary>
        public static IdleMotionPose Evaluate(float time, float breathing, float sway, float head)
        {
            var pose = new IdleMotionPose();

            // 呼吸: 0（吐き切り）〜 1（吸い切り）を胸と肩へ
            float breath = BreathCurve(time / BreathPeriod) * Mathf.Max(0f, breathing);
            pose.ChestPitch = breath * MaxChestPitch;
            pose.ShoulderRaise = breath * MaxShoulderRaise;

            // 体の揺れ: ゆっくりした波に小さい波を重ね、-1〜1 に収める
            float swayStrength = Mathf.Max(0f, sway);
            pose.TorsoRoll = SwayCurve(time) * MaxTorsoRoll * swayStrength;
            pose.TorsoYaw = Mathf.Sin(2f * Mathf.PI * time / TwistPeriod) * MaxTorsoYaw * swayStrength;

            // 頭のゆらぎ: 周期の無いノイズで、規則的に見えないようにする
            float headStrength = Mathf.Max(0f, head);
            float t = time * HeadNoiseSpeed;
            pose.Head = new Vector3(
                Noise(t, NoiseRowX) * MaxHead.x,
                Noise(t, NoiseRowY) * MaxHead.y,
                Noise(t, NoiseRowZ) * MaxHead.z) * headStrength;

            return pose;
        }

        /// <summary>
        /// 呼吸の波（周期 1 の位相 → 0〜1）。InhaleRatio までで吸い、残りで吐く。両端で速さ 0 になる滑らかな形。
        /// </summary>
        public static float BreathCurve(float phase)
        {
            // 位相を 0〜1 に正規化
            float p = phase - Mathf.Floor(phase);

            // 吸う区間: 0 → 1
            if (p < InhaleRatio)
            {
                return 0.5f - 0.5f * Mathf.Cos(Mathf.PI * p / InhaleRatio);
            }

            // 吐く区間: 1 → 0
            return 0.5f + 0.5f * Mathf.Cos(Mathf.PI * (p - InhaleRatio) / (1f - InhaleRatio));
        }

        /// <summary>
        /// 体の揺れの波（-1〜1）。主の波に周期の違う小さい波を重ねて正規化する。
        /// </summary>
        public static float SwayCurve(float time)
        {
            float main = Mathf.Sin(2f * Mathf.PI * time / SwayPeriod);
            float detail = Mathf.Sin(2f * Mathf.PI * time / (SwayPeriod * SwayDetailPeriodRatio) + 1.3f);
            return (main + detail * SwayDetail) / (1f + SwayDetail);
        }

        private static float Noise(float t, float row)
        {
            // PerlinNoise は概ね 0〜1。-1〜1 へ広げ、はみ出しは切る
            return Mathf.Clamp((Mathf.PerlinNoise(t, row) - 0.5f) * 2f, -1f, 1f);
        }
    }
}
