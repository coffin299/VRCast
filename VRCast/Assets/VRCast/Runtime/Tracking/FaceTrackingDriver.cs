using UnityEngine;
using VRCast.Animations;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// フェイストラッキングをアバターへ適用する。頭の向きは首・頭ボーン、頭の位置（前後・左右）は背骨・胸の傾き、
    /// 視線は目ボーン、まばたき（左右別）・口は既存コントローラーへ外部入力として渡す。
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

        // 途絶後に自動キャリブレーションをやり直すまでの秒数
        private const float RecalibrateAfterSeconds = 1f;

        // 頭の位置の差分 1 単位あたりの上半身の傾き（度、強さ 1 のとき）と上限（度）
        private const float LeanDegreesPerUnit = 10f;
        private const float MaxLeanAngle = 20f;

        // 左右の閉じ具合の差がこれ未満なら平均する（検出のぶれで片目だけ閉じないように）
        private const float WinkThreshold = 0.3f;

        // 目ボーンの回転の上限（度、左右・上下）と追従速度（1 秒あたり）
        private const float MaxEyeYaw = 20f;
        private const float MaxEyePitch = 15f;
        private const float EyeSmoothing = 15f;

        // 両目の閉じ具合がこれ以上の間は視線を更新しない（瞳の検出が不安定なため）
        private const float GazeFreezeClosed = 0.5f;

        private IFaceTrackingProvider _provider;
        private BlinkController _blink;
        private LipSyncController _lipSync;
        private AppSettings _settings;

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

        // 視線の正面（キャリブレーション時）と、目標・平滑化済みの目の角度（x = 左右、y = 上下）
        private Vector2 _neutralGaze;
        private bool _gazeCalibrated;
        private Vector2 _gazeTarget;
        private Vector2 _currentGaze;

        // 直近の両目の閉じ具合の平均（視線の更新可否に使う）
        private float _eyesClosed;
        private float _lastFrameTime = float.NegativeInfinity;
        private FaceTrackingFrame _lastFrame;

        /// <summary>
        /// トラッキング値を受信して適用中なら true。
        /// </summary>
        public bool IsTracking { get; private set; }

        /// <summary>
        /// 正面位置からの頭の位置の差分（平滑化・鏡像適用済み、Provider の単位。UI での強さ調整用）。
        /// </summary>
        public Vector3 HeadOffset => _currentOffset;

        public void Initialize(
            Animator animator, IFaceTrackingProvider provider, BlinkController blink, LipSyncController lipSync,
            AppSettings settings)
        {
            _provider = provider;
            _blink = blink;
            _lipSync = lipSync;
            _settings = settings;

            // Humanoid のみ頭を動かす（非 Humanoid はまばたき・口だけ）
            if (animator == null || !animator.isHuman)
            {
                return;
            }

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
        /// 現在の頭の向き・位置・視線を正面とする。
        /// </summary>
        public void Calibrate()
        {
            // 受信済みフレームが無ければ次のフレームで行う
            _calibrated = IsTracking;
            _neutral = _lastFrame.HeadRotation;
            _neutralPosition = _lastFrame.HeadPosition;

            // 視線は有効なフレームが来たときに取り直す
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

            // 途絶から一定時間後の再開時は、その時点の向きを正面として取り直す
            if (received && Time.unscaledTime - _lastFrameTime > RecalibrateAfterSeconds)
            {
                _calibrated = false;
            }

            IsTracking = received;
            if (received)
            {
                _lastFrameTime = Time.unscaledTime;

                // 初回フレームを正面とする
                if (!_calibrated)
                {
                    Calibrate();
                }
            }

            ApplyFace(received);
            ApplyBody(received);
            ApplyEyes(received);
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

            // 鏡像モードでは本人の右目がアバターの左目
            bool mirror = _settings.trackingMirror;
            SetBlink(mirror ? personRight : personLeft, mirror ? personLeft : personRight);
            SetMouth(_lastFrame.MouthOpen);
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

            // 目標の相対回転・位置（途絶時は正面へ戻す）を平滑化
            Quaternion target = received ? CalculateHeadDelta() : Quaternion.identity;
            Vector3 targetOffset = received ? CalculateHeadOffset() : Vector3.zero;
            float blend = 1f - Mathf.Exp(-HeadSmoothing * Time.deltaTime);
            _current = Quaternion.Slerp(_current, target, blend);
            _currentOffset = Vector3.Lerp(_currentOffset, targetOffset, blend);

            // 無効化後に正面へ戻り切ったらボーンを触らない（待機ポーズ等の変更を妨げない）
            bool settled = Quaternion.Angle(_current, Quaternion.identity) < 0.01f && _currentOffset.sqrMagnitude < 1e-6f;
            if (!_settings.trackingEnabled && settled)
            {
                _current = Quaternion.identity;
                _currentOffset = Vector3.zero;
                return;
            }

            // 読込時の回転へ戻す（親から子へ順に回転を加えるため、先に全部戻す）
            RestoreRest(_spine, _spineRest);
            RestoreRest(_chest, _chestRest);
            RestoreRest(_neck, _neckRest);
            _head.localRotation = _headRest;

            // 上半身の傾きを背骨・胸で分担
            Quaternion lean = CalculateLean(_currentOffset);
            float torsoShare = _spine != null && _chest != null ? 0.5f : 1f;
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

        private Quaternion CalculateLean(Vector3 offset)
        {
            // 前後の移動は前後の傾き（X 軸まわり）、左右の移動は横の傾き（Z 軸まわり）へ
            float degreesPerUnit = LeanDegreesPerUnit * _settings.trackingBodyLean;
            float pitch = Mathf.Clamp(-offset.z * degreesPerUnit, -MaxLeanAngle, MaxLeanAngle);
            float roll = Mathf.Clamp(offset.x * degreesPerUnit, -MaxLeanAngle, MaxLeanAngle);
            return Quaternion.Euler(pitch, 0f, roll);
        }

        private Quaternion CalculateHeadDelta()
        {
            // 正面からの相対回転（カメラ基準）
            Quaternion delta = _lastFrame.HeadRotation * Quaternion.Inverse(_neutral);

            // 鏡像モードは左右反転（X 軸まわりはそのまま、Y・Z 軸まわりを逆向き）
            if (_settings.trackingMirror)
            {
                delta = new Quaternion(delta.x, -delta.y, -delta.z, delta.w);
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

            // 目標の視線（途絶時は正面、目を閉じている間・視線なしのフレームは直前の値を保持）
            if (!received)
            {
                _gazeTarget = Vector2.zero;
            }
            else if (_lastFrame.HasGaze && _eyesClosed < GazeFreezeClosed)
            {
                _gazeTarget = CalculateGaze();
            }

            float blend = 1f - Mathf.Exp(-EyeSmoothing * Time.deltaTime);
            _currentGaze = Vector2.Lerp(_currentGaze, _gazeTarget, blend);

            // 無効化後に正面へ戻り切ったらボーンを触らない
            if (!_settings.trackingEnabled && _currentGaze.sqrMagnitude < 1e-4f)
            {
                _currentGaze = Vector2.zero;
                return;
            }

            // 読込時の回転へ戻し、頭の向き（アバター基準の頭の回転）を基準に左右・上下へ回す
            RestoreRest(_leftEye, _leftEyeRest);
            RestoreRest(_rightEye, _rightEyeRest);
            Quaternion headFrame = transform.rotation * _current;
            Quaternion look = Quaternion.Euler(-_currentGaze.y, _currentGaze.x, 0f);
            RotateInFrame(_leftEye, headFrame, look);
            RotateInFrame(_rightEye, headFrame, look);
        }

        private Vector2 CalculateGaze()
        {
            // 初回（キャリブレーション直後）の有効な視線を正面とする
            if (!_gazeCalibrated)
            {
                _neutralGaze = _lastFrame.Gaze;
                _gazeCalibrated = true;
            }

            // 正面からの角度差（±180° の折り返しを考慮）。鏡像モードは左右反転
            float yaw = Mathf.DeltaAngle(_neutralGaze.x, _lastFrame.Gaze.x);
            float pitch = Mathf.DeltaAngle(_neutralGaze.y, _lastFrame.Gaze.y);
            if (_settings.trackingMirror)
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
        }
    }
}
