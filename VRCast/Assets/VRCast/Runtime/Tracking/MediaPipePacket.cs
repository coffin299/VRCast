using System;
using System.Text;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// 同梱の MediaPipe トラッカー（Tools/MediaPipeTracker/vrcast_tracker.py）が送る JSON（UTF-8、1 パケット 1 フレーム）を解析する。
    /// 座標変換:
    /// - 頭の変換行列は MediaPipe の右手系（x = 映像の右、y = 上、z = カメラ側、単位 cm）。
    ///   回転は z 反転（カメラ基準の Unity 座標）とアバター正面への 180° 回転をまとめて (x, -y, -z, w)、位置は (x, y, -z)。
    /// - 腕・手の点は MediaPipe の world 座標（x = 映像の右、y = 下、z = 奥）→ カメラ基準の Unity 座標 (x, -y, z)。
    /// </summary>
    public static class MediaPipePacket
    {
        // 送信側と一致させるプロトコル番号（互換性のない変更時に両方で増やす）
        public const int ProtocolVersion = 1;

        // 腕の点の数と並び（MediaPipe ラベルの左肩・右肩・左肘・右肘・左手首・右手首。本人の左右とは逆）
        public const int ArmPointCount = 6;
        public const int LeftShoulder = 0;
        public const int RightShoulder = 1;
        public const int LeftElbow = 2;
        public const int RightElbow = 3;
        public const int LeftWrist = 4;
        public const int RightWrist = 5;

        // 手の点の数
        public const int HandPointCount = 21;

        // 変換行列の要素数（4×4、行優先）
        private const int MatrixSize = 16;

        // 頭の位置の単位変換（cm → dm。OpenSeeFace と同程度の値にして Body lean の強さを共通にする）
        private const float PositionScale = 0.1f;

        // 腕の点を使う可視度の下限（画面外の推定値で腕が暴れないように）
        private const float MinVisibility = 0.5f;

        /// <summary>
        /// 送信される JSON の形（フィールド名は送信側と一致させる。欠けた配列は null）。
        /// </summary>
        [Serializable]
        public class Message
        {
            public int v;

            // 顔: 変換行列（4×4 行優先）と ArKitFace.BlendShapeNames 順のスコア
            public bool face;
            public float[] matrix;
            public float[] blendshapes;

            // 腕: ArmPointCount 点の world 座標（x, y, z の繰り返し）と可視度
            public bool pose;
            public float[] arms;
            public float[] visibility;

            // 手: 本人の左手・右手の 21 点の world 座標（映っていなければ空）
            public float[] leftHand;
            public float[] rightHand;
        }

        /// <summary>
        /// buffer[0..length) の UTF-8 JSON を解析する。
        /// </summary>
        public static bool TryParse(
            byte[] buffer, int length, out bool hasFace, out FaceTrackingFrame face, out BodyTrackingFrame body)
        {
            // 範囲外の長さは不正
            if (buffer == null || length <= 0 || length > buffer.Length)
            {
                hasFace = false;
                face = default;
                body = default;
                return false;
            }

            return TryParse(Encoding.UTF8.GetString(buffer, 0, length), out hasFace, out face, out body);
        }

        /// <summary>
        /// JSON 1 フレームを解析する。JSON 不正・プロトコル不一致なら false。
        /// 顔・腕・手は個別に検証し、壊れた部分だけ「無し」扱いにする（hasFace / HasArm / HasHand）。
        /// </summary>
        public static bool TryParse(string json, out bool hasFace, out FaceTrackingFrame face, out BodyTrackingFrame body)
        {
            hasFace = false;
            face = default;
            body = default;

            // 空文字は不正
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            Message message;
            try
            {
                message = JsonUtility.FromJson<Message>(json);
            }
            catch (ArgumentException)
            {
                // JSON として読めない
                return false;
            }

            // 別バージョンの送信側とは値の並びが違うため使わない
            if (message == null || message.v != ProtocolVersion)
            {
                return false;
            }

            // 顔・腕・手をそれぞれ読み取る
            hasFace = message.face && TryReadFace(message, out face);
            if (message.pose)
            {
                ReadArms(message.arms, message.visibility, ref body);
            }

            // 手の左右は体のラベル（本人基準）に合わせて送られてくるため、そのまま使う
            body.Left.Hand = ReadHand(message.leftHand);
            body.Right.Hand = ReadHand(message.rightHand);
            return true;
        }

        private static bool TryReadFace(Message message, out FaceTrackingFrame face)
        {
            face = default;
            float[] m = message.matrix;
            float[] scores = message.blendshapes;

            // 要素数の不一致・非有限値を含む顔は使わない
            bool sized = m != null && m.Length == MatrixSize && scores != null && scores.Length == ArKitFace.BlendShapeNames.Length;
            if (!sized || !TrackingMath.AllFinite(m) || !TrackingMath.AllFinite(scores))
            {
                return false;
            }

            // 回転部分の Z 列（顔の正面）と Y 列（顔の上）。潰れた行列は使えない
            var forward = new Vector3(m[2], m[6], m[10]);
            var up = new Vector3(m[1], m[5], m[9]);
            if (forward.sqrMagnitude < 1e-8f || up.sqrMagnitude < 1e-8f)
            {
                return false;
            }

            // 行列の回転（右手系のまま四元数へ）を Unity のアバター基準へ変換
            Quaternion q = Quaternion.LookRotation(forward, up);
            face.HeadRotation = Quaternion.Normalize(new Quaternion(q.x, -q.y, -q.z, q.w));

            // 平行移動（cm）をカメラ基準の Unity 座標（dm）へ
            face.HeadPosition = new Vector3(m[3], m[7], -m[11]) * PositionScale;

            // 目・口・視線・表情とパーフェクトシンク用の値（MediaPipe の左右は映像基準のまま。受信ごとに新しい配列のため複製しない）
            ArKitFace.Fill(scores, ArKitRange.MediaPipe, ref face);
            return true;
        }

        private static void ReadArms(float[] points, float[] visibility, ref BodyTrackingFrame body)
        {
            // 要素数の不一致・非有限値は腕なし
            bool sized = points != null && points.Length == ArmPointCount * 3
                && visibility != null && visibility.Length == ArmPointCount;
            if (!sized || !TrackingMath.AllFinite(points) || !TrackingMath.AllFinite(visibility))
            {
                return;
            }

            // MediaPipe の体の左右ラベルは本人基準なので、そのまま本人の左腕・右腕にする
            body.Left = ReadArm(points, visibility, LeftShoulder, LeftElbow, LeftWrist);
            body.Right = ReadArm(points, visibility, RightShoulder, RightElbow, RightWrist);
        }

        private static ArmTrackingData ReadArm(float[] points, float[] visibility, int shoulder, int elbow, int wrist)
        {
            // 肩・肘・手首がすべて映っているときだけ腕の向きを使う
            return new ArmTrackingData
            {
                HasArm = visibility[shoulder] >= MinVisibility && visibility[elbow] >= MinVisibility
                    && visibility[wrist] >= MinVisibility,
                HasShoulder = visibility[shoulder] >= MinVisibility,
                Shoulder = ToUnity(points, shoulder),
                Elbow = ToUnity(points, elbow),
                Wrist = ToUnity(points, wrist),
            };
        }

        private static Vector3[] ReadHand(float[] values)
        {
            // 21 点そろった有限値のときだけ手あり（空配列 = 映っていない）
            if (values == null || values.Length != HandPointCount * 3 || !TrackingMath.AllFinite(values))
            {
                return null;
            }

            var hand = new Vector3[HandPointCount];
            for (int i = 0; i < HandPointCount; i++)
            {
                hand[i] = ToUnity(values, i);
            }

            return hand;
        }

        private static Vector3 ToUnity(float[] values, int point)
        {
            // MediaPipe の world 座標（x = 映像の右、y = 下）をカメラ基準の Unity 座標（x = 映像の右、y = 上）へ
            int index = point * 3;
            return new Vector3(values[index], -values[index + 1], values[index + 2]);
        }
    }
}
