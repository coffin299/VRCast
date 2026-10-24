using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 外部アプリ（VMC プロトコル）から届いた全身の姿勢（腰・背骨・首・頭・脚・腕・指と体全体の位置）をアバターへ適用する。
    /// 送信側（正規化済み VRM）の「T ポーズからの回転」を、アバターの T ポーズ（Avatar の骨格情報）に掛けてボーンの向きにする。
    /// 足の接地（設定 ON）では、送信側の腰からの足の位置を脚の長さの比で直した位置へ、膝の 2 関節の IK で足を寄せる
    /// （送信側とアバターの脚の比率の違いで足が浮く・めり込むのを抑える。正面を取ったときに立った足が読込時の位置に来るよう補正する）。
    /// 他の処理（頭・上半身の傾き・足の固定・待機モーション）が作った姿勢の上に書き、受信の開始・途絶ではその姿勢との間を徐々に切り替える。
    /// 書いた姿勢は次のフレームの Update で書く前へ戻す（他の処理が毎フレーム同じ姿勢から計算できるように。他が書き直したボーンは戻さない）。
    /// 腕・手（HandTrackingDriver）の後、Constraint・揺れものの前に実行する。
    /// </summary>
    [DefaultExecutionOrder(-89)]
    public class FullBodyTrackingDriver : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "Tracking";

        // 受信の開始・途絶で、他の処理が作った姿勢との間を切り替える秒数
        private const float FadeSeconds = 0.3f;

        // 届いた姿勢へ寄る速さ（1 秒あたり。送信と描画のフレームレートの違いによるカクつきを抑える程度）。
        // ボーンの向きは目標との差が FastAngle 以上なら速め（速い動きで遅れない）
        private const float Smoothing = 30f;
        private const float FastSmoothing = 60f;
        private const float FastAngle = 10f;

        // 送信側とアバターの脚の長さの比（体全体の移動量の倍率）の範囲と、比を求めるのに使う送信側の脚の長さの下限（m）
        private const float MinMoveScale = 0.25f;
        private const float MaxMoveScale = 4f;
        private const float MinSenderLegLength = 0.05f;

        // 体全体の移動の上限（m。送信側の座標の飛びでアバターが画面外へ行かないように）
        private const float MaxMoveDistance = 3f;

        // 正面を取ったときの足の位置の補正の上限（m。座ったまま正面を取ったときなどに足が大きくずれないように）
        private const float MaxFootOffset = 0.3f;

        // 左右の脚（太もも・すね・足の HumanBodyBones の値。0 = 左、1 = 右）
        private static readonly int[][] LegBones =
        {
            new[] { (int)HumanBodyBones.LeftUpperLeg, (int)HumanBodyBones.LeftLowerLeg, (int)HumanBodyBones.LeftFoot },
            new[] { (int)HumanBodyBones.RightUpperLeg, (int)HumanBodyBones.RightLowerLeg, (int)HumanBodyBones.RightFoot },
        };

        private IHumanoidPoseProvider _provider;
        private AppSettings _settings;

        // 動かすボーン（HumanBodyBones の値で引く。未割り当て・対象外は null）と、アバターの T ポーズの回転（アバタールート基準）
        private Transform[] _bones;
        private Quaternion[] _tPose;

        // 動かすボーンを親から子の順に並べたもの（アバターの階層の深さ順）
        private int[] _order;

        // 腰と、読込時の腰の位置（アバタールート基準）・脚の長さ（体全体の移動量の倍率に使う）
        private Transform _hips;
        private Vector3 _hipsRest;
        private float _legLength;

        // 送信側の各ボーンの T ポーズからの回転（作業領域）
        private readonly Quaternion[] _sender = new Quaternion[HumanoidPoseFrame.BoneCount];

        // 目標と平滑化済みの向き（アバタールート基準）、それを使うボーンか（送信側から届いたボーンだけ動かす）
        private Quaternion[] _target;
        private Quaternion[] _smoothed;
        private bool[] _drive;

        // 目標と平滑化済みの腰の位置（アバタールート基準）、平滑化の途中か（受信し始めは寄せずに合わせる）
        private Vector3 _targetHips;
        private Vector3 _smoothedHips;
        private bool _hasTarget;
        private bool _hasSmoothed;

        // 書く前（他の処理が作った）と書いた後のローカル回転・腰のローカル位置、書いたか（次の Update で戻す）
        private Quaternion[] _base;
        private Quaternion[] _written;
        private Vector3 _hipsBase;
        private Vector3 _hipsWritten;
        private bool _applied;

        // 他の処理が作った姿勢（0）と全身の追従（1）の混ぜ具合、最後に使った受信の番号
        private float _weight;
        private int _lastSequence;

        // 正面の補正（送信側の腰の向きの左右を打ち消す回転）・正面の腰の位置（送信側）・移動量の倍率と、
        // それらを取ったか・取ったときに左右を反転していたか（ミラーを切り替えたら足の補正を取り直す）
        private Quaternion _yawCorrection = Quaternion.identity;
        private Vector3 _neutralHips;
        private float _moveScale = 1f;
        private bool _calibrated;
        private bool _calibratedFlip;

        // 足の接地（左右ごと）: 脚のボーンがそろっているか・読込時の足の位置（アバタールート基準）・
        // 正面を取ったときに立った足を読込時の位置へ合わせる補正・目標と平滑化済みの足の位置・今使うか
        private readonly bool[] _hasLeg = new bool[2];
        private readonly Vector3[] _footRest = new Vector3[2];
        private readonly Vector3[] _footOffset = new Vector3[2];
        private readonly Vector3[] _targetFeet = new Vector3[2];
        private readonly Vector3[] _smoothedFeet = new Vector3[2];
        private readonly bool[] _groundFeet = new bool[2];

        /// <summary>
        /// 全身の姿勢を受信してアバターに適用中なら true。
        /// </summary>
        public bool IsTracking { get; private set; }

        /// <summary>
        /// 全身を動かせるアバター（Humanoid で T ポーズが分かる）なら true。
        /// </summary>
        public bool IsAvailable => _bones != null;

        /// <summary>
        /// 直近に受け取ったボーンの数（状態表示用）。
        /// </summary>
        public int ReceivedBones { get; private set; }

        /// <summary>
        /// true の間は受信していないものとして扱い、他の処理が作った姿勢へ戻す（パーフェクトシンク化の編集中など）。
        /// </summary>
        public bool Suspended { get; set; }

        /// <summary>
        /// true の間は受信していないものとして扱い、他の処理が作った姿勢へ戻す（顔が見つからないあいだ止めるとき用）。
        /// </summary>
        public bool HoldInput { get; set; }

        public void Initialize(Animator animator, IHumanoidPoseProvider provider, AppSettings settings)
        {
            _provider = provider;
            _settings = settings;

            // Humanoid のみ（骨格情報が無ければ何もしない）
            if (animator == null || !animator.isHuman || animator.avatar == null)
            {
                return;
            }

            // 動かすボーン（目・顎は顔のトラッキングに任せる）
            var bones = new Transform[HumanoidPoseFrame.BoneCount];
            for (int bone = 0; bone < bones.Length; bone++)
            {
                bones[bone] = HumanoidRetarget.IsExcluded(bone) ? null : animator.GetBoneTransform((HumanBodyBones)bone);
            }

            // 腰が無いアバター・T ポーズが分からないアバターは対象外
            _hips = bones[(int)HumanBodyBones.Hips];
            var tPose = new Quaternion[bones.Length];
            if (_hips == null || !TryGetTPose(animator, bones, tPose))
            {
                VRCastLog.Warning(LogCategory, "Full body tracking is not available for this avatar (no T-pose in its Avatar)");
                return;
            }

            _bones = bones;
            _tPose = tPose;
            _order = CreateOrder(bones, transform);
            _hipsRest = Quaternion.Inverse(transform.rotation) * (_hips.position - transform.position);
            _legLength = LegLength(bones);

            // 足の接地に使う脚（太もも・すね・足がそろっている側だけ）と、読込時の足の位置
            for (int side = 0; side < LegBones.Length; side++)
            {
                int[] leg = LegBones[side];
                _hasLeg[side] = bones[leg[0]] != null && bones[leg[1]] != null && bones[leg[2]] != null;
                _footRest[side] = _hasLeg[side]
                    ? Quaternion.Inverse(transform.rotation) * (bones[leg[2]].position - transform.position)
                    : Vector3.zero;
            }

            // 作業領域
            _target = new Quaternion[bones.Length];
            _smoothed = new Quaternion[bones.Length];
            _drive = new bool[bones.Length];
            _base = new Quaternion[bones.Length];
            _written = new Quaternion[bones.Length];
        }

        /// <summary>
        /// 次に届いた姿勢の腰の向き・位置と足の位置を正面とする（顔の向きのキャリブレーションと一緒に行う）。
        /// </summary>
        public void Calibrate()
        {
            _calibrated = false;
        }

        private static bool TryGetTPose(Animator animator, Transform[] bones, Quaternion[] result)
        {
            // Avatar の骨格情報（Humanoid の設定時の T ポーズ。ボーン名ごとのローカル回転）
            SkeletonBone[] skeleton = animator.avatar.humanDescription.skeleton;
            if (skeleton == null || skeleton.Length == 0)
            {
                return false;
            }

            var rest = new Dictionary<string, Quaternion>(skeleton.Length);
            foreach (SkeletonBone bone in skeleton)
            {
                rest[bone.name] = bone.rotation;
            }

            // ボーンからアバタールートの手前までのローカル回転を、根元側から掛ける（骨格情報に無いものは今の回転）
            Transform root = animator.transform;
            for (int bone = 0; bone < bones.Length; bone++)
            {
                Quaternion rotation = Quaternion.identity;
                for (Transform node = bones[bone]; node != null && node != root; node = node.parent)
                {
                    rotation = (rest.TryGetValue(node.name, out Quaternion local) ? local : node.localRotation) * rotation;
                }

                result[bone] = rotation;
            }

            return true;
        }

        private static int[] CreateOrder(Transform[] bones, Transform root)
        {
            // 割り当てのあるボーンを、アバタールートからの深さが浅い順に並べる（親の向きを決めてから子の向きを決める）
            var order = new List<int>();
            var depths = new int[bones.Length];
            for (int bone = 0; bone < bones.Length; bone++)
            {
                if (bones[bone] == null)
                {
                    continue;
                }

                order.Add(bone);
                for (Transform node = bones[bone]; node != null && node != root; node = node.parent)
                {
                    depths[bone]++;
                }
            }

            order.Sort((a, b) => depths[a] != depths[b] ? depths[a].CompareTo(depths[b]) : a.CompareTo(b));
            return order.ToArray();
        }

        private static float LegLength(Transform[] bones)
        {
            // 左脚（太もも → すね → 足）の長さ。そろっていなければ 0（移動量は等倍）
            Transform upper = bones[(int)HumanBodyBones.LeftUpperLeg];
            Transform lower = bones[(int)HumanBodyBones.LeftLowerLeg];
            Transform foot = bones[(int)HumanBodyBones.LeftFoot];
            return upper != null && lower != null && foot != null
                ? Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, foot.position)
                : 0f;
        }

        private void Update()
        {
            // 前フレームに書いた姿勢を書く前へ戻す（他の処理が書き直したボーンはそのまま）
            if (!_applied)
            {
                return;
            }

            foreach (int bone in _order)
            {
                if (_bones[bone].localRotation == _written[bone])
                {
                    _bones[bone].localRotation = _base[bone];
                }
            }

            if (_hips.localPosition == _hipsWritten)
            {
                _hips.localPosition = _hipsBase;
            }

            _applied = false;
        }

        private void LateUpdate()
        {
            // 未初期化・対象外のアバターは何もしない
            if (_settings == null || _bones == null)
            {
                return;
            }

            // 有効（VMC で全身 ON、一時停止中でない）で、腰と両腕を含む姿勢を受信中のときだけ使う。
            // 顔が無いあいだ止めるときは正面の基準を残す（顔が戻ったときに取り直してずれないように）
            bool allowed = !Suspended && _settings.trackingEnabled && _settings.trackingVmcBody
                && TrackingSourceInfo.HasFullBody(_settings.trackingSource);
            HumanoidPoseFrame pose = null;
            bool received = allowed && !HoldInput && _provider != null && _provider.TryGetPose(out pose) && pose.HasBody;
            IsTracking = received;
            ReceivedBones = received ? pose.ReceivedCount : 0;

            // 無効化中・入力元の変更後は、次に受信したときに正面を取り直す
            if (!allowed)
            {
                _calibrated = false;
            }

            // 新しい姿勢が届いたら目標にする
            if (received && pose.Sequence != _lastSequence)
            {
                _lastSequence = pose.Sequence;
                UpdateTarget(pose);
            }

            // 受信中は全身の追従へ、途絶したら他の処理が作った姿勢へ徐々に切り替える（最後の値を保持したまま）
            _weight = Mathf.MoveTowards(_weight, received ? 1f : 0f, Time.deltaTime / FadeSeconds);
            if (_weight <= 0f || !_hasTarget)
            {
                // 戻り切ったら、次に受信したときは寄せずに合わせる
                _hasSmoothed = false;
                return;
            }

            Smooth();
            Apply();
        }

        private void UpdateTarget(HumanoidPoseFrame pose)
        {
            // 送信側のルートの向き（届いていなければ正面）と腰の位置
            Quaternion root = pose.HasRoot ? pose.RootRotation : Quaternion.identity;
            Vector3 hips = pose.HipsPosition;

            // 鏡像 OFF のときは送信側の左右を入れ替えて反転する（鏡像 ON で送信側アプリと同じ向き。頭の向きと同じ扱い）
            bool flip = !_settings.trackingMirror;

            // 正面が未設定なら、今の腰の向き・位置を正面とし、移動量の倍率を求める（足の補正は位置を求めた後で取る）
            bool calibrating = !_calibrated || flip != _calibratedFlip;
            if (!_calibrated)
            {
                float yaw = HumanoidRetarget.YawOf(root * pose.Rotations[(int)HumanBodyBones.Hips]);
                _yawCorrection = Quaternion.Euler(0f, -yaw, 0f);
                _neutralHips = hips;
                _moveScale = MoveScale(pose);
            }

            _calibrated = true;
            _calibratedFlip = flip;

            // 送信側の各ボーンの T ポーズからの回転（正面の補正込み、送信側アバタールート基準）
            HumanoidRetarget.ComputeAvatarRotations(pose, _yawCorrection * root, _sender);

            foreach (int bone in _order)
            {
                // 送信側から届いたボーンだけ動かす（届かないボーンは他の処理が作った姿勢のまま親についていく）
                int source = flip ? HumanoidRetarget.Mirror[bone] : bone;
                _drive[bone] = pose.Received[source];
                if (!_drive[bone])
                {
                    continue;
                }

                Quaternion rotation = flip ? TrackingMath.MirrorRotation(_sender[source]) : _sender[source];
                _target[bone] = rotation * _tPose[bone];
            }

            // 体全体の移動（正面の位置からの差を、正面の向き・脚の長さの比で直す。上限を超える飛びは抑える）
            Vector3 move = _yawCorrection * (hips - _neutralHips) * _moveScale;
            if (flip)
            {
                move.x = -move.x;
            }

            _targetHips = _hipsRest + Vector3.ClampMagnitude(move, MaxMoveDistance);
            UpdateFeetTarget(pose, flip, calibrating);
            _hasTarget = true;
        }

        private void UpdateFeetTarget(HumanoidPoseFrame pose, bool flip, bool calibrating)
        {
            for (int side = 0; side < LegBones.Length; side++)
            {
                // アバターの脚がそろい、送信側の同じ側（反転時は逆側）の脚の位置が分かるときだけ
                Vector3 foot = Vector3.zero;
                bool known = _hasLeg[side] && TryGetSenderFoot(pose, flip ? 1 - side : side, out foot);
                _groundFeet[side] = known && _settings.trackingVmcGroundFeet;
                if (!known)
                {
                    continue;
                }

                // 腰からの足の位置を脚の長さの比で直して、目標の腰の位置へ足す（反転時は左右も反転）
                if (flip)
                {
                    foot.x = -foot.x;
                }

                Vector3 target = _targetHips + foot * _moveScale;

                // 正面を取ったとき（まっすぐ立った姿勢）の足が読込時の位置に来るよう、ずれを補正として覚える
                if (calibrating)
                {
                    _footOffset[side] = Vector3.ClampMagnitude(_footRest[side] - target, MaxFootOffset);
                }

                _targetFeet[side] = target + _footOffset[side];
            }
        }

        private bool TryGetSenderFoot(HumanoidPoseFrame pose, int side, out Vector3 foot)
        {
            foot = Vector3.zero;
            int[] leg = LegBones[side];

            // 太もも・すね・足がすべて届いていなければ求められない
            if (!pose.Received[leg[0]] || !pose.Received[leg[1]] || !pose.Received[leg[2]])
            {
                return false;
            }

            // 各ボーンのローカル位置（ルートの拡大率込み）を親の向きで回して足していく（腰 → 太もも → すね → 足）
            Vector3 scale = pose.HasRoot ? pose.RootScale : Vector3.one;
            Vector3 upper = Vector3.Scale(scale, pose.Positions[leg[0]]);
            Vector3 lower = Vector3.Scale(scale, pose.Positions[leg[1]]);
            Vector3 ankle = Vector3.Scale(scale, pose.Positions[leg[2]]);
            foot = _sender[(int)HumanBodyBones.Hips] * upper + _sender[leg[0]] * lower + _sender[leg[1]] * ankle;

            // 位置を送らない（0 を送る）送信元では脚の長さが分からないので使わない
            return lower.magnitude + ankle.magnitude >= MinSenderLegLength;
        }

        private float MoveScale(HumanoidPoseFrame pose)
        {
            // 送信側の左脚の長さ（すね・足のローカル位置の長さ。ルートの拡大率込み）
            float scale = pose.HasRoot ? pose.RootScale.y : 1f;
            float sender = (pose.Positions[(int)HumanBodyBones.LeftLowerLeg].magnitude
                + pose.Positions[(int)HumanBodyBones.LeftFoot].magnitude) * scale;

            // どちらかの脚が分からなければ等倍
            bool known = _legLength > 0f && sender >= MinSenderLegLength
                && pose.Received[(int)HumanBodyBones.LeftLowerLeg] && pose.Received[(int)HumanBodyBones.LeftFoot];
            return known ? Mathf.Clamp(_legLength / sender, MinMoveScale, MaxMoveScale) : 1f;
        }

        private void Smooth()
        {
            // 受信し始めは寄せずに合わせ、以降は毎フレームなめらかに寄せる
            MotionSmoothingLevels.Rates(_settings.trackingSmoothing, Smoothing, Smoothing, out float slow, out _);
            float blend = _hasSmoothed ? 1f - Mathf.Exp(-slow * Time.deltaTime) : 1f;
            foreach (int bone in _order)
            {
                if (!_drive[bone])
                {
                    continue;
                }

                if (!_hasSmoothed)
                {
                    _smoothed[bone] = _target[bone];
                    continue;
                }

                float boneBlend = MotionSmoothingLevels.FollowBlend(_settings.trackingSmoothing,
                    Quaternion.Angle(_smoothed[bone], _target[bone]), FastAngle, Smoothing, FastSmoothing, Time.deltaTime);
                _smoothed[bone] = Quaternion.Slerp(_smoothed[bone], _target[bone], boneBlend);
            }

            _smoothedHips = Vector3.Lerp(_smoothedHips, _targetHips, blend);
            for (int side = 0; side < _smoothedFeet.Length; side++)
            {
                _smoothedFeet[side] = Vector3.Lerp(_smoothedFeet[side], _targetFeet[side], blend);
            }

            _hasSmoothed = true;
        }

        private void Apply()
        {
            // 書く前の姿勢（他の処理が今フレームに作ったもの）を記録
            foreach (int bone in _order)
            {
                _base[bone] = _bones[bone].localRotation;
            }

            _hipsBase = _hips.localPosition;

            // 腰の位置、続いて親から順に向きを合わせる（アバタールート基準 → ワールド。Body yaw に追従）
            Quaternion frame = transform.rotation;
            _hips.position = Vector3.Lerp(_hips.position, transform.position + frame * _smoothedHips, _weight);
            foreach (int bone in _order)
            {
                if (_drive[bone])
                {
                    Transform target = _bones[bone];
                    target.rotation = Quaternion.Slerp(target.rotation, frame * _smoothed[bone], _weight);
                }
            }

            GroundFeet(frame);

            // 書いた値を記録（次の Update で、書き直されていなければ書く前へ戻す）
            foreach (int bone in _order)
            {
                _written[bone] = _bones[bone].localRotation;
            }

            _hipsWritten = _hips.localPosition;
            _applied = true;
        }

        private void GroundFeet(Quaternion frame)
        {
            for (int side = 0; side < LegBones.Length; side++)
            {
                // 足の位置が分かる側だけ
                if (!_groundFeet[side])
                {
                    continue;
                }

                int[] leg = LegBones[side];
                Transform foot = _bones[leg[2]];

                // 膝の曲げ（2 関節の IK）で足を目標の位置へ寄せる（混ぜ具合の分だけ。膝はまっすぐなら前へ曲げる）。
                // 足首の向きは送信側のまま保つ
                Quaternion ankle = foot.rotation;
                Vector3 goal = transform.position + frame * _smoothedFeet[side];
                FootPlanter.Solve(_bones[leg[0]], _bones[leg[1]], foot, Vector3.Lerp(foot.position, goal, _weight),
                    frame * Vector3.forward);
                foot.rotation = ankle;
            }
        }
    }
}
