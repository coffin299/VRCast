using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;

namespace VRCast.Tracking
{
    /// <summary>
    /// パーフェクトシンク: MediaPipe の ARKit 互換 BlendShape の値を、アバターの同名 BlendShape へそのまま書き込む。
    /// 名前は大文字小文字・区切り記号・FBX の接頭辞（"blendShape1." 等）を無視し、末尾の Left / Right は L / R も受け付ける。
    /// 同名の BlendShape が複数のメッシュ（顔・歯・舌など）にあれば全部動かす。
    /// 表情プリセット等の値を壊さないよう BlendShapeOverlay で「元の値と大きい方」を書く。
    /// </summary>
    public sealed class PerfectSyncBlendShapes
    {
        /// <summary>
        /// パーフェクトシンク対応とみなす、見つかった BlendShape の種類数の下限
        /// （まばたき用の eyeBlinkLeft / Right だけを持つアバターを対象にしないため）。
        /// </summary>
        public const int MinMatchedShapes = 20;

        // まばたきの値を閉じ具合へ写す範囲（MediaPipe は目を閉じても 1 まで上がらないため、他の値と違い広げる）
        private const float BlinkOpenScore = 0.15f;
        private const float BlinkClosedScore = 0.65f;

        // 値の追従速度（大きいほど速い、1 秒あたり。検出の細かな揺れで顔が震えないように）
        private const float Smoothing = 30f;

        // ARKit 名（正規化済み）→ BlendShapeNames 内の位置
        private static readonly Dictionary<string, int> NameToIndex = BuildNameTable();

        // 左右を入れ替えた相手の位置（左右の無い名前は自分自身）
        private static readonly int[] Opposite = BuildOpposites();

        // まばたき・顎の開きの位置（既存のまばたき・口パクと二重に動かさないための判定と、値の補正に使う）
        private static readonly int EyeBlinkLeft = Array.IndexOf(MediaPipePacket.BlendShapeNames, "eyeBlinkLeft");
        private static readonly int EyeBlinkRight = Array.IndexOf(MediaPipePacket.BlendShapeNames, "eyeBlinkRight");
        private static readonly int JawOpen = Array.IndexOf(MediaPipePacket.BlendShapeNames, "jawOpen");

        // ARKit の各名前に対応するアバター側の書き込み先（無ければ null）
        private readonly List<BlendShapeOverlay>[] _targets;

        // 平滑化済みの値（0〜1、アバター側の左右）
        private readonly float[] _current;

        // 書き込み中なら true（解除時に一度だけ元の値へ戻すため）
        private bool _active;

        private PerfectSyncBlendShapes(List<BlendShapeOverlay>[] targets, int matched)
        {
            _targets = targets;
            _current = new float[targets.Length];
            MatchedCount = matched;
        }

        /// <summary>
        /// 見つかった ARKit 名の種類数（0〜51）。
        /// </summary>
        public int MatchedCount { get; }

        /// <summary>
        /// パーフェクトシンクとして使えるなら true（MinMatchedShapes 種類以上ある）。
        /// </summary>
        public bool IsAvailable => MatchedCount >= MinMatchedShapes;

        /// <summary>
        /// 有効にする条件を満たすなら true（anyShape なら 1 種類でもあれば、そうでなければ IsAvailable と同じ）。
        /// </summary>
        public bool IsAvailableFor(bool anyShape)
        {
            // 条件に応じて必要な種類数を切り替える
            return MatchedCount >= (anyShape ? 1 : MinMatchedShapes);
        }

        /// <summary>
        /// まばたき（eyeBlinkLeft / Right の両方）を動かせるなら true（その間は既存のまばたきを開いたままにする）。
        /// </summary>
        public bool DrivesBlink => _targets[EyeBlinkLeft] != null && _targets[EyeBlinkRight] != null;

        /// <summary>
        /// 口の開き（jawOpen）を動かせるなら true（その間はカメラによる口パクを止める）。
        /// </summary>
        public bool DrivesJaw => _targets[JawOpen] != null;

        /// <summary>
        /// root 以下の全 SkinnedMeshRenderer から ARKit 名の BlendShape を探す。
        /// </summary>
        public static PerfectSyncBlendShapes Create(Transform root)
        {
            var targets = new List<BlendShapeOverlay>[MediaPipePacket.BlendShapeNames.Length];
            int matched = 0;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                // メッシュの無いレンダラーは対象外
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    // ARKit 名でなければ対象外
                    int index = Match(mesh.GetBlendShapeName(shape));
                    if (index < 0)
                    {
                        continue;
                    }

                    // 初めて見つかった名前なら種類数を数える
                    if (targets[index] == null)
                    {
                        targets[index] = new List<BlendShapeOverlay>();
                        matched++;
                    }

                    targets[index].Add(BlendShapeOverlay.Create(renderer, shape));
                }
            }

            return new PerfectSyncBlendShapes(targets, matched);
        }

        /// <summary>
        /// BlendShape 名に対応する ARKit 名の位置（BlendShapeNames 内）。対応しなければ -1。
        /// </summary>
        public static int Match(string blendShapeName)
        {
            // 空の名前は対象外
            if (string.IsNullOrEmpty(blendShapeName))
            {
                return -1;
            }

            // FBX の取り込みで付く "blendShape1." のような接頭辞を外してから正規化
            int dot = blendShapeName.LastIndexOf('.');
            string name = Normalize(dot >= 0 ? blendShapeName.Substring(dot + 1) : blendShapeName);
            return NameToIndex.TryGetValue(name, out int index) ? index : -1;
        }

        /// <summary>
        /// アバター側の ARKit 名の位置に対応する、受信値（映像基準の左右）の位置。
        /// MediaPipe の左右は映像上の左右（本人とは逆）なので、鏡像 OFF（本人の左 = アバターの左）では入れ替え、
        /// 鏡像 ON（本人の右 = アバターの左）ではそのまま使う。
        /// </summary>
        public static int SourceIndex(int avatarIndex, bool mirror)
        {
            return mirror ? avatarIndex : Opposite[avatarIndex];
        }

        /// <summary>
        /// 受信値を書き込む（毎フレーム呼ぶ）。
        /// drivesBlink が false なら eyeBlinkLeft / Right は元の値へ戻して書かない（自動まばたきに任せる）。
        /// </summary>
        public void Apply(float[] scores, bool mirror, float deltaTime, bool drivesBlink)
        {
            // 要素数が合わない値は使わない
            if (scores == null || scores.Length != _targets.Length)
            {
                return;
            }

            _active = true;
            float blend = 1f - Mathf.Exp(-Smoothing * deltaTime);
            for (int i = 0; i < _targets.Length; i++)
            {
                // アバターに無い名前は計算しない
                if (_targets[i] == null)
                {
                    continue;
                }

                // まばたきを動かさない間は、一度だけ元の値へ戻して以降は書かない（同じ BlendShape への自動まばたきと競合させない）
                bool blinkShape = i == EyeBlinkLeft || i == EyeBlinkRight;
                if (blinkShape && !drivesBlink)
                {
                    if (_current[i] != 0f)
                    {
                        _current[i] = 0f;
                        Write(i, 0f);
                    }

                    continue;
                }

                // 左右を合わせた値（まばたきは閉じ切るよう範囲を広げる）を平滑化して 0〜100 で書く
                float target = Mathf.Clamp01(scores[SourceIndex(i, mirror)]);
                if (blinkShape)
                {
                    target = Mathf.InverseLerp(BlinkOpenScore, BlinkClosedScore, target);
                }

                _current[i] = Mathf.Lerp(_current[i], target, blend);
                Write(i, _current[i] * 100f);
            }
        }

        /// <summary>
        /// 書き込みをやめて元の値へ戻す（書き込み中だったときだけ働く）。
        /// </summary>
        public void Release()
        {
            // 既に戻していれば何もしない
            if (!_active)
            {
                return;
            }

            _active = false;
            for (int i = 0; i < _targets.Length; i++)
            {
                // 上乗せ 0 で元の値へ戻し、次に使うときは 0 から追従させる
                _current[i] = 0f;
                if (_targets[i] != null)
                {
                    Write(i, 0f);
                }
            }
        }

        private void Write(int index, float weight)
        {
            // 同じ名前の全メッシュへ書く
            foreach (BlendShapeOverlay overlay in _targets[index])
            {
                overlay.Write(weight);
            }
        }

        private static string Normalize(string name)
        {
            // 英数字だけを小文字で残す（"Eye_Blink_Left" と "eyeBlinkLeft" を同じにする）
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

        private static Dictionary<string, int> BuildNameTable()
        {
            var table = new Dictionary<string, int>();
            string[] names = MediaPipePacket.BlendShapeNames;
            for (int i = 0; i < names.Length; i++)
            {
                // 正式な名前と、末尾の Left / Right を L / R にした名前（"eyeBlink_L" 等）を登録
                string normalized = Normalize(names[i]);
                table[normalized] = i;
                if (normalized.EndsWith("left", StringComparison.Ordinal))
                {
                    table[normalized.Substring(0, normalized.Length - 4) + "l"] = i;
                }
                else if (normalized.EndsWith("right", StringComparison.Ordinal))
                {
                    table[normalized.Substring(0, normalized.Length - 5) + "r"] = i;
                }
            }

            return table;
        }

        private static int[] BuildOpposites()
        {
            string[] names = MediaPipePacket.BlendShapeNames;
            var opposites = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                // 名前の Left と Right を入れ替えた相手を探す（無ければ自分自身）
                string name = names[i];
                string swapped = name.EndsWith("Left", StringComparison.Ordinal)
                    ? name.Substring(0, name.Length - 4) + "Right"
                    : name.EndsWith("Right", StringComparison.Ordinal)
                        ? name.Substring(0, name.Length - 5) + "Left"
                        : name;
                int index = Array.IndexOf(names, swapped);
                opposites[i] = index >= 0 ? index : i;
            }

            return opposites;
        }
    }
}
