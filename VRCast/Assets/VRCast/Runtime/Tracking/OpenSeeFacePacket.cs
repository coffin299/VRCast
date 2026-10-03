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
        private const int QuaternionOffset = 33;
        private const int TranslationOffset = 61;
        private const int FeaturesOffset = 1729;

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
            if (!AllFinite(qx, qy, qz, qw, tx, ty, tz, rightEye, leftEye, mouth))
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
            return true;
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

        private static bool AllFinite(params float[] values)
        {
            foreach (float value in values)
            {
                // 1 つでも非有限なら不正
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
