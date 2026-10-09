using System;

namespace VRCast.Core
{
    /// <summary>
    /// アバターごとに覚える見た目の設定（ライト・アバターの明るさ・輪郭線の太さ・待機ポーズ・向き）。
    /// 範囲の補正は適用後の AppSettings.Sanitize が行う。
    /// </summary>
    [Serializable]
    public struct AvatarLook
    {
        public float lightIntensity;
        public float lightYaw;
        public float lightPitch;
        public float lightTemperature;
        public float ambientIntensity;
        public float avatarBrightness;
        public float poseArmDown;
        public float poseElbowBend;
        public float avatarYaw;

        // 輪郭線の太さ。以前の版の記録には無く 0 と読まれるため、hasOutlineWidth が false なら既定（1）として扱う
        public bool hasOutlineWidth;
        public float outlineWidth;

        /// <summary>
        /// 現在の設定から取り出す。
        /// </summary>
        public static AvatarLook From(AppSettings settings)
        {
            return new AvatarLook
            {
                lightIntensity = settings.lightIntensity,
                lightYaw = settings.lightYaw,
                lightPitch = settings.lightPitch,
                lightTemperature = settings.lightTemperature,
                ambientIntensity = settings.ambientIntensity,
                avatarBrightness = settings.avatarBrightness,
                poseArmDown = settings.poseArmDown,
                poseElbowBend = settings.poseElbowBend,
                avatarYaw = settings.avatarYaw,
                hasOutlineWidth = true,
                outlineWidth = settings.outlineWidth,
            };
        }

        /// <summary>
        /// 設定へ書き戻し、範囲外の値を補正する（反映は呼び出し側）。
        /// </summary>
        public void ApplyTo(AppSettings settings)
        {
            settings.lightIntensity = lightIntensity;
            settings.lightYaw = lightYaw;
            settings.lightPitch = lightPitch;
            settings.lightTemperature = lightTemperature;
            settings.ambientIntensity = ambientIntensity;
            settings.avatarBrightness = avatarBrightness;
            settings.poseArmDown = poseArmDown;
            settings.poseElbowBend = poseElbowBend;
            settings.avatarYaw = avatarYaw;
            // 記録の無い以前の版の見た目はマテリアルのままの太さにする
            settings.outlineWidth = hasOutlineWidth ? outlineWidth : 1f;
            settings.Sanitize();
        }

        /// <summary>
        /// 全要素が有限値なら true（壊れた設定ファイルの値を使わないため）。
        /// </summary>
        public bool IsFinite => IsFiniteValue(lightIntensity) && IsFiniteValue(lightYaw) && IsFiniteValue(lightPitch)
            && IsFiniteValue(lightTemperature) && IsFiniteValue(ambientIntensity) && IsFiniteValue(avatarBrightness)
            && IsFiniteValue(poseArmDown) && IsFiniteValue(poseElbowBend) && IsFiniteValue(avatarYaw)
            && IsFiniteValue(outlineWidth);

        /// <summary>
        /// 全要素が同じなら true（変化したときだけ記録するための比較）。
        /// </summary>
        public bool SameAs(AvatarLook other)
        {
            return lightIntensity == other.lightIntensity && lightYaw == other.lightYaw
                && lightPitch == other.lightPitch && lightTemperature == other.lightTemperature
                && ambientIntensity == other.ambientIntensity && avatarBrightness == other.avatarBrightness
                && poseArmDown == other.poseArmDown && poseElbowBend == other.poseElbowBend
                && avatarYaw == other.avatarYaw && hasOutlineWidth == other.hasOutlineWidth
                && outlineWidth == other.outlineWidth;
        }

        private static bool IsFiniteValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
