using System.Globalization;
using System.Text;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// iPhone の iFacialMocap が UDP で送るテキスト 1 パケットを FaceTrackingFrame に変換する（暫定対応。実機で未確認）。
    /// 形式: "eyeBlink_L-35|jawOpen-60|...|=head#回転x,回転y,回転z,位置x,位置y,位置z|rightEye#...|leftEye#...|"
    /// - 表情は ARKit 名（_L / _R）と 0〜100 の値（新しい形式の "名前&amp;値" も受け付ける）
    /// - head は ARKit（右手系）の回転角（度）。位置は単位が不明なため使わない
    /// iFacialMocap の値は ARKit そのまま（本人基準の左右）なので、MediaPipe と同じ映像基準へ入れ替えてから ArKitFace に渡す。
    /// </summary>
    public static class IFacialMocapPacket
    {
        /// <summary>
        /// iPhone へ送る送信開始の合図（iFacialMocap はこれを受け取ると、送り主の IP アドレスへ送信を始める）。
        /// </summary>
        public const string StartCommand = "iFacialMocap_sahuasouryya9218sauhuiayeta91555dy3719";

        // 区切り文字（項目・名前と値・名前と数値列・数値列の中）
        private const char ItemSeparator = '|';
        private const char ValueSeparator = '-';
        private const char ValueSeparatorV2 = '&';
        private const char VectorSeparator = '#';
        private const char NumberSeparator = ',';

        // 頭の回転の項目名（先頭の '=' は除いて比べる）
        private const string HeadItem = "head";

        // 表情の値の最大（0〜100 を 0〜1 にする）
        private const float ValueScale = 100f;

        /// <summary>
        /// 1 パケットを読む。ARKit 名の値が 1 つも無ければ false（iFacialMocap のデータではない）。
        /// </summary>
        public static bool TryParse(byte[] buffer, int length, out FaceTrackingFrame frame)
        {
            // ASCII のテキストとして読む
            return TryParse(Encoding.ASCII.GetString(buffer, 0, length), out frame);
        }

        /// <summary>
        /// テキスト 1 パケット分を読む（TryParse(byte[], int, out) の本体）。
        /// </summary>
        public static bool TryParse(string text, out FaceTrackingFrame frame)
        {
            frame = new FaceTrackingFrame { HeadRotation = Quaternion.identity, HeadPosition = Vector3.zero };
            var personScores = new float[ArKitFace.BlendShapeNames.Length];
            int shapes = 0;

            foreach (string rawItem in text.Split(ItemSeparator))
            {
                // 空の項目（末尾の '|' 等）は飛ばす
                string item = rawItem.Trim();
                if (item.Length == 0)
                {
                    continue;
                }

                // "名前#数値列" は頭・目の向き（目は eyeLook の値から求めるので頭だけ使う）
                int vector = item.IndexOf(VectorSeparator);
                if (vector >= 0)
                {
                    if (item.Substring(0, vector).TrimStart('=') == HeadItem)
                    {
                        TryReadHead(item.Substring(vector + 1), ref frame);
                    }

                    continue;
                }

                // "名前-値" / "名前&値"（ARKit 名でないもの・読めない値は飛ばす）
                if (TryReadShape(item, out int index, out float value))
                {
                    personScores[index] = value;
                    shapes++;
                }
            }

            // 表情の値が無ければ iFacialMocap のデータではない
            if (shapes == 0)
            {
                return false;
            }

            ArKitFace.Fill(ArKitFace.FromPersonSides(personScores), ArKitRange.IPhone, ref frame);
            return true;
        }

        private static bool TryReadShape(string item, out int index, out float value)
        {
            index = -1;
            value = 0f;

            // 新しい形式の '&' を優先し、無ければ最後の '-' で名前と値に分ける（名前に '-' は含まれない）
            int separator = item.LastIndexOf(ValueSeparatorV2);
            if (separator < 0)
            {
                separator = item.LastIndexOf(ValueSeparator);
            }

            if (separator <= 0
                || !float.TryParse(item.Substring(separator + 1), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out float raw)
                || !TrackingMath.AllFinite(raw))
            {
                return false;
            }

            // ARKit 名（_L / _R の揺れはパーフェクトシンクと同じ照合）だけ使う
            index = PerfectSyncBlendShapes.Match(item.Substring(0, separator));
            value = Mathf.Clamp01(raw / ValueScale);
            return index >= 0;
        }

        private static void TryReadHead(string numbers, ref FaceTrackingFrame frame)
        {
            // 回転 x, y, z（度）が読めなければ正面のまま
            string[] parts = numbers.Split(NumberSeparator);
            if (parts.Length < 3
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                || !TrackingMath.AllFinite(x, y, z))
            {
                return;
            }

            // ARKit（右手系、Z が手前）から MediaPipe と同じ映像基準の Unity 座標へ: X 軸を反転するので
            // Y・Z 軸まわりの回転が逆向きになる（上下の向きはそのまま。下向きが正）
            frame.HeadRotation = Quaternion.Euler(x, -y, -z);
        }
    }
}
