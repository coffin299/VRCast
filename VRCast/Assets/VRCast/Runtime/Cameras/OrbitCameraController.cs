using UnityEngine;

namespace VRCast.Cameras
{
    /// <summary>
    /// 注視点を中心に回転・パン・ズームするカメラ操作。
    /// 右ドラッグ: 回転 / 中ドラッグ: パン / ホイール: ズーム。
    /// </summary>
    [RequireComponent(typeof(UnityEngine.Camera))]
    public class OrbitCameraController : MonoBehaviour
    {
        // マウス操作の感度
        private const float RotateSpeed = 4f;
        private const float PanSpeed = 0.02f;
        private const float ZoomSpeed = 0.1f;

        // 距離と仰角の制限
        private const float MinDistance = 0.1f;
        private const float MaxDistance = 50f;
        private const float MinPitch = -89f;
        private const float MaxPitch = 89f;

        // Frame 時に対象の周囲へ確保する余白率
        private const float FramePadding = 1.1f;

        // アバター未読込時の既定の注視点（目線の高さ付近）
        private static readonly Vector3 DefaultTarget = new Vector3(0f, 1.2f, 0f);
        private const float DefaultDistance = 2.5f;
        private const float DefaultFieldOfView = 30f;

        // VRChat アバターは +Z 向きなので、正面から見るため 180 度回す
        private const float FrontYaw = 180f;

        private UnityEngine.Camera _camera;
        private Vector3 _target;
        private float _distance;
        private float _yaw;
        private float _pitch;

        // Reset で戻す位置（Frame 時に更新）
        private Vector3 _homeTarget;
        private float _homeDistance;

        /// <summary>
        /// UI 操作中などマウス入力を無視したいときに true にする。
        /// </summary>
        public bool InputBlocked { get; set; }

        public float FieldOfView
        {
            get => _camera.fieldOfView;
            set => _camera.fieldOfView = Mathf.Clamp(value, 1f, 120f);
        }

        private void Awake()
        {
            // カメラ参照と既定値の初期化
            _camera = GetComponent<UnityEngine.Camera>();
            _camera.fieldOfView = DefaultFieldOfView;
            _homeTarget = DefaultTarget;
            _homeDistance = DefaultDistance;
            ResetView();
        }

        /// <summary>
        /// 境界全体が収まる距離で正面から映す。Reset の戻り先にもなる。
        /// </summary>
        public void Frame(Bounds bounds)
        {
            // 外接球の半径と画角から必要距離を計算
            float radius = Mathf.Max(bounds.extents.magnitude, MinDistance);
            float halfFov = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            _homeTarget = bounds.center;
            _homeDistance = Mathf.Clamp(radius / Mathf.Sin(halfFov) * FramePadding, MinDistance, MaxDistance);
            ResetView();
        }

        public void ResetView()
        {
            // 保存された注視点・距離に戻し、正面水平にする
            _target = _homeTarget;
            _distance = _homeDistance;
            _yaw = FrontYaw;
            _pitch = 0f;
            Apply();
        }

        private void LateUpdate()
        {
            // UI 操作中はカメラを動かさない
            if (!InputBlocked)
            {
                HandleInput();
            }

            Apply();
        }

        private void HandleInput()
        {
            float dx = Input.GetAxis("Mouse X");
            float dy = Input.GetAxis("Mouse Y");

            // 右ドラッグで注視点まわりに回転
            if (Input.GetMouseButton(1))
            {
                _yaw += dx * RotateSpeed;
                _pitch = Mathf.Clamp(_pitch - dy * RotateSpeed, MinPitch, MaxPitch);
            }

            // 中ドラッグで画面平面に沿って注視点を移動（距離に比例して速く）
            if (Input.GetMouseButton(2))
            {
                _target -= (transform.right * dx + transform.up * dy) * (_distance * PanSpeed);
            }

            // ホイールで距離を指数的に変更
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0f)
            {
                _distance = Mathf.Clamp(_distance * (1f - scroll * ZoomSpeed), MinDistance, MaxDistance);
            }
        }

        private void Apply()
        {
            // 球面座標からカメラ位置と向きを決定
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_target - rotation * Vector3.forward * _distance, rotation);
        }
    }
}
