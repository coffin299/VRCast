using System;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// OpenSeeFace（facetracker）の UDP パケット 1 顔分を解析する。リトルエンディアン、1 顔 1785 バイト。
    /// 配置: time(double) id(int) 解像度(2f) 右目・左目の開き(2f) got3D(byte) fitError(f) 四元数(4f) オイラー(3f)
    /// 位置(3f) 信頼度(68f) 2D 点(68×2f) 3D 点(70×3f) 特徴量(14f)。
    /// 座標変換は OpenSeeFace の Unity サンプルに合わせる（回転 (-y, -x, z, w)、位置 (-y, x, -z)）。
    /// </summary>
    public static class OpenSeeFacePacket
    {
        // 1 顔分のバイト数
        public const int FrameSize = 1785;

        // 各値の先頭位置
        private const int RightEyeOffset = 20;
        private const int LeftEyeOffset = 24;
        private const int Got3DOffset = 28;
        private const int QuaternionOffset = 33;
        private const int TranslationOffset = 61;
        private const int Points3DOffset = 889;
        private const int FeaturesOffset = 1729;

        // 3D 点のうち右・左の瞳と、右・左の眼球中心の番号
        private const int RightPupilPoint = 66;
        private const int LeftPupilPoint = 67;
        private const int RightEyeCenterPoint = 68;
        private const int LeftEyeCenterPoint = 69;

        // 特徴量配列内の MouthOpen の位置
        private const int MouthOpenFeature = 12;

        // 特徴量 MouthOpen（中央値 0 基準）を口の開き 0〜1 へ写す範囲
        private const float MouthClosedFeature = 0.1f;
        private const float MouthOpenedFeature = 0.8f;

        /// <summary>
        /// buffer[offset..offset+length) の先頭 1 顔を解析する。長さ不足・非有限値なら false。
        /// </summary>
        public static bool TryParse(byte[] buffer, int offset, int length, out FaceTrackingFrame frame)
        {
            frame = default;

            // 1 顔分に満たないパケットは不正
            if (buffer == null || offset < 0 || length < FrameSize || offset + length > buffer.Length)
            {
                return false;
            }

            // 頭の回転（OpenSeeFace 座標系 → Unity 座標系）
            float qx = ReadFloat(buffer, offset + QuaternionOffset);
            float qy = ReadFloat(buffer, offset + QuaternionOffset + 4);
            float qz = ReadFloat(buffer, offset + QuaternionOffset + 8);
            float qw = ReadFloat(buffer, offset + QuaternionOffset + 12);

            // 頭の位置
            float tx = ReadFloat(buffer, offset + TranslationOffset);
            float ty = ReadFloat(buffer, offset + TranslationOffset + 4);
            float tz = ReadFloat(buffer, offset + TranslationOffset + 8);

            // 目の開きと口の特徴量
            float rightEye = ReadFloat(buffer, offset + RightEyeOffset);
            float leftEye = ReadFloat(buffer, offset + LeftEyeOffset);
            float mouth = ReadFloat(buffer, offset + FeaturesOffset + MouthOpenFeature * 4);

            // 壊れた値（NaN・無限大）を含むフレームは捨てる
            if (!TrackingMath.AllFinite(qx, qy, qz, qw, tx, ty, tz, rightEye, leftEye, mouth))
            {
                return false;
            }

            // 長さ 0 の四元数は回転として使えない
            var rotation = new Quaternion(-qy, -qx, qz, qw);
            float magnitude = Mathf.Sqrt(qx * qx + qy * qy + qz * qz + qw * qw);
            if (magnitude < 1e-4f)
            {
                return false;
            }

            frame.HeadRotation = Quaternion.Normalize(rotation);
            frame.HeadPosition = new Vector3(-ty, tx, -tz);
            frame.EyeOpenRight = Mathf.Clamp01(rightEye);
            frame.EyeOpenLeft = Mathf.Clamp01(leftEye);
            frame.MouthOpen = Mathf.InverseLerp(MouthClosedFeature, MouthOpenedFeature, mouth);

            // 3D 点が推定できたフレームだけ視線を使う
            frame.HasGaze = buffer[offset + Got3DOffset] != 0 && TryReadGaze(buffer, offset, out frame.Gaze);
            return true;
        }

        private static bool TryReadGaze(byte[] buffer, int offset, out Vector2 gaze)
        {
            gaze = default;

            // 左右の目の向き（眼球中心 → 瞳）の平均。どちらかが壊れていれば使わない
            if (!TryReadEyeDirection(buffer, offset, RightPupilPoint, RightEyeCenterPoint, out Vector3 right)
                || !TryReadEyeDirection(buffer, offset, LeftPupilPoint, LeftEyeCenterPoint, out Vector3 left))
            {
                return false;
            }

            // 左右・上下の角度へ（DeltaAngle で差分を取るため ±180° の折り返しは Driver 側で吸収）
            Vector3 direction = (right + left).normalized;
            float horizontal = Mathf.Sqrt(direction.x * direction.x + direction.z * direction.z);
            gaze = new Vector2(
                Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg,
                Mathf.Atan2(direction.y, horizontal) * Mathf.Rad2Deg);
            return true;
        }

        private static bool TryReadEyeDirection(byte[] buffer, int offset, int pupil, int center, out Vector3 direction)
        {
            // 瞳と眼球中心の差（OpenSeeFace 座標系）
            Vector3 difference = ReadPoint3D(buffer, offset, pupil) - ReadPoint3D(buffer, offset, center);

            // Unity サンプルと同じ軸変換で視線方向にする（読込時の y 反転・X 反転・180° 回転をまとめたもの）
            direction = new Vector3(difference.x, difference.y, -difference.z);
            return TrackingMath.AllFinite(direction.x, direction.y, direction.z) && direction.sqrMagnitude > 1e-10f;
        }

        private static Vector3 ReadPoint3D(byte[] buffer, int offset, int point)
        {
            // 1 点は 3 つの float
            int index = offset + Points3DOffset + point * 12;
            return new Vector3(ReadFloat(buffer, index), ReadFloat(buffer, index + 4), ReadFloat(buffer, index + 8));
        }

        private static float ReadFloat(byte[] buffer, int index)
        {
            // OpenSeeFace はリトルエンディアンで送る
            if (BitConverter.IsLittleEndian)
            {
                return BitConverter.ToSingle(buffer, index);
            }

            // ビッグエンディアン環境ではバイト順を反転
            var bytes = new[] { buffer[index + 3], buffer[index + 2], buffer[index + 1], buffer[index] };
            return BitConverter.ToSingle(bytes, 0);
        }
    }
}
