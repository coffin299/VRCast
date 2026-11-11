using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// MediaPipe の腰・膝・足首・足先をアバターの脚へ適用する。
    /// world 座標の原点は腰の中心なので、脚の曲げは点同士の方向だけを使う。
    /// 「部屋を歩き回る」が ON のときだけ、映像上の腰の位置で腰を水平に動かす。
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

        // 正面（キャリブレーション）の腰の向きと、使い始めの腰の回転。毎フレームここから足す（積み増しにしない）
        private bool _hipCalibrated;
        private float _hipNeutralYaw;
        private float _hipYawDegrees;
        private Quaternion _hipRest = Quaternion.identity;
        private bool _hipRestTaken;
        private bool _hipMirror;

        // 歩き回るモードの開始位置と、待機ポーズの腰の位置。毎フレームここから足す
        private const float MaxWalkDistance = 3f;
        private const float MinImageWidth = 0.02f;
        private const float MinShoulderWidth = 0.2f;

        // 水平画角 60° のとき、肩幅が画像の何割かからカメラまでの距離を概算する係数（2 * tan(30°)）
        private const float DepthFactor = 1.1547f;
        private const float WalkSmoothSeconds = 0.08f;

        // 体を横に向けると画像上の肩幅が縮む。それを「カメラから遠ざかった」とみなさないための下限
        private const float MinForeshorten = 0.4f;

        // 1 秒で進める距離の上限（推定が跳んでも足先が部屋の向こうへ飛ばないように）
        private const float MaxWalkSpeed = 2f;

        // 足首の可動域（度）。ひねりを許すと足先が横へ飛び、曲げ過ぎると床や空へ向く
        private const float MaxAnkleBend = 85f;
        private const float MaxAnkleTwist = 25f;

        private bool _walkCalibrated;
        private bool _walkMoved;
        private bool _hipPositionTaken;
        private float _walkNeutralX;
        private float _walkNeutralWidth;
        private float _walkNeutralWorld;
        private Vector3 _hipRestPosition;
        private Vector3 _walkOffset;

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
        /// 今の入力元で全身トラッキング（MediaPipe）のスイッチが ON なら true。
        /// このあいだは、顔が無いときの停止・待機モーション・頭の位置による体の動き・足の固定を使わない。
        /// トラッキング自体が OFF でも true（体タブの待機モーションも触れなくするため）。
        /// </summary>
        public static bool BlocksOtherMotion(AppSettings settings)
        {
            return settings != null && settings.trackingLegs
                && TrackingSourceInfo.HasArms(settings.trackingSource)
                && !TrackingSourceInfo.HasFullBody(settings.trackingSource);
        }

        /// <summary>
        /// 今の腰の向きを正面にする（顔の向きのリセットと一緒に呼ぶ）。
        /// </summary>
        public void Calibrate()
        {
            _hipCalibrated = false;
            _walkCalibrated = false;
        }

        /// <summary>
        /// この入力元で脚トラッキングがアバターを動かすなら true（足の固定はここが true のあいだ止める）。
        /// </summary>
        public static bool Drives(AppSettings settings)
        {
            return settings != null && settings.trackingEnabled && BlocksOtherMotion(settings);
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
            if (_hipMirror != mirror)
            {
                _hipMirror = mirror;
                _hipCalibrated = false;
                _walkCalibrated = false;
            }

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
                    if (_hips != null && _hipRestTaken)
                    {
                        _hips.localRotation = _hipRest;
                    }

                    if (_hips != null && _hipPositionTaken)
                    {
                        _hips.localPosition = _hipRestPosition;
                    }

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

            // 腰の向きと位置を合わせてから脚を合わせる（後から腰を動かすと、合わせた足がずれる）
            ApplyHipYaw();
            ApplyWalk(received ? frame : default, received);
            Apply(_left);
            Apply(_right);
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

            // 足先が映っていれば、足の裏を下に向けたまま足首から足先へ向ける。
            // ひねりを自由にすると、歩いて脚の向きが変わったときに足先が横や上へ飛ぶ
            AimFoot(leg, root);

            // 映り始め・消え際は待機ポーズとの間を混ぜる
            leg.Upper.localRotation = Quaternion.Slerp(upperRest, leg.Upper.localRotation, leg.Weight);
            leg.Lower.localRotation = Quaternion.Slerp(lowerRest, leg.Lower.localRotation, leg.Weight);
            leg.Foot.localRotation = Quaternion.Slerp(footRest, leg.Foot.localRotation, leg.Weight);
        }

        private void AimFoot(Leg leg, Quaternion root)
        {
            // 短すぎる足先は向きが暴れるので、脚の IK が付けた向きのままにする
            Vector3 footDirection = leg.FootFromAnkle;
            if (!leg.HasFoot || footDirection.sqrMagnitude < 0.04f * 0.04f || leg.ToeAxis.sqrMagnitude < 1e-8f)
            {
                return;
            }

            // ほぼ水平に倒す。上下は少しだけ残し、足先を床の下や頭の方へ向けない
            Vector3 desired = root * footDirection;
            Vector3 flat = Vector3.ProjectOnPlane(desired, root * Vector3.up);
            if (flat.sqrMagnitude < 1e-8f)
            {
                return;
            }

            Vector3 aim = flat.normalized * 0.85f + desired.normalized * 0.15f;
            if (aim.sqrMagnitude < 1e-8f)
            {
                return;
            }

            aim.Normalize();
            Vector3 current = leg.Foot.TransformDirection(leg.ToeAxis);
            if (current.sqrMagnitude < 1e-8f)
            {
                return;
            }

            // すねまわりのひねりと、足首の曲げを可動域に収める（足先が横へ飛ばないように）
            Vector3 shin = leg.Foot.position - leg.Lower.position;
            leg.Foot.rotation = LimitAnkle(Quaternion.FromToRotation(current, aim), shin) * leg.Foot.rotation;
        }

        private static Quaternion LimitAnkle(Quaternion delta, Vector3 shin)
        {
            // すねの向きが無ければ、角度だけ制限する
            if (shin.sqrMagnitude < 1e-8f)
            {
                return Quaternion.RotateTowards(Quaternion.identity, delta, MaxAnkleBend);
            }

            // すねまわりのひねりと、それ以外の曲げに分ける
            shin.Normalize();
            Vector3 vector = new Vector3(delta.x, delta.y, delta.z);
            Vector3 projected = Vector3.Project(vector, shin);
            var twist = new Quaternion(projected.x, projected.y, projected.z, delta.w);
            float length = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            twist = length > 1e-6f ? Quaternion.Normalize(twist) : Quaternion.identity;
            Quaternion bend = delta * Quaternion.Inverse(twist);
            twist = Quaternion.RotateTowards(Quaternion.identity, twist, MaxAnkleTwist);
            bend = Quaternion.RotateTowards(Quaternion.identity, bend, MaxAnkleBend);
            return bend * twist;
        }

        private void ApplyHipYaw()
        {
            if (_hips == null)
            {
                return;
            }

            // 待機ポーズの腰を一度だけ覚える（毎フレームここに戻してから向きを足す）
            if (!_hipRestTaken)
            {
                _hipRest = _hips.localRotation;
                _hipRestTaken = true;
            }

            _hips.localRotation = _hipRest;
            if (_hipWeight <= 0f || !_hasHipDirection)
            {
                _hipYawDegrees = 0f;
                return;
            }

            // 両腰の線の水平成分。上半身（TorsoPose）と同じ角度で、最初に映った向きを正面（0）とする
            Vector3 flat = Vector3.ProjectOnPlane(_hipDirection, Vector3.up);
            if (flat.sqrMagnitude < 1e-6f)
            {
                _hipYawDegrees = 0f;
                return;
            }

            float rawYaw = Mathf.Atan2(-flat.z, flat.x) * Mathf.Rad2Deg;
            if (!_hipCalibrated)
            {
                _hipNeutralYaw = rawYaw;
                _hipCalibrated = true;
            }

            float yawDegrees = Mathf.DeltaAngle(_hipNeutralYaw, rawYaw);
            _hipYawDegrees = yawDegrees * _hipWeight;
            Quaternion yaw = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(0f, yawDegrees, 0f), _hipWeight);
            Quaternion root = transform.rotation;
            _hips.rotation = root * yaw * Quaternion.Inverse(root) * _hips.rotation;
        }

        private void ApplyWalk(BodyTrackingFrame frame, bool received)
        {
            if (_hips == null)
            {
                return;
            }

            // 待機ポーズの腰の位置を一度だけ覚える
            if (!_hipPositionTaken)
            {
                _hipRestPosition = _hips.localPosition;
                _hipPositionTaken = true;
            }

            // 固定のあいだは開始位置へ戻す（歩き回っていた分を残さない）
            bool walk = received && _settings.trackingLegsWalk && frame.HasRoot;
            if (!walk)
            {
                _walkOffset = Vector3.zero;
                if (_walkMoved)
                {
                    _hips.localPosition = _hipRestPosition;
                    _walkMoved = false;
                }

                return;
            }

            // 最初に映った場所を開始位置にする（顔の向きのリセットで取り直す）
            if (!_walkCalibrated)
            {
                _walkNeutralX = frame.RootImageX;
                _walkNeutralWidth = frame.RootImageWidth;
                _walkNeutralWorld = Mathf.Max(frame.RootWorldWidth, MinShoulderWidth);
                _walkCalibrated = true;
            }

            // 体を横に向けると肩幅が縮んで見える。その分は奥へ下がったことにしない
            float foreshorten = Mathf.Max(Mathf.Abs(Mathf.Cos(_hipYawDegrees * Mathf.Deg2Rad)), MinForeshorten);
            float width = Mathf.Max(frame.RootImageWidth / foreshorten, MinImageWidth);
            float world = Mathf.Max(frame.RootWorldWidth, MinShoulderWidth);
            float metersPerX = world / width;
            float distance = world / (width * DepthFactor);
            float neutralDistance = _walkNeutralWorld / (Mathf.Max(_walkNeutralWidth, MinImageWidth) * DepthFactor);

            // 映像の右がカメラの +x、カメラから遠ざかる向きが +z。上下は床に置いたまま
            Vector3 camera = new Vector3(
                (frame.RootImageX - _walkNeutralX) * metersPerX, 0f, distance - neutralDistance);
            Vector3 target = TrackingMath.ToAvatar(camera, _settings.BodyMirror, _settings.TrackingTiltCorrection);
            target.y = 0f;
            target = Vector3.ClampMagnitude(target, MaxWalkDistance);
            float blend = 1f - Mathf.Exp(-Time.deltaTime / WalkSmoothSeconds);
            Vector3 step = Vector3.Lerp(_walkOffset, target, blend) - _walkOffset;
            float maxStep = MaxWalkSpeed * Time.deltaTime;
            if (step.magnitude > maxStep)
            {
                step = step.normalized * maxStep;
            }

            _walkOffset += step;
            _hips.localPosition = _hipRestPosition;
            _hips.position += transform.rotation * _walkOffset;
            _walkMoved = true;
        }
    }
}
