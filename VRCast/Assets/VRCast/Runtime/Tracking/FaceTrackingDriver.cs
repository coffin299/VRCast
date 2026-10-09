using UnityEngine;
using VRCast.Animations;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// フェイストラッキングをアバターへ適用する。頭の向きは首・頭ボーン、頭の位置は背骨・胸の傾き（前後・左右）と
    /// 腰の移動（前後・左右・上下、足も一緒に動く）のどちらかまたは両方、
    /// 両肩（MediaPipe の体）が映っていれば上半身のひねり・左右の傾きを肩の線から背骨・胸へ、
    /// 視線は目ボーン、まばたき（左右別）・口は既存コントローラーへ外部入力として渡す。
    /// 表情（MediaPipe のみ）は判定結果が変わったときだけ、割り当てた表情プリセットへ切り替える。
    /// 揺れもの（PhysBoneSimulator）が回転後の頭を基準に計算できるよう、他の LateUpdate より先に実行する。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class FaceTrackingDriver : MonoBehaviour
    {
        // 首が受け持つ回転の割合（残りは頭）
        private const float NeckShare = 0.4f;

        // 頭の回転の上限（度、誤検出で首が折れないように）
        private const float MaxHeadAngle = 70f;

        // 頭の向きの追従速度（大きいほど速い、1 秒あたり）
        private const float HeadSmoothing = 20f;

        // 目の開きの値をまばたきへ写す範囲（この値以下で完全に閉じ、以上で完全に開く）
        private const float EyeClosedValue = 0.2f;
        private const float EyeOpenedValue = 0.55f;

        // 自動キャリブレーションで静止とみなす頭の回転の変化（度）と、静止が続く必要がある秒数
        // （映り始めや手で隠した直後の不安定な向きを正面にしないため）
        private const float CalibrationStableAngle = 3f;
        private const float CalibrationStableSeconds = 0.5f;

        // 見失った顔を再検出した直後、頭の向きを使わない秒数（再検出直後の不正確な値で頭が跳ねないように）
        private const float ReacquireSettleSeconds = 0.3f;

        // 頭の位置の差分 1 単位あたりの上半身の傾き（度、強さ 1 のとき）と上限（度）
        private const float LeanDegreesPerUnit = 10f;
        private const float MaxLeanAngle = 20f;

        // 頭の位置の差分 1 単位あたりの体全体の移動量（m、強さ 1 のとき）と上限（m）
        private const float MoveMetersPerUnit = 0.1f;
        private const float MaxMoveDistance = 0.3f;

        // 全身モードで腰が受け持つ上半身の傾きの割合（残りは背骨・胸。足は FootPlanter が固定する）
        private const float HipsLeanShare = 0.4f;

        // 肩の線による上半身のひねりの上限（度。左右の傾きの上限は MaxLeanAngle）と追従速度（1 秒あたり。肩の奥行きは揺れやすいので遅め）、
        // 肩が映った・消えたときに頭の位置による傾きとの間を切り替える秒数
        private const float MaxTorsoYaw = 35f;
        private const float TorsoSmoothing = 8f;
        private const float TorsoFadeSeconds = 0.3f;

        // 左右の閉じ具合の差がこれ未満なら平均する（検出のぶれで片目だけ閉じないように）
        private const float WinkThreshold = 0.3f;

        // 目ボーンの回転の上限（度、左右・上下）と追従速度（1 秒あたり）
        private const float MaxEyeYaw = 20f;
        private const float MaxEyePitch = 15f;
        private const float EyeSmoothing = 15f;

        // 両目の閉じ具合がこれ以上の間は視線を更新しない（瞳の検出が不安定なため）
        private const float GazeFreezeClosed = 0.5f;

        private IFaceTrackingProvider _provider;
        private IBodyTrackingProvider _body;
        private BlinkController _blink;
        private LipSyncController _lipSync;
        private ExpressionController _expressions;
        private AppSettings _settings;

        // 表情の判定と、自動で当てた表情プリセット（-1 = なし）、最後に割り当てを解決した表情と設定値
        private readonly ExpressionDetector _detector = new ExpressionDetector();
        private int _autoPreset = -1;
        private FaceExpression _resolvedExpression = FaceExpression.Neutral;
        private string _resolvedSaved;

        // 手動の固定で当てるのを止めていたら true（固定が外れたら判定結果をすぐ当て直す）
        private bool _manualHeld;

        // 割り当て先がある表情のビット列と、それを求めたときの割り当ての設定値（FaceExpression の値で引く）
        private int _candidates;
        private readonly string[] _candidateSaved = new string[(int)FaceExpressions.Last + 1];
        private bool _candidatesValid;

        // 表情ごとのしきい値（FaceExpression の値で引く。毎フレーム設定から詰め直す）
        private readonly float[] _thresholds = new float[(int)FaceExpressions.Last + 1];

        // 腰ボーンと読込時の位置・回転（体全体の移動・全身モードの傾き用）、前フレームに動かした・回したかどうか
        private Transform _hips;
        private Vector3 _hipsRest;
        private Quaternion _hipsRestRotation;
        private bool _hipsMoved;
        private bool _hipsRotated;

        // 背骨・胸・首・頭ボーンと、読込時（待機ポーズ適用後）の回転
        private Transform _spine;
        private Transform _chest;
        private Transform _neck;
        private Transform _head;
        private Quaternion _spineRest;
        private Quaternion _chestRest;
        private Quaternion _neckRest;
        private Quaternion _headRest;

        // 目ボーンと読込時の回転
        private Transform _leftEye;
        private Transform _rightEye;
        private Quaternion _leftEyeRest;
        private Quaternion _rightEyeRest;

        // キャリブレーション時の頭の回転・位置（正面）と、平滑化済みの相対回転・位置
        private Quaternion _neutral = Quaternion.identity;
        private Vector3 _neutralPosition;
        private bool _calibrated;
        private Quaternion _current = Quaternion.identity;
        private Vector3 _currentOffset;

        // 肩の線の正面（キャリブレーション後に最初に両肩が映ったときの角度。x = ひねり、y = 傾き）と、
        // それを取ったときの鏡像設定、平滑化済みの正面からの角度、頭の位置による傾きとの混ぜ具合（0〜1）
        private Vector2 _torsoNeutral;
        private bool _torsoCalibrated;
        private bool _torsoMirror;
        private Vector2 _torsoAngles;
        private float _torsoWeight;

        // 正面を取ったときの入力元とカメラ（変わったら以前の正面は基準が違うため取り直す）
        private TrackingSource _calibratedSource;
        private string _calibratedCamera;

        // 自動キャリブレーションの静止判定の基準の向きと、その向きになった時刻
        private Quaternion _stableReference = Quaternion.identity;
        private float _stableSince;

        // 前フレームに受信していたかと、再検出直後で頭の向きを使わない期限
        private bool _wasReceived;
        private float _settleUntil;

        // 視線の正面（キャリブレーション時）と、目標・平滑化済みの目の角度（x = 左右、y = 上下）
        private Vector2 _neutralGaze;
        private bool _gazeCalibrated;
        private Vector2 _gazeTarget;
        private Vector2 _currentGaze;

        // 直近の両目の閉じ具合の平均（視線の更新可否に使う）
        private float _eyesClosed;
        private FaceTrackingFrame _lastFrame;

        // パーフェクトシンクの書き込み先（ARKit 名の BlendShape）
        private PerfectSyncBlendShapes _perfectSync;

        /// <summary>
        /// アバターで見つかった ARKit 名の BlendShape の種類数（パーフェクトシンクの対応状況の表示用）。
        /// </summary>
        public int PerfectSyncShapeCount => _perfectSync != null ? _perfectSync.MatchedCount : 0;

        /// <summary>
        /// アバターがパーフェクトシンクを有効にする条件（設定の種類数）を満たしていれば true。
        /// </summary>
        public bool SupportsPerfectSync => _perfectSync != null && _settings != null
            && _perfectSync.IsAvailableFor(_settings.trackingPerfectSyncAnyShape);

        /// <summary>
        /// パーフェクトシンクで顔を動かしている最中なら true（MediaPipe で受信中、設定 ON、対応アバター）。
        /// </summary>
        public bool IsPerfectSyncActive { get; private set; }

        /// <summary>
        /// パーフェクトシンクが表情プリセットを止めているなら true
        /// （ARKit 名が MinMatchedShapes 種類未満のアバターは顔全体を動かせないので、表情プリセットを併用する）。
        /// </summary>
        public bool PausesExpressions => IsPerfectSyncActive && _perfectSync.IsAvailable;

        /// <summary>
        /// 視線（トラッキング・カメラ目線）で動かす目ボーンがあれば true。
        /// </summary>
        public bool HasEyes => _leftEye != null || _rightEye != null;

        /// <summary>
        /// トラッキング値を受信して適用中なら true。
        /// </summary>
        public bool IsTracking { get; private set; }

        /// <summary>
        /// 正面位置からの頭の位置の差分（平滑化・鏡像適用済み、Provider の単位。UI での強さ調整用）。
        /// </summary>
        public Vector3 HeadOffset => _currentOffset;

        /// <summary>
        /// 両肩が映っていて、肩の線で上半身の向きを動かしている最中なら true。
        /// </summary>
        public bool IsTrackingTorso { get; private set; }

        /// <summary>
        /// 表情反映が動作中なら true（MediaPipe で受信中、設定 ON、表情データあり）。
        /// </summary>
        public bool IsDetectingExpression { get; private set; }

        /// <summary>
        /// 判定中の表情（表情反映が動作していなければニュートラル）。
        /// </summary>
        public FaceExpression DetectedExpression => _detector.Current;

        public void Initialize(
            Animator animator, IFaceTrackingProvider provider, IBodyTrackingProvider body, BlinkController blink,
            LipSyncController lipSync, ExpressionController expressions, AppSettings settings)
        {
            _provider = provider;
            _body = body;
            _blink = blink;
            _lipSync = lipSync;
            _expressions = expressions;
            _settings = settings;

            // ARKit 名の BlendShape を探しておく（Humanoid でなくても顔は動かせる）
            _perfectSync = PerfectSyncBlendShapes.Create(transform);

            // Humanoid のみ頭を動かす（非 Humanoid はまばたき・口だけ）
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            // 腰は体全体の移動に使う
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _hipsRest = _hips != null ? _hips.localPosition : Vector3.zero;
            _hipsRestRotation = RestOf(_hips);

            // 首・胸が無いアバターは残りのボーンで回転させる（任意ボーンは null）
            _spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            _chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            _neck = animator.GetBoneTransform(HumanBodyBones.Neck);
            _head = animator.GetBoneTransform(HumanBodyBones.Head);
            _spineRest = RestOf(_spine);
            _chestRest = RestOf(_chest);
            _neckRest = RestOf(_neck);
            _headRest = RestOf(_head);

            // 目ボーンが無いアバターは視線なし
            _leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            _rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            _leftEyeRest = RestOf(_leftEye);
            _rightEyeRest = RestOf(_rightEye);
        }

        /// <summary>
        /// 現在の頭の向き・位置（上半身の傾きの基準）・視線を正面とする（VSeeFace の Calibrate 相当）。
        /// </summary>
        public void Calibrate()
        {
            // 受信済みフレームが無ければ次のフレームで行う
            _calibrated = IsTracking;
            _neutral = _lastFrame.HeadRotation;
            _neutralPosition = _lastFrame.HeadPosition;

            // 肩の線は次に両肩が映ったときに取り直す
            _torsoCalibrated = false;
            CalibrateGaze();
        }

        /// <summary>
        /// 視線だけを取り直す（現在見ている方向を正面とする）。
        /// </summary>
        public void CalibrateGaze()
        {
            // 有効なフレームが来たときに取り直す
            _gazeCalibrated = false;
        }

        private static Quaternion RestOf(Transform bone)
        {
            // 未割り当てのボーンは単位回転
            return bone != null ? bone.localRotation : Quaternion.identity;
        }

        private void LateUpdate()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            // 有効かつ受信中のときだけ値を使う
            bool received = _settings.trackingEnabled && _provider != null && _provider.TryGetFrame(out _lastFrame);

            // 無効化中や入力元・カメラの変更後は、次に映ったときに正面を取り直す
            // （顔を見失っただけなら正面は保持する。再検出直後の不正確な向きで取り直すと以後ずっとずれるため）
            if (!_settings.trackingEnabled || _settings.trackingSource != _calibratedSource
                || _settings.trackerCamera != _calibratedCamera)
            {
                _calibrated = false;
                _calibratedSource = _settings.trackingSource;
                _calibratedCamera = _settings.trackerCamera;
            }

            // 見失った顔を再検出した直後は、値が落ち着くまで頭の向きを使わない
            if (received && !_wasReceived)
            {
                _settleUntil = Time.unscaledTime + ReacquireSettleSeconds;
            }

            _wasReceived = received;
            IsTracking = received;

            // 正面が未設定なら、頭が静止したところで自動で取る
            if (received && !_calibrated)
            {
                TryAutoCalibrate();
            }

            // パーフェクトシンクは ARKit の値を受信中（MediaPipe・VMC・iFacialMocap）・設定 ON・対応アバターのときだけ
            IsPerfectSyncActive = received && _settings.trackingPerfectSync
                && _lastFrame.BlendShapes != null && SupportsPerfectSync;

            ApplyFace(received);
            ApplyPerfectSync();
            ApplyExpression(received);
            ApplyBody(received);
            ApplyEyes(received);
        }

        private void TryAutoCalibrate()
        {
            float now = Time.unscaledTime;

            // 再検出直後か、基準から頭が動いたら静止の計測をやり直す
            if (now < _settleUntil
                || Quaternion.Angle(_stableReference, _lastFrame.HeadRotation) > CalibrationStableAngle)
            {
                _stableReference = _lastFrame.HeadRotation;
                _stableSince = now;
                return;
            }

            // 一定時間ほぼ静止していたら、その向きを正面とする
            if (now - _stableSince >= CalibrationStableSeconds)
            {
                Calibrate();
            }
        }

        private bool IsHeadUsable(bool received)
        {
            // 正面が決まっていて、再検出直後でない受信フレームだけを頭・視線に使う
            return received && _calibrated && Time.unscaledTime >= _settleUntil;
        }

        private void ApplyExpression(bool received)
        {
            // 受信中・設定 ON・表情データあり（MediaPipe・VMC・iFacialMocap）のときだけ判定する
            // （パーフェクトシンク中は顔の動きで表情が出るため、表情プリセットを重ねない）
            IsDetectingExpression = received && _settings.trackingExpressions && !PausesExpressions
                && _lastFrame.HasExpression
                && _expressions != null && _expressions.Names.Count > 0;
            if (!IsDetectingExpression)
            {
                // 自動で当てた表情だけを戻し、判定もやり直す
                ReleaseAutoExpression();
                _detector.Reset();
                _resolvedExpression = FaceExpression.Neutral;
                return;
            }

            // 表情ごとのしきい値を詰める（スライダーの変更をすぐ反映する）
            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                _thresholds[(int)expression] = ExpressionMapping.GetThreshold(_settings, expression);
            }

            // 割り当て先の無い表情は選ばない（強く出ても、割り当てのある表情を押しのけてニュートラルにしない）
            FaceExpression detected = _detector.Update(
                _lastFrame.Expression, _lastFrame.MouthOpen, _thresholds, Time.deltaTime, GetCandidates());

            // 手動で固定中は当てない（判定は続け、固定を外したら今の判定結果をすぐ当て直す）
            if (_expressions.IsManual)
            {
                _autoPreset = -1;
                _manualHeld = true;
                return;
            }

            // 判定結果か割り当ての設定が変わったとき、固定が外れた直後だけ切り替える
            string saved = ExpressionMapping.GetSaved(_settings, detected);
            if (!_manualHeld && detected == _resolvedExpression && saved == _resolvedSaved)
            {
                return;
            }

            _manualHeld = false;
            _resolvedExpression = detected;
            _resolvedSaved = saved;

            // 割り当てが無い表情（ニュートラル含む）は、自動で当てた表情を戻すだけ
            int preset = ExpressionMapping.Resolve(_expressions.Names, detected, saved);
            if (preset < 0)
            {
                ReleaseAutoExpression();
                return;
            }

            _expressions.Apply(preset);
            _autoPreset = preset;
        }

        private int GetCandidates()
        {
            // 割り当ての設定が前回から変わっていなければ前回の結果を使う（名前の推定を毎フレーム行わない）
            bool same = _candidatesValid;
            for (var expression = FaceExpressions.First; same && expression <= FaceExpressions.Last; expression++)
            {
                same = _candidateSaved[(int)expression] == ExpressionMapping.GetSaved(_settings, expression);
            }

            if (same)
            {
                return _candidates;
            }

            // 設定値を控えて求め直す
            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                _candidateSaved[(int)expression] = ExpressionMapping.GetSaved(_settings, expression);
            }

            _candidates = ExpressionMapping.Candidates(_expressions.Names, _settings);
            _candidatesValid = true;
            return _candidates;
        }

        private void ReleaseAutoExpression()
        {
            // 自動で当てた表情が残っているときだけニュートラルへ（手動で選び直した・固定中の表情は残す）
            if (_expressions != null && !_expressions.IsManual && _autoPreset >= 0 && _expressions.Current == _autoPreset)
            {
                _expressions.ResetToNeutral();
            }

            _autoPreset = -1;
        }

        private void ApplyFace(bool received)
        {
            // 途絶時は外部入力を解除（自動まばたき・マイク口パクへ戻る）
            if (!received)
            {
                ClearBlink();
                SetMouth(0f);
                return;
            }

            // 本人の左右の目の開きを閉じ具合へ
            float personLeft = 1f - Mathf.InverseLerp(EyeClosedValue, EyeOpenedValue, _lastFrame.EyeOpenLeft);
            float personRight = 1f - Mathf.InverseLerp(EyeClosedValue, EyeOpenedValue, _lastFrame.EyeOpenRight);
            _eyesClosed = (personLeft + personRight) * 0.5f;

            // 差が小さければ平均（ウインクは差が大きいときだけ）
            if (Mathf.Abs(personLeft - personRight) < WinkThreshold)
            {
                personLeft = _eyesClosed;
                personRight = _eyesClosed;
            }

            // まばたきをトラッキングしない設定なら外部入力を解除し、自動まばたきに任せる
            if (!_settings.trackingBlink)
            {
                ClearBlink();
            }
            // パーフェクトシンクで目・口を動かす間は、まばたき・口パク用の BlendShape を重ねて閉じ過ぎ・開き過ぎにしない
            // （まばたきは開いたままの外部入力にして自動まばたきも止める）
            else if (IsPerfectSyncActive && _perfectSync.DrivesBlink)
            {
                SetBlink(0f, 0f);
            }
            else
            {
                // 鏡像モードでは本人の右目がアバターの左目（顔の左右の入れ替えも反映）
                bool mirror = _settings.FaceMirror;
                SetBlink(mirror ? personRight : personLeft, mirror ? personLeft : personRight);
            }

            SetMouth(IsPerfectSyncActive && _perfectSync.DrivesJaw ? 0f : _lastFrame.MouthOpen);
        }

        private void ApplyPerfectSync()
        {
            // 未初期化なら何もしない
            if (_perfectSync == null)
            {
                return;
            }

            // 動作中は毎フレーム書き、使わない間は元の値へ戻す（一度だけ）
            if (IsPerfectSyncActive)
            {
                _perfectSync.Apply(_lastFrame.BlendShapes, _lastFrame.BlendShapeRange, _settings.FaceMirror,
                    Time.deltaTime, _settings.trackingBlink);
            }
            else
            {
                _perfectSync.Release();
            }
        }

        private void SetBlink(float left, float right)
        {
            // まぶたが無いアバターでは何もしない
            if (_blink != null)
            {
                _blink.SetExternal(left, right);
            }
        }

        private void ClearBlink()
        {
            // まぶたが無い（または破棄済みの）アバターでは何もしない
            if (_blink != null)
            {
                _blink.ClearExternal();
            }
        }

        private void SetMouth(float open)
        {
            // 口が無いアバターでは何もしない
            if (_lipSync != null)
            {
                _lipSync.ExternalLevel = open;
            }
        }

        private void ApplyBody(bool received)
        {
            // 頭ボーンが無ければ何もしない
            if (_head == null)
            {
                return;
            }

            // 目標の相対回転・位置（途絶時・正面の未設定時・再検出直後は正面へ戻す）を平滑化
            bool usable = IsHeadUsable(received);
            Quaternion target = usable ? CalculateHeadDelta() : Quaternion.identity;
            Vector3 targetOffset = usable ? CalculateHeadOffset() : Vector3.zero;
            float blend = 1f - Mathf.Exp(-HeadSmoothing * Time.deltaTime);
            _current = Quaternion.Slerp(_current, target, blend);
            _currentOffset = Vector3.Lerp(_currentOffset, targetOffset, blend);
            UpdateTorso(usable);

            // 無効化後に正面へ戻り切ったらボーンを触らない（待機ポーズ等の変更を妨げない）
            bool settled = Quaternion.Angle(_current, Quaternion.identity) < 0.01f && _currentOffset.sqrMagnitude < 1e-6f
                && _torsoWeight <= 0f;
            if (!_settings.trackingEnabled && settled)
            {
                _current = Quaternion.identity;
                _currentOffset = Vector3.zero;
                MoveHips(false, false);
                RotateHips(Quaternion.identity, false);
                return;
            }

            // 読込時の回転へ戻す（親から子へ順に回転を加えるため、先に全部戻す）
            RestoreRest(_spine, _spineRest);
            RestoreRest(_chest, _chestRest);
            RestoreRest(_neck, _neckRest);
            _head.localRotation = _headRest;

            // 体全体の移動（腰を動かす。全身モードでは足を固定したまま膝で吸収し、それ以外は足も一緒に動く）
            BodyMotion motion = _settings.trackingBodyMotion;
            bool plantFeet = _settings.trackingPlantFeet && _hips != null;
            MoveHips(motion != BodyMotion.Lean, plantFeet);

            // 上半身の傾き（移動のみのモードでは頭の位置では傾けない）と肩の線による向きを、
            // 全身モードでは腰にも分け（下半身がついていく）、残りを背骨・胸で分担
            Quaternion lean = CalculateLean(_currentOffset, motion != BodyMotion.Move);
            float hipsShare = plantFeet ? HipsLeanShare : 0f;
            RotateHips(Quaternion.Slerp(Quaternion.identity, lean, hipsShare), plantFeet);
            float torsoShare = (_spine != null && _chest != null ? 0.5f : 1f) * (1f - hipsShare);
            RotateInAvatarSpace(_spine, Quaternion.Slerp(Quaternion.identity, lean, torsoShare));
            RotateInAvatarSpace(_chest, Quaternion.Slerp(Quaternion.identity, lean, torsoShare));

            // 頭の向きはトラッキング値どおりにするため、首（無ければ頭）で傾きを打ち消してから首と頭に分ける
            Transform first = _neck != null ? _neck : _head;
            RotateInAvatarSpace(first, Quaternion.Inverse(lean));
            float headShare = _neck != null ? 1f - NeckShare : 1f;
            RotateInAvatarSpace(_neck, Quaternion.Slerp(Quaternion.identity, _current, NeckShare));
            RotateInAvatarSpace(_head, Quaternion.Slerp(Quaternion.identity, _current, headShare));
        }

        private static void RestoreRest(Transform bone, Quaternion rest)
        {
            // 未割り当てのボーンは無視
            if (bone != null)
            {
                bone.localRotation = rest;
            }
        }

        private void MoveHips(bool move, bool plantFeet)
        {
            // 腰が無い、または動かさないモードで前フレームも動かしていなければ触らない
            if (_hips == null || (!move && !_hipsMoved))
            {
                return;
            }

            // 読込時の位置へ戻し、移動するモードならアバタールート基準の移動量をワールドで加える（親の拡大率に依存しない）
            _hips.localPosition = _hipsRest;
            _hipsMoved = move;
            if (move)
            {
                // 足を固定している間は、立った姿勢より上へは上げない（脚が届かず足が浮くため。下げる分は膝で曲げる）
                Vector3 offset = CalculateMove(_currentOffset);
                offset.y = plantFeet ? Mathf.Min(offset.y, 0f) : offset.y;
                _hips.position += transform.rotation * offset;
            }
        }

        private void RotateHips(Quaternion rotation, bool rotate)
        {
            // 腰が無い、または回さない状態で前フレームも回していなければ触らない
            if (_hips == null || (!rotate && !_hipsRotated))
            {
                return;
            }

            // 読込時の回転へ戻し、回すときだけアバタールート基準の回転を加える
            _hips.localRotation = _hipsRestRotation;
            _hipsRotated = rotate;
            if (rotate)
            {
                RotateInAvatarSpace(_hips, rotation);
            }
        }

        private Vector3 CalculateMove(Vector3 offset)
        {
            // カメラ基準の差分をアバタールート基準へ（傾きと同じ対応: カメラへ近づく = 前、左右は逆向き）
            Vector3 move = new Vector3(-offset.x, offset.y, -offset.z) * (MoveMetersPerUnit * _settings.trackingBodyLean);
            return Vector3.ClampMagnitude(move, MaxMoveDistance);
        }

        private Vector3 CalculateHeadOffset()
        {
            // 正面位置からの差分（カメラ基準）。鏡像モードは左右反転
            Vector3 offset = _lastFrame.HeadPosition - _neutralPosition;
            if (_settings.trackingMirror)
            {
                offset.x = -offset.x;
            }

            return offset;
        }

        private Quaternion CalculateLean(Vector3 offset, bool fromHead)
        {
            // 前後の移動は前後の傾き（X 軸まわり）、左右の移動は横の傾き（Z 軸まわり）へ（頭の位置で傾けないモードは 0）
            float degreesPerUnit = fromHead ? LeanDegreesPerUnit * _settings.trackingBodyLean : 0f;
            float pitch = Mathf.Clamp(-offset.z * degreesPerUnit, -MaxLeanAngle, MaxLeanAngle);
            float roll = Mathf.Clamp(offset.x * degreesPerUnit, -MaxLeanAngle, MaxLeanAngle);

            // 両肩が映っている間は、横の傾きを肩の線に置き換え（頭の位置と二重に傾けない）、ひねりを加える。
            // 前後の傾きは肩の奥行きが不安定なため頭の位置のまま
            Quaternion torso = TorsoPose.ToRotation(_torsoAngles);
            Quaternion head = Quaternion.Euler(pitch, 0f, roll);
            Quaternion withoutRoll = Quaternion.Euler(pitch, 0f, 0f);
            return Quaternion.Slerp(head, withoutRoll * torso, _torsoWeight);
        }

        private void UpdateTorso(bool usable)
        {
            // 設定 ON・腕を受信できる入力元で腕・手 ON・頭が使えるときだけ、両肩が映っていれば向きを求める
            bool enabled = usable && _settings.trackingTorso && _settings.trackingHands
                && TrackingSourceInfo.HasArms(_settings.trackingSource);
            Vector2 angles = Vector2.zero;
            bool has = enabled && _body != null && _body.TryGetBody(out BodyTrackingFrame body)
                && TorsoPose.TryGetAngles(body, _settings.trackingMirror, out angles);

            // 鏡像設定が変わったら左右の基準が変わるため正面を取り直す
            if (_torsoMirror != _settings.trackingMirror)
            {
                _torsoMirror = _settings.trackingMirror;
                _torsoCalibrated = false;
            }

            // キャリブレーション後に最初に映った肩の線を正面とする（カメラが斜めでも正面で 0 になるように）
            if (has && !_torsoCalibrated)
            {
                _torsoNeutral = angles;
                _torsoCalibrated = true;
            }

            // 正面からの角度差を上限に収め、平滑化して追従（頭の位置の傾きから戻ってきた直後は補間せず合わせる）。
            // ひねりを固定する設定なら、ひねりは正面（0）へ戻す
            if (has)
            {
                float twist = _settings.trackingTorsoLockTwist ? 0f : Mathf.DeltaAngle(_torsoNeutral.x, angles.x);
                var target = new Vector2(
                    Mathf.Clamp(twist, -MaxTorsoYaw, MaxTorsoYaw),
                    Mathf.Clamp(Mathf.DeltaAngle(_torsoNeutral.y, angles.y), -MaxLeanAngle, MaxLeanAngle));
                float blend = 1f - Mathf.Exp(-TorsoSmoothing * Time.deltaTime);
                _torsoAngles = _torsoWeight > 0f ? Vector2.Lerp(_torsoAngles, target, blend) : target;
            }

            // 映っている間は肩の線へ、消えたら頭の位置による傾きへ徐々に切り替える（最後の値を保持したまま）
            _torsoWeight = Mathf.MoveTowards(_torsoWeight, has ? 1f : 0f, Time.deltaTime / TorsoFadeSeconds);
            IsTrackingTorso = has;
        }

        private Quaternion CalculateHeadDelta()
        {
            // 正面からの相対回転（カメラ基準）
            Quaternion delta = _lastFrame.HeadRotation * Quaternion.Inverse(_neutral);

            // 鏡像モードは左右反転
            if (_settings.trackingMirror)
            {
                delta = TrackingMath.MirrorRotation(delta);
            }

            // 誤検出による極端な角度を制限
            return Quaternion.RotateTowards(Quaternion.identity, delta, MaxHeadAngle);
        }

        private void ApplyEyes(bool received)
        {
            // 目ボーンが無ければ何もしない
            if (_leftEye == null && _rightEye == null)
            {
                return;
            }

            // 頭の向き（アバター基準の頭の回転）。目はこれを基準に左右・上下へ回す
            Quaternion headFrame = transform.rotation * _current;

            // 目標の視線。カメラ目線ならカメラの方向（トラッキングの有無によらない）、
            // それ以外は途絶時・正面の未設定時・再検出直後は正面、目を閉じている間・視線なしのフレームは直前の値を保持
            Camera view = _settings.trackingLookAtCamera ? Camera.main : null;
            if (view != null)
            {
                _gazeTarget = CalculateGazeTo(view.transform.position, headFrame);
            }
            else if (!IsHeadUsable(received))
            {
                _gazeTarget = Vector2.zero;
            }
            else if (_lastFrame.HasGaze && _eyesClosed < GazeFreezeClosed)
            {
                _gazeTarget = CalculateGaze();
            }

            float blend = 1f - Mathf.Exp(-EyeSmoothing * Time.deltaTime);
            _currentGaze = Vector2.Lerp(_currentGaze, _gazeTarget, blend);

            // 無効化後に正面へ戻り切ったらボーンを触らない（カメラ目線の間は動かし続ける）
            if (!_settings.trackingEnabled && view == null && _currentGaze.sqrMagnitude < 1e-4f)
            {
                _currentGaze = Vector2.zero;
                return;
            }

            // 読込時の回転へ戻してから回す
            RestoreRest(_leftEye, _leftEyeRest);
            RestoreRest(_rightEye, _rightEyeRest);
            Quaternion look = Quaternion.Euler(-_currentGaze.y, _currentGaze.x, 0f);
            RotateInFrame(_leftEye, headFrame, look);
            RotateInFrame(_rightEye, headFrame, look);
        }

        private Vector2 CalculateGazeTo(Vector3 point, Quaternion headFrame)
        {
            // 両目の中間から見た点の方向を頭の向き基準にする（寄り目にならないよう両目で同じ角度を使う）
            Vector3 eyes = _leftEye != null && _rightEye != null
                ? (_leftEye.position + _rightEye.position) * 0.5f
                : (_leftEye != null ? _leftEye : _rightEye).position;
            Vector3 direction = Quaternion.Inverse(headFrame) * (point - eyes);

            // 左右（Y 軸まわり）と上下の角度にし、白目をむかない範囲に制限する
            float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            float pitch = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            return new Vector2(Mathf.Clamp(yaw, -MaxEyeYaw, MaxEyeYaw), Mathf.Clamp(pitch, -MaxEyePitch, MaxEyePitch));
        }

        private Vector2 CalculateGaze()
        {
            // 初回（キャリブレーション直後）の有効な視線を正面とする
            if (!_gazeCalibrated)
            {
                _neutralGaze = _lastFrame.Gaze;
                _gazeCalibrated = true;
            }

            // 正面からの角度差（±180° の折り返しを考慮）。鏡像モード（顔の左右の入れ替えも反映）は左右反転
            float yaw = Mathf.DeltaAngle(_neutralGaze.x, _lastFrame.Gaze.x);
            float pitch = Mathf.DeltaAngle(_neutralGaze.y, _lastFrame.Gaze.y);
            if (_settings.FaceMirror)
            {
                yaw = -yaw;
            }

            // 強さを掛けて目が白目をむかない範囲に制限
            float strength = _settings.trackingGaze;
            return new Vector2(
                Mathf.Clamp(yaw * strength, -MaxEyeYaw, MaxEyeYaw),
                Mathf.Clamp(pitch * strength, -MaxEyePitch, MaxEyePitch));
        }

        private void RotateInAvatarSpace(Transform bone, Quaternion rotation)
        {
            // アバタールート基準の回転をワールドへ変換して適用（Body yaw に追従）
            RotateInFrame(bone, transform.rotation, rotation);
        }

        private static void RotateInFrame(Transform bone, Quaternion frame, Quaternion rotation)
        {
            // 未割り当てのボーンは無視
            if (bone == null)
            {
                return;
            }

            // frame 基準の回転をワールドへ変換して適用
            bone.rotation = frame * rotation * Quaternion.Inverse(frame) * bone.rotation;
        }

        private void OnDestroy()
        {
            // アバター破棄時に外部入力を残さない
            ClearBlink();
            SetMouth(0f);
            _perfectSync?.Release();
        }
    }
}
