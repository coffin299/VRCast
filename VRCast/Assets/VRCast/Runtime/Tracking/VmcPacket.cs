using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Remote;

namespace VRCast.Tracking
{
    /// <summary>
    /// VMC プロトコル（OSC over UDP。iPhone の Waidayo 等）で届く顔の値を FaceTrackingFrame にまとめる。
    /// VMC は状態を送り続ける形式なので、値は受信のたびに上書きして保持し、"/VMC/Ext/Blend/Apply" で 1 フレームを確定する。
    /// - "/VMC/Ext/Blend/Val (s 名前, f 値)": ARKit 名（パーフェクトシンク）または VRM の表情名（Blink_L / Blink_R / A 等）
    /// - "/VMC/Ext/Bone/Pos (s 名前, f px, py, pz, qx, qy, qz, qw)": Head の回転を頭の向きに使う（Unity 座標系）
    /// ARKit の左右は本人基準なので、MediaPipe と同じ映像基準へ入れ替えてから ArKitFace に渡す。
    /// </summary>
    public sealed class VmcPacket
    {
        // 使うアドレス
        private const string BlendValueAddress = "/VMC/Ext/Blend/Val";
        private const string BlendApplyAddress = "/VMC/Ext/Blend/Apply";
        private const string BonePoseAddress = "/VMC/Ext/Bone/Pos";
        private const string VmcPrefix = "/VMC/";

        // 頭のボーン名（Unity の HumanBodyBones の名前）
        private const string HeadBone = "Head";

        // Bone/Pos の float 引数の数（位置 3 + 回転 4）
        private const int BonePoseFloats = 7;

        // 診断用に覚えるアドレスの数の上限
        private const int MaxSeenAddresses = 256;

        // VRM の表情名（正規化済み: 英数字のみ小文字）。ARKit 名が届かない送信元の目・口に使う
        private const string VrmBlink = "blink";
        private const string VrmBlinkLeft = "blinkl";
        private const string VrmBlinkRight = "blinkr";
        private const string VrmMouthA = "a";

        // 本人基準の左右で受け取った ARKit の値（ArKitFace.BlendShapeNames 順）と、受け取ったことのある名前
        private readonly float[] _personScores = new float[ArKitFace.BlendShapeNames.Length];
        private readonly bool[] _received = new bool[ArKitFace.BlendShapeNames.Length];

        // 本人基準の位置 → 映像基準の位置（左右の無い名前は自分自身）
        private static readonly int[] ToVideoSide = BuildSideSwap();

        // 解析用に使い回すメッセージの一覧
        private readonly List<OscMessage> _messages = new List<OscMessage>();

        // 受け取ったことのあるアドレス（送信内容の確認用に、初めてのものだけ NewAddresses へ出す）
        private readonly HashSet<string> _seenAddresses = new HashSet<string>();

        // VRM の表情名の値（0〜1）
        private float _vrmBlink;
        private float _vrmBlinkLeft;
        private float _vrmBlinkRight;
        private float _vrmMouthA;

        // 頭の向き（Head ボーンの回転。届くまでは正面）
        private Quaternion _headRotation = Quaternion.identity;

        /// <summary>
        /// 受け取ったことのある ARKit 名の数（診断用。0 ならパーフェクトシンクの値が来ていない）。
        /// </summary>
        public int ArKitShapeCount { get; private set; }

        /// <summary>
        /// Head ボーンを受け取ったことがあれば true（診断用）。
        /// </summary>
        public bool HasHead { get; private set; }

        /// <summary>
        /// 直前の Read で初めて受け取ったアドレス（Blend/Val と Bone/Pos は名前付き。診断ログ用）。
        /// </summary>
        public List<string> NewAddresses { get; } = new List<string>();

        /// <summary>
        /// 保持している値を捨てる（入力元の切替・待ち受けのやり直し時）。
        /// </summary>
        public void Reset()
        {
            Array.Clear(_personScores, 0, _personScores.Length);
            Array.Clear(_received, 0, _received.Length);
            ArKitShapeCount = 0;
            _vrmBlink = 0f;
            _vrmBlinkLeft = 0f;
            _vrmBlinkRight = 0f;
            _vrmMouthA = 0f;
            _headRotation = Quaternion.identity;
            HasHead = false;
            _seenAddresses.Clear();
            NewAddresses.Clear();
        }

        /// <summary>
        /// 1 パケットを読む。VMC のメッセージを 1 つも含まなければ false（不正なパケット）。
        /// Apply で 1 フレーム確定したら hasFrame が true になり、frame に最新の値が入る。
        /// collectAddresses が true なら、初めて受け取ったアドレスを NewAddresses に入れる（詳細ログ用）。
        /// </summary>
        public bool Read(byte[] buffer, int length, out bool hasFrame, out FaceTrackingFrame frame,
            bool collectAddresses = false)
        {
            hasFrame = false;
            frame = default;

            // OSC として読み、VMC のアドレスだけを処理する
            _messages.Clear();
            NewAddresses.Clear();
            OscPacket.Parse(buffer, 0, length, _messages);
            bool anyVmc = false;
            foreach (OscMessage message in _messages)
            {
                // VMC 以外のアドレスは無視
                if (message.Address == null || !message.Address.StartsWith(VmcPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                anyVmc = true;

                // 求められたときだけ、初めてのアドレス（名前付きのものは名前ごと）を記録する
                // （毎メッセージの文字列の生成を避けるため。上限を設けて壊れた送信元で増え続けないように）
                if (collectAddresses && _seenAddresses.Count < MaxSeenAddresses)
                {
                    string key = message.Kind == OscValueKind.String ? $"{message.Address} {message.String}" : message.Address;
                    if (_seenAddresses.Add(key))
                    {
                        NewAddresses.Add(key);
                    }
                }

                if (Handle(message))
                {
                    hasFrame = true;
                    frame = BuildFrame();
                }
            }

            return anyVmc;
        }

        /// <summary>
        /// メッセージを 1 つ処理する。Apply（フレームの確定）なら true。
        /// </summary>
        public bool Handle(OscMessage message)
        {
            switch (message.Address)
            {
                case BlendValueAddress:
                    // 名前と値がそろっているものだけ使う
                    if (message.Kind == OscValueKind.String && message.Floats != null && message.Floats.Length > 0)
                    {
                        SetBlend(message.String, message.Floats[0]);
                    }

                    return false;
                case BonePoseAddress:
                    // 頭のボーンの回転だけ使う
                    if (message.Kind == OscValueKind.String && message.String == HeadBone
                        && message.Floats != null && message.Floats.Length >= BonePoseFloats)
                    {
                        SetHead(message.Floats);
                    }

                    return false;
                case BlendApplyAddress:
                    return true;
                default:
                    // ルート・トラッカー・カメラ等は使わない
                    return false;
            }
        }

        /// <summary>
        /// 保持している値から 1 フレームを作る。
        /// </summary>
        public FaceTrackingFrame BuildFrame()
        {
            var frame = new FaceTrackingFrame
            {
                HeadRotation = _headRotation,
                HeadPosition = Vector3.zero,
            };

            // ARKit 名が届く送信元は、映像基準の左右に並べ替えて MediaPipe と同じ処理に通す（フレームごとに新しい配列）。
            // 届かない送信元は VRM の表情名で目・口だけ動かす
            if (ArKitShapeCount > 0)
            {
                var scores = new float[_personScores.Length];
                for (int i = 0; i < scores.Length; i++)
                {
                    scores[ToVideoSide[i]] = _personScores[i];
                }

                ArKitFace.Fill(scores, ArKitRange.IPhone, ref frame);
                return frame;
            }

            // VRM の表情名だけの送信元は目と口だけ（片目ずつの値が無ければ両目の値を使う）
            frame.EyeOpenLeft = 1f - Mathf.Clamp01(Mathf.Max(_vrmBlink, _vrmBlinkLeft));
            frame.EyeOpenRight = 1f - Mathf.Clamp01(Mathf.Max(_vrmBlink, _vrmBlinkRight));
            frame.MouthOpen = Mathf.Clamp01(_vrmMouthA);
            return frame;
        }

        private void SetBlend(string name, float value)
        {
            // 壊れた値・空の名前は使わない
            if (string.IsNullOrEmpty(name) || !TrackingMath.AllFinite(value))
            {
                return;
            }

            // ARKit 名（大文字小文字・区切り記号・_L / _R の揺れはパーフェクトシンクと同じ照合）
            int index = PerfectSyncBlendShapes.Match(name);
            if (index >= 0)
            {
                // 初めて届いた名前なら数を数える（値が 0 のままの名前も数えるため、値ではなく受信の有無で判定）
                if (!_received[index])
                {
                    _received[index] = true;
                    ArKitShapeCount++;
                }

                _personScores[index] = Mathf.Clamp01(value);
                return;
            }

            // VRM の表情名（目と口に使うものだけ）
            switch (Normalize(name))
            {
                case VrmBlink:
                    _vrmBlink = value;
                    break;
                case VrmBlinkLeft:
                    _vrmBlinkLeft = value;
                    break;
                case VrmBlinkRight:
                    _vrmBlinkRight = value;
                    break;
                case VrmMouthA:
                    _vrmMouthA = value;
                    break;
            }
        }

        private void SetHead(float[] values)
        {
            // 回転（qx, qy, qz, qw）が壊れていれば使わない
            var rotation = new Quaternion(values[3], values[4], values[5], values[6]);
            if (!TrackingMath.AllFinite(rotation.x, rotation.y, rotation.z, rotation.w)
                || rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z + rotation.w * rotation.w < 1e-6f)
            {
                return;
            }

            // 送信元のアバター基準の回転をそのまま頭の向きにする（正面は FaceTrackingDriver のキャリブレーションで決まる）
            _headRotation = Quaternion.Normalize(rotation);
            HasHead = true;
        }

        private static string Normalize(string name)
        {
            // 英数字だけを小文字で残す（"Blink_L" と "blinkl" を同じにする）
            var chars = new char[name.Length];
            int length = 0;
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c))
                {
                    chars[length++] = char.ToLowerInvariant(c);
                }
            }

            return new string(chars, 0, length);
        }

        private static int[] BuildSideSwap()
        {
            // 左右の付く名前は相手の位置へ、左右の無い名前は自分自身へ（鏡像 OFF のパーフェクトシンクと同じ対応）
            var swap = new int[ArKitFace.BlendShapeNames.Length];
            for (int i = 0; i < swap.Length; i++)
            {
                swap[i] = PerfectSyncBlendShapes.SourceIndex(i, false);
            }

            return swap;
        }
    }
}
