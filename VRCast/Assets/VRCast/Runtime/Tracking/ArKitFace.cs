using System;
using UnityEngine;

namespace VRCast.Tracking
{
    /// <summary>
    /// ARKit 互換の BlendShape の値の範囲（入力元ごとに、目を閉じ切った・口を開け切ったときの値が違うため）。
    /// </summary>
    [Serializable]
    public struct ArKitRange
    {
        // まばたきの値を目の閉じ具合へ写す範囲（BlinkOpen 以下で完全に開き、BlinkClosed 以上で完全に閉じる）
        public float BlinkOpen;
        public float BlinkClosed;

        // 顎の開きの値を口の開き 0〜1 へ写す範囲
        public float JawClosed;
        public float JawOpened;

        /// <summary>
        /// MediaPipe（目を閉じても 1 まで上がらず、口も 0.6 前後で開き切る）。
        /// </summary>
        public static readonly ArKitRange MediaPipe = new ArKitRange
        {
            BlinkOpen = 0.15f,
            BlinkClosed = 0.65f,
            JawClosed = 0.05f,
            JawOpened = 0.6f,
        };

        /// <summary>
        /// iPhone の ARKit（TrueDepth カメラ。閉じ切ると 1 近くまで上がる）。
        /// </summary>
        public static readonly ArKitRange IPhone = new ArKitRange
        {
            BlinkOpen = 0.1f,
            BlinkClosed = 0.8f,
            JawClosed = 0.03f,
            JawOpened = 0.7f,
        };

        /// <summary>
        /// まばたきの値（1 = 閉じ）を閉じ具合 0〜1 へ写す。
        /// </summary>
        public float BlinkToClosed(float blink)
        {
            return Mathf.InverseLerp(BlinkOpen, BlinkClosed, blink);
        }
    }

    /// <summary>
    /// ARKit 互換の BlendShape の値（MediaPipe・iPhone 共通）から、顔の向き以外の FaceTrackingFrame の項目を作る。
    /// 値の並びは BlendShapeNames 順、左右は映像基準（本人とは逆）にそろえてから渡す。
    /// </summary>
    public static class ArKitFace
    {
        // 共通の BlendShape の並び（ARKit 互換 51 種。MediaPipe の _neutral・iPhone の tongueOut は除く）。
        // MediaPipe トラッカーの送信側と並びを一致させる
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

        // 視線の BlendShape 1.0 あたりの角度（度）
        private const float GazeDegrees = 30f;

        // ウインク: 左右のまばたきの差をウインクの強さ 0〜1 へ写す範囲と、閉じた側の目の閉じ具合の範囲
        private const float WinkGapMin = 0.25f;
        private const float WinkGapFull = 0.6f;
        private const float WinkClosedMin = 0.35f;
        private const float WinkClosedFull = 0.6f;

        // ジト目: 両目の閉じ具合（平均）が半分ほどの範囲で強くなり、閉じきると弱くなる
        private const float SquintStart = 0.2f;
        private const float SquintFull = 0.4f;
        private const float SquintFadeStart = 0.6f;
        private const float SquintFadeEnd = 0.8f;

        // ジト目: 下を見ているとまぶたも下がるため、下向きの視線の値でこの範囲だけ弱める
        private const float LookDownFadeStart = 0.3f;
        private const float LookDownFadeEnd = 0.6f;

        // ふくれっ面（口をとがらせる）: mouthPucker を強さ 0〜1 へ写す範囲（「う」の口より強くとがらせたときに届く）
        private const float PoutStart = 0.35f;
        private const float PoutFull = 0.8f;

        // 使う BlendShape の位置（BlendShapeNames 内）
        public static readonly int EyeBlinkLeft = IndexOf("eyeBlinkLeft");
        public static readonly int EyeBlinkRight = IndexOf("eyeBlinkRight");
        public static readonly int JawOpen = IndexOf("jawOpen");
        private static readonly int EyeLookDownLeft = IndexOf("eyeLookDownLeft");
        private static readonly int EyeLookDownRight = IndexOf("eyeLookDownRight");
        private static readonly int EyeLookInLeft = IndexOf("eyeLookInLeft");
        private static readonly int EyeLookInRight = IndexOf("eyeLookInRight");
        private static readonly int EyeLookOutLeft = IndexOf("eyeLookOutLeft");
        private static readonly int EyeLookOutRight = IndexOf("eyeLookOutRight");
        private static readonly int EyeLookUpLeft = IndexOf("eyeLookUpLeft");
        private static readonly int EyeLookUpRight = IndexOf("eyeLookUpRight");

        // 表情の合成に使う BlendShape の位置（MediaPipe でよく動くものだけ。頬・鼻・目の見開きはほぼ 0 のままで平均を薄めるため使わない）
        private static readonly int[] SmileShapes = { IndexOf("mouthSmileLeft"), IndexOf("mouthSmileRight") };
        private static readonly int[] AngryShapes = { IndexOf("browDownLeft"), IndexOf("browDownRight") };
        private static readonly int[] FrownShapes = { IndexOf("mouthFrownLeft"), IndexOf("mouthFrownRight") };
        private static readonly int[] BrowOuterUpShapes = { IndexOf("browOuterUpLeft"), IndexOf("browOuterUpRight") };
        private static readonly int BrowInnerUp = IndexOf("browInnerUp");
        private static readonly int MouthPucker = IndexOf("mouthPucker");

        /// <summary>
        /// 目・口・視線・表情の強さ・パーフェクトシンク用の値を face に書く（顔の向き・位置は書かない）。
        /// scores は BlendShapeNames と同じ要素数で、そのまま face.BlendShapes として渡す（複製しない）。
        /// </summary>
        public static void Fill(float[] scores, ArKitRange range, ref FaceTrackingFrame face)
        {
            // まばたき（1 = 閉じ）を目の開き（1 = 開き）へ。左右は映像上の左右（本人とは逆）
            face.EyeOpenLeft = 1f - range.BlinkToClosed(scores[EyeBlinkRight]);
            face.EyeOpenRight = 1f - range.BlinkToClosed(scores[EyeBlinkLeft]);
            face.MouthOpen = Mathf.InverseLerp(range.JawClosed, range.JawOpened, scores[JawOpen]);

            // 視線: 本人の右向き = 左目の内寄せ + 右目の外寄せ、上向き = 両目の上 − 下（左右の平均）
            float right = (scores[EyeLookInLeft] + scores[EyeLookOutRight]
                - scores[EyeLookOutLeft] - scores[EyeLookInRight]) * 0.5f;
            float upward = (scores[EyeLookUpLeft] + scores[EyeLookUpRight]
                - scores[EyeLookDownLeft] - scores[EyeLookDownRight]) * 0.5f;
            face.Gaze = new Vector2(right * GazeDegrees, upward * GazeDegrees);
            face.HasGaze = true;

            // 表情: 笑顔 = 口角、怒り = 眉を下げる、驚き = 眉全体（内側と外側）を上げる、
            // 悲しみ = 口角を下げる + 眉の内側だけを上げる（外側も上がる驚きと区別する）、
            // ウインク = 片目だけ閉じる、ジト目 = 両目を半分閉じる、ふくれっ面 = 口をとがらせる
            float browInner = scores[BrowInnerUp];
            float browOuter = Average(scores, BrowOuterUpShapes);
            face.Expression = new ExpressionScores
            {
                Smile = Average(scores, SmileShapes),
                Surprise = (browInner + browOuter) * 0.5f,
                Angry = Average(scores, AngryShapes),
                Sad = Mathf.Clamp01(Average(scores, FrownShapes) + Mathf.Max(0f, browInner - browOuter)),
                Wink = WinkScore(scores[EyeBlinkLeft], scores[EyeBlinkRight]),
                Squint = SquintScore(scores[EyeBlinkLeft], scores[EyeBlinkRight],
                    (scores[EyeLookDownLeft] + scores[EyeLookDownRight]) * 0.5f),
                Pout = Mathf.InverseLerp(PoutStart, PoutFull, scores[MouthPucker]),
            };
            face.HasExpression = true;

            // パーフェクトシンク用に生の値と、まばたきを写す範囲も渡す
            face.BlendShapes = scores;
            face.BlendShapeRange = range;
        }

        /// <summary>
        /// ウインクの強さ（片目だけを閉じているほど 1 に近い）。
        /// </summary>
        public static float WinkScore(float blinkLeft, float blinkRight)
        {
            // 左右の差が大きく、閉じた側がしっかり閉じているときだけ強くする
            float gap = Mathf.InverseLerp(WinkGapMin, WinkGapFull, Mathf.Abs(blinkLeft - blinkRight));
            float closed = Mathf.InverseLerp(WinkClosedMin, WinkClosedFull, Mathf.Max(blinkLeft, blinkRight));
            return gap * closed;
        }

        /// <summary>
        /// ジト目の強さ（両目を半分ほど閉じているほど 1 に近い。閉じきり・片目だけ・下向きの視線では弱い）。
        /// </summary>
        public static float SquintScore(float blinkLeft, float blinkRight, float lookDown)
        {
            // 両目の閉じ具合の平均が半分ほどの間だけ強くする（閉じきったらまばたき・目を閉じた扱い）
            float closed = (blinkLeft + blinkRight) * 0.5f;
            float half = Mathf.InverseLerp(SquintStart, SquintFull, closed)
                * (1f - Mathf.InverseLerp(SquintFadeStart, SquintFadeEnd, closed));

            // 片目だけ閉じているならウインク側なので弱める
            float symmetric = 1f - Mathf.InverseLerp(WinkGapMin, WinkGapFull, Mathf.Abs(blinkLeft - blinkRight));

            // 下を見てまぶたが下がっているだけなら弱める
            float notLookingDown = 1f - Mathf.InverseLerp(LookDownFadeStart, LookDownFadeEnd, lookDown);
            return half * symmetric * notLookingDown;
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

        /// <summary>
        /// 名前の位置（BlendShapeNames 内）。並びに無い名前は実装ミスなので例外にする。
        /// </summary>
        public static int IndexOf(string name)
        {
            // 並びに無い名前は即座に気付けるよう例外にする
            int index = Array.IndexOf(BlendShapeNames, name);
            if (index < 0)
            {
                throw new InvalidOperationException("Unknown blend shape: " + name);
            }

            return index;
        }
    }
}
