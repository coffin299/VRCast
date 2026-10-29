using System.Collections.Generic;
using UnityEngine;
using VRCast.Animations;
using VRCast.PerfectSync;

namespace VRCast.Tracking
{
    /// <summary>
    /// 視線を BlendShape で動かす（目ボーンが無い・瞳がメッシュの形状で動くアバター向け）。
    /// 「上を見る」「下を見る」「右を見る」「左を見る」など、両目まとめて動かす名前を探す。
    /// </summary>
    public sealed class GazeBlendShapes
    {
        // 方向ごとの照合用の名前（PerfectSyncAutoMapper.Normalize 済み・完全一致）。
        // EyeLookUp / eyelookup 等の Unified・ARKit 名はパーフェクトシンク側で動かすのでここに入れない
        private static readonly string[] UpNames = { "上を見る", "lookup", "look_up", "eyeslookup" };
        private static readonly string[] DownNames = { "下を見る", "lookdown", "look_down", "eyeslookdown" };
        private static readonly string[] RightNames = { "右を見る", "lookright", "look_right", "eyeslookright" };
        private static readonly string[] LeftNames = { "左を見る", "lookleft", "look_left", "eyeslookleft" };

        // 角度（度）がこの値のとき強さ 100（FaceTrackingDriver の目の回転上限に合わせる）
        private const float FullYaw = 20f;
        private const float FullPitch = 15f;

        private readonly BlendShapeOverlay[] _up;
        private readonly BlendShapeOverlay[] _down;
        private readonly BlendShapeOverlay[] _right;
        private readonly BlendShapeOverlay[] _left;
        private bool _active;

        private GazeBlendShapes(
            BlendShapeOverlay[] up, BlendShapeOverlay[] down, BlendShapeOverlay[] right, BlendShapeOverlay[] left)
        {
            _up = up;
            _down = down;
            _right = right;
            _left = left;
        }

        /// <summary>
        /// 上・下・左・右のいずれか 1 つでも見つかっていれば true。
        /// </summary>
        public bool IsAvailable => _up.Length > 0 || _down.Length > 0 || _right.Length > 0 || _left.Length > 0;

        /// <summary>
        /// root 以下から視線用の BlendShape を探す。無ければ IsAvailable が false のインスタンス。
        /// </summary>
        public static GazeBlendShapes Create(Transform root)
        {
            var up = new List<BlendShapeOverlay>();
            var down = new List<BlendShapeOverlay>();
            var right = new List<BlendShapeOverlay>();
            var left = new List<BlendShapeOverlay>();

            // 同じレンダラー・番号への上乗せは 1 つにまとめる（複数メッシュにあれば全部動かす）
            var seen = new HashSet<long>();
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                for (int shape = 0; shape < mesh.blendShapeCount; shape++)
                {
                    int direction = Match(mesh.GetBlendShapeName(shape));
                    if (direction < 0)
                    {
                        continue;
                    }

                    long key = ((long)renderer.GetInstanceID() << 32) ^ (uint)shape;
                    if (!seen.Add(key))
                    {
                        continue;
                    }

                    BlendShapeOverlay overlay = BlendShapeOverlay.Create(renderer, shape);
                    switch (direction)
                    {
                        case 0:
                            up.Add(overlay);
                            break;
                        case 1:
                            down.Add(overlay);
                            break;
                        case 2:
                            right.Add(overlay);
                            break;
                        case 3:
                            left.Add(overlay);
                            break;
                    }
                }
            }

            return new GazeBlendShapes(up.ToArray(), down.ToArray(), right.ToArray(), left.ToArray());
        }

        /// <summary>
        /// BlendShape 名が視線の上下左右のどれかに当てはまるか（0 = 上、1 = 下、2 = 右、3 = 左、それ以外は -1）。
        /// ARKit の片目用（eyeLookUpLeft 等）は対象外。
        /// </summary>
        public static int Match(string blendShapeName)
        {
            if (string.IsNullOrEmpty(blendShapeName))
            {
                return -1;
            }

            // FBX の接頭辞を外してから正規化（AutoMapper と同じ規則）
            int dot = blendShapeName.LastIndexOf('.');
            string name = PerfectSyncAutoMapper.Normalize(dot >= 0 ? blendShapeName.Substring(dot + 1) : blendShapeName);
            if (name.Length == 0 || IsArKitEyeLook(name))
            {
                return -1;
            }

            if (Contains(UpNames, name))
            {
                return 0;
            }

            if (Contains(DownNames, name))
            {
                return 1;
            }

            if (Contains(RightNames, name))
            {
                return 2;
            }

            if (Contains(LeftNames, name))
            {
                return 3;
            }

            return -1;
        }

        /// <summary>
        /// 視線の角度（度。x = 右向きが正、y = 上向きが正）を BlendShape に書く。
        /// </summary>
        public void Apply(float yawDegrees, float pitchDegrees)
        {
            // 無ければ何もしない
            if (!IsAvailable)
            {
                return;
            }

            _active = true;
            float up = pitchDegrees > 0f ? Mathf.Clamp01(pitchDegrees / FullPitch) * 100f : 0f;
            float down = pitchDegrees < 0f ? Mathf.Clamp01(-pitchDegrees / FullPitch) * 100f : 0f;
            float right = yawDegrees > 0f ? Mathf.Clamp01(yawDegrees / FullYaw) * 100f : 0f;
            float left = yawDegrees < 0f ? Mathf.Clamp01(-yawDegrees / FullYaw) * 100f : 0f;
            Write(_up, up);
            Write(_down, down);
            Write(_right, right);
            Write(_left, left);
        }

        /// <summary>
        /// 書き込みをやめて元の値へ戻す（書き込み中だったときだけ働く）。
        /// </summary>
        public void Release()
        {
            if (!_active)
            {
                return;
            }

            _active = false;
            Write(_up, 0f);
            Write(_down, 0f);
            Write(_right, 0f);
            Write(_left, 0f);
        }

        private static void Write(BlendShapeOverlay[] overlays, float weight)
        {
            for (int i = 0; i < overlays.Length; i++)
            {
                overlays[i].Write(weight);
            }
        }

        private static bool Contains(string[] names, string name)
        {
            for (int i = 0; i < names.Length; i++)
            {
                // 規則側も同じ正規化をしておく（アンダースコア付きの別名用）
                if (PerfectSyncAutoMapper.Normalize(names[i]) == name)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsArKitEyeLook(string normalized)
        {
            // eyeLook* / EyeLook*（左右分割・両目まとめ）はパーフェクトシンクが動かす。EyeLookPush は独自名なので対象外
            return normalized.StartsWith("eyelook", System.StringComparison.Ordinal)
                && !normalized.StartsWith("eyelookpush", System.StringComparison.Ordinal);
        }
    }
}
