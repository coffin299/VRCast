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
    /// VMC の値は送信側アプリのアバターの動き（本人と向かい合う鏡像。Waidayo 等）なので、
    /// 鏡像 ON で送信側と同じ向きになるよう、頭の回転は左右反転し、表情の左右は入れ替えずにカメラ基準のフレームへ置く。
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

        // VRM の表情名（正規化済み: 英数字のみ小文字。VRM 0.x と 1.0 の両方）。ARKit 名が届かない値の代わりに目・口へ使う
        private const string VrmBlink = "blink";
        private static readonly HashSet<string> VrmBlinkLeftNames = new HashSet<string> { "blinkl", "blinkleft" };
        private static readonly HashSet<string> VrmBlinkRightNames = new HashSet<string> { "blinkr", "blinkright" };

        // 母音の表情名 → 位置（あいうえお。どの母音でも口は開くので、最大値を口の開きにする）
        private static readonly Dictionary<string, int> VrmVowels = new Dictionary<string, int>
        {
            { "a", 0 }, { "aa", 0 },
            { "i", 1 }, { "ih", 1 },
            { "u", 2 }, { "ou", 2 },
            { "e", 3 }, { "ee", 3 },
            { "o", 4 }, { "oh", 4 },
        };

        // 母音の種類数
        private const int VowelCount = 5;

        // 受け取った ARKit の値（送信側アバターの左右のまま、ArKitFace.BlendShapeNames 順）と、受け取ったことのある名前
        private readonly float[] _scores = new float[ArKitFace.BlendShapeNames.Length];
        private readonly bool[] _received = new bool[ArKitFace.BlendShapeNames.Length];

        // 解析用に使い回すメッセージの一覧
        private readonly List<OscMessage> _messages = new List<OscMessage>();

        // 受け取ったことのあるアドレス（送信内容の確認用に、初めてのものだけ NewAddresses へ出す）
        private readonly HashSet<string> _seenAddresses = new HashSet<string>();

        // VRM の表情名の値（0〜1）
        private float _vrmBlink;
        private float _vrmBlinkLeft;
        private float _vrmBlinkRight;
        private readonly float[] _vrmVowels = new float[VowelCount];

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
            Array.Clear(_scores, 0, _scores.Length);
            Array.Clear(_received, 0, _received.Length);
            ArKitShapeCount = 0;
            _vrmBlink = 0f;
            _vrmBlinkLeft = 0f;
            _vrmBlinkRight = 0f;
            Array.Clear(_vrmVowels, 0, _vrmVowels.Length);
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

            // VRM の表情名から求めた目の閉じ具合（送信側アバターの左右。片目ずつの値が無ければ両目の値）と口の開き
            float vrmClosedLeft = Mathf.Clamp01(Mathf.Max(_vrmBlink, _vrmBlinkLeft));
            float vrmClosedRight = Mathf.Clamp01(Mathf.Max(_vrmBlink, _vrmBlinkRight));
            float vrmMouth = VrmMouthOpen();

            // ARKit 名が届く送信元は、左右をそのまま MediaPipe と同じ処理に通す（フレームごとに新しい配列）。
            // 届かない送信元は VRM の表情名で目・口だけ動かす
            if (ArKitShapeCount > 0)
            {
                var scores = (float[])_scores.Clone();

                // 一部の ARKit 名だけ届く送信元では、届かない顎・まばたきを VRM の表情名の値で補う
                // （補わないと口・目が閉じたままになる。パーフェクトシンクの顎・まぶたにも同じ値が入る）
                if (!_received[ArKitFace.JawOpen])
                {
                    scores[ArKitFace.JawOpen] = vrmMouth;
                }

                // 送信側アバターの左目（Blink_L）は ARKit の eyeBlinkLeft と同じ側
                if (!_received[ArKitFace.EyeBlinkLeft] && !_received[ArKitFace.EyeBlinkRight])
                {
                    scores[ArKitFace.EyeBlinkLeft] = vrmClosedLeft;
                    scores[ArKitFace.EyeBlinkRight] = vrmClosedRight;
                }

                ArKitFace.Fill(scores, ArKitRange.IPhone, ref frame);
                return frame;
            }

            // VRM の表情名だけの送信元は目と口だけ（ArKitFace.Fill と同じく、フレームの左目は eyeBlinkRight 側）
            frame.EyeOpenLeft = 1f - vrmClosedRight;
            frame.EyeOpenRight = 1f - vrmClosedLeft;
            frame.MouthOpen = vrmMouth;
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

                _scores[index] = Mathf.Clamp01(value);
                return;
            }

            // VRM の表情名（目と口に使うものだけ）
            string normalized = Normalize(name);
            if (normalized == VrmBlink)
            {
                _vrmBlink = value;
            }
            else if (VrmBlinkLeftNames.Contains(normalized))
            {
                _vrmBlinkLeft = value;
            }
            else if (VrmBlinkRightNames.Contains(normalized))
            {
                _vrmBlinkRight = value;
            }
            else if (VrmVowels.TryGetValue(normalized, out int vowel))
            {
                _vrmVowels[vowel] = value;
            }
        }

        private float VrmMouthOpen()
        {
            // どの母音でも口は開くので、いちばん大きい値を口の開きにする
            float open = 0f;
            foreach (float value in _vrmVowels)
            {
                open = Mathf.Max(open, value);
            }

            return Mathf.Clamp01(open);
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

            // 送信側アバターの回転は鏡像済みなので、左右反転してカメラ基準にする（正面は FaceTrackingDriver のキャリブレーションで決まる）
            _headRotation = TrackingMath.MirrorRotation(Quaternion.Normalize(rotation));
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
    }
}
