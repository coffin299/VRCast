using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// MediaPipe の腰・膝・足首・足先をアバターの脚へ適用する。
    /// world 座標の原点は腰の中心なので、部屋の中の位置には使わず、点同士の方向だけを使う。
    /// 両腰の線で腰の左右の向きを足し、腰 → 膝 → 足首の 2 関節 IK で太もも・すねを曲げる。
    /// 足先が映っていれば足首から足先の向きで足を回す。映っていない脚は待機ポーズのまま。
    /// 上半身の傾き（FaceTrackingDriver）と足の固定（FootPlanter）の後、腕（HandTrackingDriver）の前に実行する。
    /// VMC の全身（FullBodyTrackingDriver）が動く入力元では適用しない。
    /// </summary>
    [DefaultExecutionOrder(-97)]
    public class LegTrackingDriver : MonoBehaviour
    {
        // 届いた値へ寄る目安の秒数（腕と同じ。トラッカーが体の推定を間引いても段差が見えないように）
        private const float MinSmoothTime = 0.03f;
        private const float MaxSmoothTime = 0.1f;
        private const float IntervalShare = 0.8f;

        // 映った・消えたときに待機ポーズとの間を切り替える秒数
        private const float FadeSeconds = 0.3f;

        // 本人の脚の長さ（腰→膝→足首、m）をなめらかに追う秒数と、体格の比に使う下限
        private const float LengthSmoothSeconds = 1f;
        private const float MinTrackedLegLength = 0.4f;

        // 平滑化する点（腰からの膝・腰からの足首・足首からの足先）
        private const int KneeSample = 0;
        private const int AnkleSample = 1;
        private const int FootSample = 2;
        private const int SampleCount = 3;

        private IBodyTrackingProvider _provider;
        private AppSettings _settings;
        private Transform _hips;
        private Leg _left;
        private Leg _right;

        // 両腰の線（アバタールート基準）と、腰の向きを混ぜる割合
        private readonly PointSmoother _hipLine = new PointSmoother(1, MinSmoothTime, MaxSmoothTime, IntervalShare);
        private readonly Vector3[] _hipSample = new Vector3[1];
        private Vector3 _hipDirection;
        private bool _hasHipDirection;
        private float _hipWeight;

        // 使い始めに記録した待機ポーズへ戻しているあいだ true（戻り切ったらボーンを触らない）
        private bool _engaged;

        private int _lastSequence;

        /// <summary>
        /// 脚を動かせるアバター（Humanoid で太もも・すね・足がそろっている）なら true。
        /// </summary>
        public bool IsAvailable => _left != null || _right != null;

        /// <summary>
        /// 脚を受信してアバターに適用中なら true。
        /// </summary>
        public bool IsTracking { get; private set; }

        /// <summary>
        /// true のあいだは受信を使わず待機ポーズへ戻す（パーフェクトシンク化の編集中・離席中）。
        /// </summary>
        public bool Suspended { get; set; }

        /// <summary>
        /// true のあいだは受信を使わず待機ポーズへ戻す（顔が見つからないあいだ）。
        /// </summary>
        public bool HoldInput { get; set; }

        /// <summary>
        /// この入力元で脚トラッキングがアバターを動かすなら true（足の固定はここが true のあいだ止める）。
        /// </summary>
        public static bool Drives(AppSettings settings)
        {
            return settings != null && settings.trackingEnabled && settings.trackingLegs
                && TrackingSourceInfo.HasArms(settings.trackingSource)
                && !TrackingSourceInfo.HasFullBody(settings.trackingSource);
        }

        /// <summary>
        /// 1 本の脚のボーンと、使い始めたときの回転、平滑化した腰からの点。
        /// </summary>
        private sealed class Leg
        {
            public Transform Upper;
            public Transform Lower;
            public Transform Foot;
            public Quaternion UpperRest;
            public Quaternion LowerRest;
            public Quaternion FootRest;
            public readonly PointSmoother Path = new PointSmoother(SampleCount, MinSmoothTime, MaxSmoothTime, IntervalShare);
            public readonly Vector3[] Sample = new Vector3[SampleCount];
            public float TrackedLength;
            public float Weight;
            public bool HasFoot;
            public Vector3 KneeFromHip;
            public Vector3 AnkleFromHip;
            public Vector3 FootFromAnkle;
            public Vector3 ToeAxis = Vector3.forward;
        }

        public void Initialize(Animator animator, IBodyTrackingProvider provider, AppSettings settings)
        {
            _provider = provider;
            _settings = settings;
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _left = Create(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
            _right = Create(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        }

        private static Leg Create(Animator animator, HumanBodyBones upper, HumanBodyBones lower, HumanBodyBones foot)
        {
            var leg = new Leg
            {
                Upper = animator.GetBoneTransform(upper),
                Lower = animator.GetBoneTransform(lower),
                Foot = animator.GetBoneTransform(foot),
            };
            if (leg.Upper == null || leg.Lower == null || leg.Foot == null)
            {
                return null;
            }

            if (leg.Foot.childCount > 0)
            {
                Vector3 along = leg.Foot.GetChild(0).position - leg.Foot.position;
                if (along.sqrMagnitude > 1e-8f)
                {
                    leg.ToeAxis = leg.Foot.InverseTransformDirection(along.normalized);
                }
            }

            return leg;
        }

        private void LateUpdate()
        {
            if (_settings == null || (_left == null && _right == null))
            {
                return;
            }

            // 有効（MediaPipe の体で脚が ON、VMC の全身ではない、一時停止中でない）かつ受信中のときだけ値を使う
            bool enabled = !Suspended && !HoldInput && Drives(_settings);
            BodyTrackingFrame frame = default;
            bool received = enabled && _provider != null && _provider.TryGetBody(out frame);
            bool fresh = received && frame.Sequence != _lastSequence;
            if (received)
            {
                _lastSequence = frame.Sequence;
            }

            bool mirror = _settings.BodyMirror;
            float now = Time.unscaledTime;
            LegTrackingData leftData = mirror ? frame.RightLeg : frame.LeftLeg;
            LegTrackingData rightData = mirror ? frame.LeftLeg : frame.RightLeg;
            UpdateLeg(_left, leftData, received, fresh, now);
            UpdateLeg(_right, rightData, received, fresh, now);
            UpdateHips(leftData, rightData, received, fresh, now);
            IsTracking = received && ((_left != null && _left.Weight > 0f) || (_right != null && _right.Weight > 0f));

            if (!HasWeight(_left) && !HasWeight(_right) && _hipWeight <= 0f)
            {
                if (_engaged)
                {
                    Restore(_left);
                    Restore(_right);
                    _engaged = false;
                    _hasHipDirection = false;
                }

                return;
            }

            if (!_engaged)
            {
                Take(_left);
                Take(_right);
                _engaged = true;
            }

            Apply(_left);
            Apply(_right);
            ApplyHipYaw();
        }

        private static bool HasWeight(Leg leg)
        {
            return leg != null && leg.Weight > 0f;
        }

        private void UpdateLeg(Leg leg, LegTrackingData data, bool received, bool fresh, float now)
        {
            if (leg == null)
            {
                return;
            }

            leg.Path.SmoothTimeFloor = MotionSmoothingLevels.ArmSmoothTimeFloor(_settings.trackingSmoothing);
            bool hasLeg = received && data.HasLeg;
            if (hasLeg)
            {
                if (fresh || !leg.Path.HasValue)
                {
                    leg.Sample[KneeSample] = ToAvatar(data.Knee - data.Hip);
                    leg.Sample[AnkleSample] = ToAvatar(data.Ankle - data.Hip);
                    leg.Sample[FootSample] = data.HasFoot ? ToAvatar(data.Foot - data.Ankle) : Vector3.zero;
                    leg.HasFoot = data.HasFoot;
                    leg.Path.Push(leg.Sample, now);
                }

                leg.Path.Update(now);
                leg.KneeFromHip = leg.Path.Current[KneeSample];
                leg.AnkleFromHip = leg.Path.Current[AnkleSample];
                leg.FootFromAnkle = leg.Path.Current[FootSample];
                Vector3 knee = leg.KneeFromHip;
                Vector3 ankle = leg.AnkleFromHip;
                float length = knee.magnitude + (ankle - knee).magnitude;
                leg.TrackedLength = leg.TrackedLength > 0f
                    ? Mathf.Lerp(leg.TrackedLength, length, 1f - Mathf.Exp(-Time.deltaTime / LengthSmoothSeconds))
                    : length;
            }
            else
            {
                // 見失っても最後の方向は残す（消えるまでのフェードで脚を畳まない）
                leg.Path.Reset();
            }

            leg.Weight = Mathf.MoveTowards(leg.Weight, hasLeg ? 1f : 0f, Time.deltaTime / FadeSeconds);
        }

        private void UpdateHips(LegTrackingData left, LegTrackingData right, bool received, bool fresh, float now)
        {
            _hipLine.SmoothTimeFloor = MotionSmoothingLevels.ArmSmoothTimeFloor(_settings.trackingSmoothing);
            bool both = received && left.HasLeg && right.HasLeg;
            if (both)
            {
                if (fresh || !_hipLine.HasValue)
                {
                    _hipSample[0] = ToAvatar(right.Hip - left.Hip);
                    _hipLine.Push(_hipSample, now);
                }

                _hipLine.Update(now);
                _hipDirection = _hipLine.Current[0];
                _hasHipDirection = true;
            }
            else
            {
                _hipLine.Reset();
            }

            _hipWeight = Mathf.MoveTowards(_hipWeight, both ? 1f : 0f, Time.deltaTime / FadeSeconds);
        }

        private Vector3 ToAvatar(Vector3 cameraVector)
        {
            return TrackingMath.ToAvatar(cameraVector, _settings.BodyMirror, _settings.TrackingTiltCorrection);
        }

        private static void Take(Leg leg)
        {
            if (leg == null)
            {
                return;
            }

            leg.UpperRest = leg.Upper.localRotation;
            leg.LowerRest = leg.Lower.localRotation;
            leg.FootRest = leg.Foot.localRotation;
        }

        private static void Restore(Leg leg)
        {
            if (leg == null)
            {
                return;
            }

            leg.Upper.localRotation = leg.UpperRest;
            leg.Lower.localRotation = leg.LowerRest;
            leg.Foot.localRotation = leg.FootRest;
        }

        private void Apply(Leg leg)
        {
            if (leg == null || leg.Weight <= 0f)
            {
                return;
            }

            Restore(leg);
            Vector3 knee = leg.KneeFromHip;
            Vector3 ankle = leg.AnkleFromHip;
            float upperLength = Vector3.Distance(leg.Upper.position, leg.Lower.position);
            float lowerLength = Vector3.Distance(leg.Lower.position, leg.Foot.position);
            if (upperLength < 1e-4f || lowerLength < 1e-4f || leg.TrackedLength <= 0f)
            {
                return;
            }

            // 本人の脚の長さに対するアバターの脚の長さの比で、足首の目標を置く
            Quaternion root = transform.rotation;
            float scale = (upperLength + lowerLength) / Mathf.Max(leg.TrackedLength, MinTrackedLegLength);
            Vector3 target = leg.Upper.position + root * (ankle * scale);
            Vector3 bend = root * ArmIk.BendDirection(knee, ankle);
            Quaternion upperRest = leg.Upper.localRotation;
            Quaternion lowerRest = leg.Lower.localRotation;
            Quaternion footRest = leg.Foot.localRotation;
            FootPlanter.Solve(leg.Upper, leg.Lower, leg.Foot, target, bend);

            // 足先が映っていれば、足のボーンの前方向を足首→足先へ向ける
            Vector3 footDirection = leg.FootFromAnkle;
            if (leg.HasFoot && footDirection.sqrMagnitude > 1e-8f)
            {
                Vector3 desired = root * footDirection.normalized;
                Vector3 current = leg.Foot.TransformDirection(leg.ToeAxis);
                if (current.sqrMagnitude > 1e-8f && desired.sqrMagnitude > 1e-8f)
                {
                    leg.Foot.rotation = Quaternion.FromToRotation(current, desired) * leg.Foot.rotation;
                }
            }

            // 映り始め・消え際は待機ポーズとの間を混ぜる
            leg.Upper.localRotation = Quaternion.Slerp(upperRest, leg.Upper.localRotation, leg.Weight);
            leg.Lower.localRotation = Quaternion.Slerp(lowerRest, leg.Lower.localRotation, leg.Weight);
            leg.Foot.localRotation = Quaternion.Slerp(footRest, leg.Foot.localRotation, leg.Weight);
        }

        private void ApplyHipYaw()
        {
            if (_hips == null || _hipWeight <= 0f || !_hasHipDirection)
            {
                return;
            }

            // 両腰の線を水平に倒した向きを、アバターの右方向へ合わせる（正面を向いていれば回転なし）
            Vector3 flat = Vector3.ProjectOnPlane(_hipDirection, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Vector3 forward = Vector3.Cross(Vector3.up, flat.normalized);
            if (forward.sqrMagnitude < 1e-6f)
            {
                return;
            }

            Quaternion yaw = Quaternion.Slerp(Quaternion.identity, Quaternion.LookRotation(forward, Vector3.up), _hipWeight);
            Quaternion root = transform.rotation;
            _hips.rotation = root * yaw * Quaternion.Inverse(root) * _hips.rotation;
        }
    }
}
