using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// 腕の位置合わせの計算（状態を持たない）。手首の目標位置を本人の体格からアバターの体格へ写し、
    /// 上腕・前腕の 2 関節で届く肘の位置を求める。
    /// 手首が顔に近いほど、肩ではなく顔を基準に写す（本人の手が顔の前ならアバターの手も顔の前に来る）。
    /// </summary>
    public static class ArmIk
    {
        // 顔を基準にし始める・完全に顔を基準にする手首と顔の距離（本人の m）
        public const float FaceFarDistance = 0.35f;
        public const float FaceNearDistance = 0.15f;

        // 顔の正面にある手を、顔からこの距離（本人の m）より手前に置く（推定の奥行きの誤差で顔にめり込まないように）と、
        // それを効かせる顔の正面からの横・縦のずれ（本人の m。これより外は耳・頭の横なので押し出さない）
        public const float FaceClearance = 0.06f;
        public const float FaceFrontRadius = 0.08f;

        // 顔の前の手を奥へ入れないときの、顔に重なるとみなす範囲（両目の中点からの横・縦、本人の m。
        // ピースの指は頬にかかるが、手首は顎の下にあり FaceFrontRadius の外になりやすい）と、
        // これより奥の点は本当に頭の後ろにあるものとして押し出さない（本人の m）
        public const float FaceOverlapHalfWidth = 0.11f;
        public const float FaceOverlapHalfHeight = 0.13f;
        public const float FaceDepthGlitch = 0.12f;

        // 腕を伸ばし切らない・畳み切らない余裕（長さに対する割合。肘の向きが定まらなくなるのを防ぐ）
        private const float ReachMargin = 0.001f;

        /// <summary>
        /// 手首と顔の距離（本人の m）から、顔を基準にする割合（0 = 肩基準、1 = 顔基準）を返す。
        /// </summary>
        public static float FaceWeight(float wristToFaceDistance)
        {
            // 遠いほど 0、近いほど 1 へなめらかに
            float t = Mathf.InverseLerp(FaceFarDistance, FaceNearDistance, wristToFaceDistance);
            return Mathf.SmoothStep(0f, 1f, t);
        }

        /// <summary>
        /// 顔から手首へのずれ（アバタールート基準・本人の m）を、顔の正面ならめり込まない手前まで押し出す。
        /// z が正 = アバターの前（カメラ側）。
        /// </summary>
        public static Vector3 KeepInFrontOfFace(Vector3 wristFromFace)
        {
            // 顔の正面からの横・縦のずれが小さいほど強く押し出す（頭の横・上の手はそのまま）
            float lateral = new Vector2(wristFromFace.x, wristFromFace.y).magnitude;
            float front = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FaceFrontRadius, FaceFrontRadius * 2f, lateral));

            // 手前に十分あればそのまま、足りなければ正面に近いほど手前へ出す（横へ外れるにつれてなめらかに元の奥行きへ）
            wristFromFace.z = Mathf.Lerp(wristFromFace.z, Mathf.Max(wristFromFace.z, FaceClearance), front);
            return wristFromFace;
        }

        /// <summary>
        /// <see cref="KeepInFrontOfFace"/> に加え、顔の範囲に重なる手の点（指先など）も手前に残す。
        /// 手の点はアバタールート基準の絶対位置。手首が顎の下にあっても、顔に重なる指が奥なら手首ごと前へ出す。
        /// 頭の横の手と、顔の面より明らかに奥の手は動かさない。
        /// </summary>
        public static Vector3 KeepHandInFrontOfFace(Vector3 wristFromFace, Vector3[] handPoints, int wristIndex)
        {
            Vector3 adjusted = KeepInFrontOfFace(wristFromFace);
            if (handPoints == null || wristIndex < 0 || wristIndex >= handPoints.Length)
            {
                return adjusted;
            }

            // 手首を先に動かした分だけ、指も同じだけ前へ来ている
            Vector3 wristPoint = handPoints[wristIndex];
            float shift = adjusted.z - wristFromFace.z;
            float minZ = float.PositiveInfinity;
            for (int i = 0; i < handPoints.Length; i++)
            {
                Vector3 fromFace = wristFromFace + (handPoints[i] - wristPoint);
                if (!OverlapsFace(fromFace.x, fromFace.y))
                {
                    continue;
                }

                float z = fromFace.z + shift;
                if (z < -FaceDepthGlitch)
                {
                    continue;
                }

                if (z < minZ)
                {
                    minZ = z;
                }
            }

            // 顔に重なる点のうち一番奥のものを、めり込まない手前まで出す
            if (minZ < FaceClearance)
            {
                adjusted.z += FaceClearance - minZ;
            }

            return adjusted;
        }

        /// <summary>顔（両目の中点）の正面の楕円の中なら true。</summary>
        public static bool OverlapsFace(float x, float y)
        {
            float nx = x / FaceOverlapHalfWidth;
            float ny = y / FaceOverlapHalfHeight;
            return nx * nx + ny * ny <= 1f;
        }

        /// <summary>
        /// 肩（上腕の付け根）・上腕と前腕の長さ・手首の目標・肘を曲げる向きから、肘の位置を求める（ワールド座標）。
        /// 届かない目標は腕を伸ばした先へ、近すぎる目標は畳んだ位置へ寄せる。reachable は目標に届くなら true。
        /// </summary>
        public static Vector3 SolveElbow(Vector3 shoulder, float upperLength, float lowerLength, Vector3 target,
            Vector3 bendHint, Vector3 fallbackDirection, out bool reachable)
        {
            // 肩から目標への向き（重なっていれば代わりの向き）
            Vector3 toTarget = target - shoulder;
            float distance = toTarget.magnitude;
            Vector3 direction = distance > 1e-6f ? toTarget / distance : fallbackDirection.normalized;

            // 届く範囲に収める（伸ばし切り・畳み切りの手前）
            float total = upperLength + lowerLength;
            float shortest = Mathf.Abs(upperLength - lowerLength);
            float clamped = Mathf.Clamp(distance, shortest + total * ReachMargin, total * (1f - ReachMargin));
            reachable = distance <= total && distance >= shortest;

            // 余弦定理で、肩→目標の線から肘がどれだけ離れるかを求める
            float cosine = (upperLength * upperLength + clamped * clamped - lowerLength * lowerLength)
                / (2f * upperLength * clamped);
            cosine = Mathf.Clamp(cosine, -1f, 1f);
            float sine = Mathf.Sqrt(1f - cosine * cosine);

            // 肘を曲げる向き（肩→目標の線に垂直な成分）。潰れていれば線に垂直な任意の向き
            Vector3 bend = bendHint - Vector3.Dot(bendHint, direction) * direction;
            if (bend.sqrMagnitude < 1e-8f)
            {
                bend = Vector3.Cross(direction, Vector3.up);
                if (bend.sqrMagnitude < 1e-8f)
                {
                    bend = Vector3.Cross(direction, Vector3.forward);
                }
            }

            bend.Normalize();
            return shoulder + direction * (upperLength * cosine) + bend * (upperLength * sine);
        }

        /// <summary>
        /// 肩・肘・手首の 3 点（同じ座標系）から、肘を曲げている向き（肩→手首の線に垂直な成分）を返す。
        /// </summary>
        public static Vector3 BendDirection(Vector3 elbowFromShoulder, Vector3 wristFromShoulder)
        {
            // 手首の向きが潰れていれば肘の向きそのもの
            if (wristFromShoulder.sqrMagnitude < 1e-10f)
            {
                return elbowFromShoulder;
            }

            return elbowFromShoulder - Vector3.Project(elbowFromShoulder, wristFromShoulder);
        }
    }
}
