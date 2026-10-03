using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 腕・手（指）のトラッキングをアバターへ適用する。各ボーンを「子ボーンへの向き」がトラッキングの点の向きに一致するよう回す。
    /// 腕・手が映っていない間は待機ポーズへ戻し、全く使っていない間はボーンを触らない（待機ポーズの変更を妨げない）。
    /// 上半身の傾き（FaceTrackingDriver）の後、揺れもの（PhysBoneSimulator）の前に実行する。
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class HandTrackingDriver : MonoBehaviour
    {
        // 点の追従速度（大きいほど速い、1 秒あたり）
        private const float Smoothing = 15f;

        // 映った・消えたときに待機ポーズとの間を切り替える秒数
        private const float FadeSeconds = 0.3f;

        // 手の点の番号（MediaPipe Hand Landmarker）: 手首・人差し指・中指・小指の付け根
        private const int WristPoint = 0;
        private const int IndexBasePoint = 5;
        private const int MiddleBasePoint = 9;
        private const int LittleBasePoint = 17;

        // 指ごとの点の番号（付け根側から 4 点。親指は CMC から）
        private static readonly int[][] FingerPoints =
        {
            new[] { 1, 2, 3, 4 },
            new[] { 5, 6, 7, 8 },
            new[] { 9, 10, 11, 12 },
            new[] { 13, 14, 15, 16 },
            new[] { 17, 18, 19, 20 },
        };

        // 片腕のボーン（上腕・前腕・手、続いて指 5 本 × 3 節を親指から順に）
        private static readonly HumanBodyBones[] LeftBones =
        {
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal,
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
        };

        private static readonly HumanBodyBones[] RightBones =
        {
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal,
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        };

        // ボーン配列内の位置（上腕・前腕・手・最初の指の節、指 1 本あたりの節数）
        private const int UpperArm = 0;
        private const int LowerArm = 1;
        private const int Hand = 2;
        private const int FirstFinger = 3;
        private const int JointsPerFinger = 3;

        // 指の付け根（手の向きの計算用）の位置
        private const int IndexProximal = FirstFinger + JointsPerFinger;
        private const int MiddleProximal = FirstFinger + JointsPerFinger * 2;
        private const int LittleProximal = FirstFinger + JointsPerFinger * 4;

        /// <summary>
        /// 片腕分のボーンと追従状態。
        /// </summary>
        private sealed class ArmRig
        {
            public Transform[] Bones;

            // 各ボーンのローカル空間での「子ボーンへの向き」（求められなければ zero）
            public Vector3[] Axes;

            // 待機ポーズのローカル回転と、前フレームに適用したローカル回転（待機ポーズの変更の検出用）
            public Quaternion[] Snapshot;
            public Quaternion[] Applied;

            // 平滑化済みの上腕・前腕の向きと手の 21 点（アバタールート基準）
            public Vector3 UpperDirection;
            public Vector3 LowerDirection;
            public Vector3[] HandPoints = new Vector3[MediaPipePacket.HandPointCount];

            // 待機ポーズ（0）とトラッキング（1）の混ぜ具合
            public float ArmWeight;
            public float HandWeight;
        }

        private IBodyTrackingProvider _provider;
        private AppSettings _settings;
        private ArmRig _left;
        private ArmRig _right;

        // 待機ポーズを記録してボーンを操作中なら true
        private bool _engaged;

        /// <summary>
        /// 腕または手のトラッキング値を受信して適用中なら true。
        /// </summary>
        public bool IsTracking { get; private set; }

        public void Initialize(Animator animator, IBodyTrackingProvider provider, AppSettings settings)
        {
            _provider = provider;
            _settings = settings;

            // Humanoid のみ（腕のボーンが無ければ何もしない）
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            _left = CreateRig(animator, LeftBones);
            _right = CreateRig(animator, RightBones);
        }

        private static ArmRig CreateRig(Animator animator, HumanBodyBones[] ids)
        {
            // 上腕・前腕が無いアバターは対象外
            var bones = new Transform[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                bones[i] = animator.GetBoneTransform(ids[i]);
            }

            if (bones[UpperArm] == null || bones[LowerArm] == null)
            {
                return null;
            }

            // 各ボーンの向き（腕は次のボーン、指は同じ指の次の節、手は使わない）
            var axes = new Vector3[ids.Length];
            axes[UpperArm] = AxisTo(bones[UpperArm], bones[LowerArm]);
            axes[LowerArm] = AxisTo(bones[LowerArm], bones[Hand]);
            for (int i = FirstFinger; i < ids.Length; i++)
            {
                axes[i] = FingerAxis(bones, i);
            }

            return new ArmRig
            {
                Bones = bones,
                Axes = axes,
                Snapshot = new Quaternion[ids.Length],
                Applied = new Quaternion[ids.Length],
            };
        }

        private static Vector3 FingerAxis(Transform[] bones, int index)
        {
            Transform bone = bones[index];

            // 未割り当ての節は対象外
            if (bone == null)
            {
                return Vector3.zero;
            }

            // 最後の節以外は次の節への向き
            bool last = (index - FirstFinger) % JointsPerFinger == JointsPerFinger - 1;
            if (!last)
            {
                return AxisTo(bone, bones[index + 1]);
            }

            // 最後の節は指先のボーンがあればそれへ、無ければ前の節からの延長方向
            if (bone.childCount > 0)
            {
                return AxisTo(bone, bone.GetChild(0));
            }

            Transform parent = bones[index - 1];
            return parent != null ? LocalDirection(bone, bone.position - parent.position) : Vector3.zero;
        }

        private static Vector3 AxisTo(Transform bone, Transform child)
        {
            // どちらかが無ければ求められない
            return bone != null && child != null ? LocalDirection(bone, child.position - bone.position) : Vector3.zero;
        }

        private static Vector3 LocalDirection(Transform bone, Vector3 worldDirection)
        {
            // 長さ 0（ボーンが重なっている）なら求められない
            if (worldDirection.sqrMagnitude < 1e-10f)
            {
                return Vector3.zero;
            }

            return Quaternion.Inverse(bone.rotation) * worldDirection.normalized;
        }

        private void LateUpdate()
        {
            // 未初期化・非 Humanoid なら何もしない
            if (_settings == null || (_left == null && _right == null))
            {
                return;
            }

            // 有効（MediaPipe で腕・手が ON）かつ受信中のときだけ値を使う
            bool enabled = _settings.trackingEnabled && _settings.trackingHands
                && _settings.trackingSource == TrackingSource.MediaPipe;
            BodyTrackingFrame frame = default;
            bool received = enabled && _provider != null && _provider.TryGetBody(out frame);

            // 鏡像モードでは本人の右腕がアバターの左腕
            bool mirror = _settings.trackingMirror;
            UpdateTargets(_left, mirror ? frame.Right : frame.Left, received);
            UpdateTargets(_right, mirror ? frame.Left : frame.Right, received);
            IsTracking = received && (HasWeight(_left) || HasWeight(_right));

            // 操作中に待機ポーズが変更されていたら記録し直す
            if (_engaged)
            {
                RefreshSnapshotIfChanged(_left);
                RefreshSnapshotIfChanged(_right);
            }

            // 両腕とも待機ポーズに戻り切ったら、待機ポーズへ戻してボーンを触るのをやめる
            if (!HasWeight(_left) && !HasWeight(_right))
            {
                if (_engaged)
                {
                    RestoreSnapshot(_left);
                    RestoreSnapshot(_right);
                    _engaged = false;
                }

                return;
            }

            // 使い始めに現在の姿勢（待機ポーズ）を記録
            if (!_engaged)
            {
                TakeSnapshot(_left);
                TakeSnapshot(_right);
                _engaged = true;
            }

            Apply(_left);
            Apply(_right);
            RecordApplied(_left);
            RecordApplied(_right);
        }

        private static bool HasWeight(ArmRig rig)
        {
            // 対象外の腕は常に 0
            return rig != null && (rig.ArmWeight > 0f || rig.HandWeight > 0f);
        }

        private void UpdateTargets(ArmRig rig, ArmTrackingData data, bool received)
        {
            // 対象外の腕は何もしない
            if (rig == null)
            {
                return;
            }

            float blend = 1f - Mathf.Exp(-Smoothing * Time.deltaTime);
            float fade = Time.deltaTime / FadeSeconds;

            // 腕: 映っていれば向きを追従（待機ポーズから戻ってきた直後は補間せず合わせる）
            bool hasArm = received && data.HasArm;
            if (hasArm)
            {
                Vector3 upper = ToAvatar(data.Elbow - data.Shoulder).normalized;
                Vector3 lower = ToAvatar(data.Wrist - data.Elbow).normalized;
                bool following = rig.ArmWeight > 0f;
                rig.UpperDirection = following ? Vector3.Slerp(rig.UpperDirection, upper, blend) : upper;
                rig.LowerDirection = following ? Vector3.Slerp(rig.LowerDirection, lower, blend) : lower;
            }

            // 手: 同様に 21 点を追従
            bool hasHand = received && data.HasHand;
            if (hasHand)
            {
                float t = rig.HandWeight > 0f ? blend : 1f;
                for (int i = 0; i < rig.HandPoints.Length; i++)
                {
                    rig.HandPoints[i] = Vector3.Lerp(rig.HandPoints[i], ToAvatar(data.Hand[i]), t);
                }
            }

            // 映っている間はトラッキングへ、消えたら待機ポーズへ徐々に切り替える（最後の値を保持したまま）
            rig.ArmWeight = Mathf.MoveTowards(rig.ArmWeight, hasArm ? 1f : 0f, fade);
            rig.HandWeight = Mathf.MoveTowards(rig.HandWeight, hasHand ? 1f : 0f, fade);
        }

        private Vector3 ToAvatar(Vector3 cameraVector)
        {
            // カメラ基準 → アバタールート基準。通常はカメラの方を向いたアバター（180° 回転）、鏡像は左右反転のみ
            return _settings.trackingMirror
                ? new Vector3(cameraVector.x, cameraVector.y, -cameraVector.z)
                : new Vector3(-cameraVector.x, cameraVector.y, -cameraVector.z);
        }

        private void Apply(ArmRig rig)
        {
            // 対象外の腕・両方とも待機ポーズの腕は待機ポーズのまま
            if (rig == null)
            {
                return;
            }

            RestoreSnapshot(rig);

            // 腕: 上腕 → 前腕の順に向きを合わせる（親を回してから子の現在の向きを求める）
            if (rig.ArmWeight > 0f)
            {
                Aim(rig, UpperArm, rig.UpperDirection, rig.ArmWeight);
                Aim(rig, LowerArm, rig.LowerDirection, rig.ArmWeight);
            }

            // 手: 手首の向きを合わせてから、各指を付け根側から順に合わせる
            if (rig.HandWeight > 0f)
            {
                AlignHand(rig);
                for (int finger = 0; finger < FingerPoints.Length; finger++)
                {
                    int[] points = FingerPoints[finger];
                    for (int joint = 0; joint < JointsPerFinger; joint++)
                    {
                        Vector3 segment = rig.HandPoints[points[joint + 1]] - rig.HandPoints[points[joint]];
                        Aim(rig, FirstFinger + finger * JointsPerFinger + joint, segment, rig.HandWeight);
                    }
                }
            }
        }

        private void Aim(ArmRig rig, int index, Vector3 avatarDirection, float weight)
        {
            Transform bone = rig.Bones[index];
            Vector3 axis = rig.Axes[index];

            // ボーン・向きが無い、または目標の向きが潰れていれば何もしない
            if (bone == null || axis == Vector3.zero || avatarDirection.sqrMagnitude < 1e-10f)
            {
                return;
            }

            // 現在の子への向きを目標の向き（アバタールート基準 → ワールド）へ回す回転を、混ぜ具合だけ加える
            Vector3 current = bone.rotation * axis;
            Vector3 target = transform.rotation * avatarDirection;
            Quaternion delta = Quaternion.FromToRotation(current, target);
            bone.rotation = Quaternion.Slerp(Quaternion.identity, delta, weight) * bone.rotation;
        }

        private void AlignHand(ArmRig rig)
        {
            Transform hand = rig.Bones[Hand];
            Transform index = rig.Bones[IndexProximal];
            Transform middle = rig.Bones[MiddleProximal];
            Transform little = rig.Bones[LittleProximal];

            // 手の向きを決めるボーンが足りなければ手首は前腕に任せる
            if (hand == null || index == null || middle == null || little == null)
            {
                return;
            }

            // 現在の手の向き（手首 → 中指の付け根、小指 → 人差し指の付け根）と目標の手の向き。潰れた向きは使わない
            bool valid = TryHandFrame(middle.position - hand.position, index.position - little.position,
                    out Quaternion current)
                && TryHandFrame(
                    transform.rotation * (rig.HandPoints[MiddleBasePoint] - rig.HandPoints[WristPoint]),
                    transform.rotation * (rig.HandPoints[IndexBasePoint] - rig.HandPoints[LittleBasePoint]),
                    out Quaternion target);
            if (!valid)
            {
                return;
            }

            // 現在の向きを目標の向きへ回す回転を、混ぜ具合だけ加える
            Quaternion delta = target * Quaternion.Inverse(current);
            hand.rotation = Quaternion.Slerp(Quaternion.identity, delta, rig.HandWeight) * hand.rotation;
        }

        private static bool TryHandFrame(Vector3 forward, Vector3 side, out Quaternion frame)
        {
            // 指先方向を前、手のひらの法線を上とする回転（左右の手で同じ式を使うので鏡像の手にもそのまま使える）
            Vector3 normal = Vector3.Cross(forward, side);
            bool valid = forward.sqrMagnitude > 1e-10f && normal.sqrMagnitude > 1e-10f;
            frame = valid ? Quaternion.LookRotation(forward, normal) : Quaternion.identity;
            return valid;
        }

        private static void TakeSnapshot(ArmRig rig)
        {
            // 対象外の腕は何もしない
            if (rig == null)
            {
                return;
            }

            for (int i = 0; i < rig.Bones.Length; i++)
            {
                // 割り当てのあるボーンだけ記録
                if (rig.Bones[i] != null)
                {
                    rig.Snapshot[i] = rig.Bones[i].localRotation;
                }
            }
        }

        private static void RestoreSnapshot(ArmRig rig)
        {
            // 対象外の腕は何もしない
            if (rig == null)
            {
                return;
            }

            for (int i = 0; i < rig.Bones.Length; i++)
            {
                // 割り当てのあるボーンだけ戻す
                if (rig.Bones[i] != null)
                {
                    rig.Bones[i].localRotation = rig.Snapshot[i];
                }
            }
        }

        private static void RecordApplied(ArmRig rig)
        {
            // 対象外の腕は何もしない
            if (rig == null)
            {
                return;
            }

            for (int i = 0; i < rig.Bones.Length; i++)
            {
                // 割り当てのあるボーンだけ記録
                if (rig.Bones[i] != null)
                {
                    rig.Applied[i] = rig.Bones[i].localRotation;
                }
            }
        }

        private static void RefreshSnapshotIfChanged(ArmRig rig)
        {
            // 対象外の腕は何もしない
            if (rig == null)
            {
                return;
            }

            for (int i = 0; i < rig.Bones.Length; i++)
            {
                // 前フレームに適用した回転から変わっていれば、他（待機ポーズの変更）が姿勢を設定し直したので記録し直す
                if (rig.Bones[i] != null && rig.Bones[i].localRotation != rig.Applied[i])
                {
                    TakeSnapshot(rig);
                    return;
                }
            }
        }
    }
}
