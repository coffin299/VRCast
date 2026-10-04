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

        // 送信側と共通の BlendShape の並び（ARKit 互換 51 種、MediaPipe の _neutral は除く）
        public static readonly string[] BlendShapeNames =
        {
            "browDownLeft", "browDownRight", "browInnerUp", "browOuterUpLeft", "browOuterUpRight",
            "cheekPuff", "cheekSquintLeft", "cheekSquintRight",
            "eyeBlinkLeft", "eyeBlinkRight", "eyeLookDownLeft", "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight",
            "eyeLookOutLeft", "eyeLookOutRight", "eyeLookUpLeft", "eyeLookUpRight", "eyeSquintLeft", "eyeSquintRight",
            "eyeWideLeft", "eyeWideRight",
            "jawForward", "jawLeft", "jawOpen", "jawRight",
            "mouthClose", "mouthDimpleLeft", "mouthDimpleRight", "mouthFrownLeft", "mouthFrownRight", "mouthFunnel",
            "mouthLeft", "mouthLowerDownLeft", "mouthLowerDownRight", "mouthPressLeft", "mouthPressRight",
            "mouthPucker", "mouthRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower", "mouthShrugUpper",
            "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft", "mouthStretchRight",
            "mouthUpperUpLeft", "mouthUpperUpRight", "noseSneerLeft", "noseSneerRight",
        };

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

        // まばたきの値を目の開きへ写す範囲（この値以下で完全に開き、以上で完全に閉じる）
        private const float BlinkOpenScore = 0.15f;
        private const float BlinkClosedScore = 0.65f;

        // 顎の開きの値を口の開き 0〜1 へ写す範囲
        private const float JawClosedScore = 0.05f;
        private const float JawOpenedScore = 0.6f;

        // 視線の BlendShape 1.0 あたりの角度（度）
        private const float GazeDegrees = 30f;

        // 腕の点を使う可視度の下限（画面外の推定値で腕が暴れないように）
        private const float MinVisibility = 0.5f;

        // 使う BlendShape の位置（BlendShapeNames 内）
        private static readonly int EyeBlinkLeft = IndexOf("eyeBlinkLeft");
        private static readonly int EyeBlinkRight = IndexOf("eyeBlinkRight");
        private static readonly int EyeLookDownLeft = IndexOf("eyeLookDownLeft");
        private static readonly int EyeLookDownRight = IndexOf("eyeLookDownRight");
        private static readonly int EyeLookInLeft = IndexOf("eyeLookInLeft");
        private static readonly int EyeLookInRight = IndexOf("eyeLookInRight");
        private static readonly int EyeLookOutLeft = IndexOf("eyeLookOutLeft");
        private static readonly int EyeLookOutRight = IndexOf("eyeLookOutRight");
        private static readonly int EyeLookUpLeft = IndexOf("eyeLookUpLeft");
        private static readonly int EyeLookUpRight = IndexOf("eyeLookUpRight");
        private static readonly int JawOpen = IndexOf("jawOpen");

        // 表情の合成に使う BlendShape の位置（左右の組。左右の無い browInnerUp は 2 回並べて重みをそろえる）
        private static readonly int[] SmileShapes =
        {
            IndexOf("mouthSmileLeft"), IndexOf("mouthSmileRight"),
            IndexOf("cheekSquintLeft"), IndexOf("cheekSquintRight"),
        };

        private static readonly int[] SurpriseShapes =
        {
            IndexOf("browInnerUp"), IndexOf("browInnerUp"), IndexOf("eyeWideLeft"), IndexOf("eyeWideRight"),
        };

        private static readonly int[] AngryShapes =
        {
            IndexOf("browDownLeft"), IndexOf("browDownRight"), IndexOf("noseSneerLeft"), IndexOf("noseSneerRight"),
        };

        private static readonly int[] SadShapes =
        {
            IndexOf("mouthFrownLeft"), IndexOf("mouthFrownRight"), IndexOf("browInnerUp"), IndexOf("browInnerUp"),
        };

        /// <summary>
        /// 送信される JSON の形（フィールド名は送信側と一致させる。欠けた配列は null）。
        /// </summary>
        [Serializable]
        public class Message
        {
            public int v;

            // 顔: 変換行列（4×4 行優先）と BlendShapeNames 順のスコア
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

            // 手の左右は体のラベルに合わせて送られてくるため、腕と同じく入れ替える
            body.Left.Hand = ReadHand(message.rightHand);
            body.Right.Hand = ReadHand(message.leftHand);
            return true;
        }

        private static bool TryReadFace(Message message, out FaceTrackingFrame face)
        {
            face = default;
            float[] m = message.matrix;
            float[] scores = message.blendshapes;

            // 要素数の不一致・非有限値を含む顔は使わない
            bool sized = m != null && m.Length == MatrixSize && scores != null && scores.Length == BlendShapeNames.Length;
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

            // まばたき（1 = 閉じ）を目の開き（1 = 開き）へ。MediaPipe の eyeBlink の左右は映像上の左右（本人とは逆）
            face.EyeOpenLeft = 1f - Mathf.InverseLerp(BlinkOpenScore, BlinkClosedScore, scores[EyeBlinkRight]);
            face.EyeOpenRight = 1f - Mathf.InverseLerp(BlinkOpenScore, BlinkClosedScore, scores[EyeBlinkLeft]);
            face.MouthOpen = Mathf.InverseLerp(JawClosedScore, JawOpenedScore, scores[JawOpen]);

            // 視線: 本人の右向き = 左目の内寄せ + 右目の外寄せ、上向き = 両目の上 − 下（左右の平均）
            float right = (scores[EyeLookInLeft] + scores[EyeLookOutRight]
                - scores[EyeLookOutLeft] - scores[EyeLookInRight]) * 0.5f;
            float upward = (scores[EyeLookUpLeft] + scores[EyeLookUpRight]
                - scores[EyeLookDownLeft] - scores[EyeLookDownRight]) * 0.5f;
            face.Gaze = new Vector2(right * GazeDegrees, upward * GazeDegrees);
            face.HasGaze = true;

            // 表情: 口・頬・眉・目の BlendShape の平均（口だけに頼らず、発話中の誤判定を減らす）
            face.Expression = new ExpressionScores
            {
                Smile = Average(scores, SmileShapes),
                Surprise = Average(scores, SurpriseShapes),
                Angry = Average(scores, AngryShapes),
                Sad = Average(scores, SadShapes),
            };
            face.HasExpression = true;
            return true;
        }

        private static float Average(float[] scores, int[] indices)
        {
            // 指定位置の値の平均を 0〜1 に収める
            float sum = 0f;
            foreach (int index in indices)
            {
                sum += scores[index];
            }

            return Mathf.Clamp01(sum / indices.Length);
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

            // MediaPipe の左右ラベルは映像上の左右（本人とは逆）なので、入れ替えて本人の左腕・右腕にする
            body.Left = ReadArm(points, visibility, RightShoulder, RightElbow, RightWrist);
            body.Right = ReadArm(points, visibility, LeftShoulder, LeftElbow, LeftWrist);
        }

        private static ArmTrackingData ReadArm(float[] points, float[] visibility, int shoulder, int elbow, int wrist)
        {
            // 肩・肘・手首がすべて映っているときだけ腕の向きを使う
            return new ArmTrackingData
            {
                HasArm = visibility[shoulder] >= MinVisibility && visibility[elbow] >= MinVisibility
                    && visibility[wrist] >= MinVisibility,
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
            // MediaPipe の world 座標（x = 映像の左、y = 下）をカメラ基準の Unity 座標（x = 映像の右、y = 上）へ
            int index = point * 3;
            return new Vector3(-values[index], -values[index + 1], values[index + 2]);
        }

        private static int IndexOf(string name)
        {
            // 並びに無い名前は実装ミスなので即座に気付けるよう例外にする
            int index = Array.IndexOf(BlendShapeNames, name);
            if (index < 0)
            {
                throw new InvalidOperationException("Unknown blend shape: " + name);
            }

            return index;
        }
    }
}
