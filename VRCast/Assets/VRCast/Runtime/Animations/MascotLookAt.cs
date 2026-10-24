using System.Collections.Generic;
using UnityEngine;
using VRCast.Platform;

namespace VRCast.Animations
{
    /// <summary>
    /// マスコットが、近くのマウスカーソルの方を向く。遠いときは何も足さない。
    /// 首・頭・目へ、前フレームの上乗せを外してから付け直す。
    /// </summary>
    [DefaultExecutionOrder(200)]
    public class MascotLookAt : MonoBehaviour
    {
        // 頭の画面位置からこの距離（px）以内を「近い」とする。端で 0 になる
        private const float NearPixels = 520f;

        // 向ける最大角度（度）
        private const float MaxYaw = 42f;
        private const float MaxPitch = 24f;

        private sealed class BoneSlot
        {
            public Transform Bone;
            public Quaternion Base;
            public Quaternion Written;
            public bool Applied;
        }

        private readonly List<BoneSlot> _all = new List<BoneSlot>();
        private BoneSlot _neck;
        private BoneSlot _head;
        private BoneSlot _leftEye;
        private BoneSlot _rightEye;
        private Transform _aim;
        private float _weight;
        private float _yaw;
        private float _pitch;

        public bool Active { get; set; }

        /// <summary>
        /// 向きの計算に使うカメラ（マスコットウィンドウへ描いているもの）。
        /// </summary>
        public Camera View { get; set; }

        /// <summary>
        /// カーソルが近いほど 1。立ちポーズはこれで頭を止める。
        /// </summary>
        public float Weight => _weight;

        /// <summary>
        /// マスコットウィンドウの左上（画面座標）と大きさ。未設定なら向かない。
        /// </summary>
        public Rect ScreenRect { get; set; }

        public void Initialize(Animator animator)
        {
            _all.Clear();
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            _neck = Add(animator, HumanBodyBones.Neck);
            _head = Add(animator, HumanBodyBones.Head);
            _leftEye = Add(animator, HumanBodyBones.LeftEye);
            _rightEye = Add(animator, HumanBodyBones.RightEye);
            _aim = _head != null ? _head.Bone : _neck != null ? _neck.Bone : null;
        }

        private BoneSlot Add(Animator animator, HumanBodyBones bone)
        {
            Transform transformOfBone = animator.GetBoneTransform(bone);
            if (transformOfBone == null)
            {
                return null;
            }

            var slot = new BoneSlot { Bone = transformOfBone };
            _all.Add(slot);
            return slot;
        }

        private void LateUpdate()
        {
            if (_aim == null)
            {
                return;
            }

            float target = 0f;
            float yaw = 0f;
            float pitch = 0f;
            if (Active && View != null && ScreenRect.width > 1f && TryAim(out yaw, out pitch, out float closeness))
            {
                target = closeness;
            }

            float step = Time.deltaTime * 6f;
            _weight = Mathf.MoveTowards(_weight, target, step);
            _yaw = Mathf.MoveTowards(_yaw, yaw * _weight, 80f * Time.deltaTime);
            _pitch = Mathf.MoveTowards(_pitch, pitch * _weight, 80f * Time.deltaTime);

            Restore();
            if (_weight <= 0.001f && Mathf.Abs(_yaw) < 0.05f && Mathf.Abs(_pitch) < 0.05f)
            {
                return;
            }

            foreach (BoneSlot slot in _all)
            {
                slot.Base = slot.Bone.localRotation;
            }

            Quaternion offset = Quaternion.Euler(_pitch, _yaw, 0f);
            if (_neck != null)
            {
                Rotate(_neck, Quaternion.Slerp(Quaternion.identity, offset, 0.35f));
                Rotate(_head, Quaternion.Slerp(Quaternion.identity, offset, 0.65f));
            }
            else
            {
                Rotate(_head, offset);
            }

            Rotate(_leftEye, offset);
            Rotate(_rightEye, offset);

            foreach (BoneSlot slot in _all)
            {
                slot.Written = slot.Bone.localRotation;
                slot.Applied = true;
            }
        }

        private bool TryAim(out float yaw, out float pitch, out float closeness)
        {
            yaw = 0f;
            pitch = 0f;
            closeness = 0f;
            Vector3 viewport = View.WorldToViewportPoint(_aim.position);
            if (viewport.z <= 0f)
            {
                return false;
            }

            float headX = ScreenRect.x + viewport.x * ScreenRect.width;
            float headY = ScreenRect.y + (1f - viewport.y) * ScreenRect.height;
            Vector2 cursor = MascotCursor.Screen;
            float distance = Vector2.Distance(cursor, new Vector2(headX, headY));
            if (distance >= NearPixels)
            {
                return true;
            }

            closeness = 1f - distance / NearPixels;

            // カーソルを、頭を通るカメラ正面の平面へ落として向きを決める
            float u = (cursor.x - ScreenRect.x) / ScreenRect.width;
            float v = 1f - (cursor.y - ScreenRect.y) / ScreenRect.height;
            Ray ray = View.ViewportPointToRay(new Vector3(u, v, 0f));
            var plane = new Plane(-View.transform.forward, _aim.position);
            if (!plane.Raycast(ray, out float enter))
            {
                return true;
            }

            Vector3 local = transform.InverseTransformDirection(ray.GetPoint(enter) - _aim.position);
            yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -MaxYaw, MaxYaw);
            float flat = new Vector2(local.x, local.z).magnitude;
            pitch = Mathf.Clamp(-Mathf.Atan2(local.y, flat) * Mathf.Rad2Deg, -MaxPitch, MaxPitch);
            return true;
        }

        private void Restore()
        {
            foreach (BoneSlot slot in _all)
            {
                if (slot.Applied && slot.Bone != null && slot.Bone.localRotation == slot.Written)
                {
                    slot.Bone.localRotation = slot.Base;
                }

                slot.Applied = false;
            }
        }

        private void Rotate(BoneSlot slot, Quaternion rotation)
        {
            if (slot == null)
            {
                return;
            }

            Quaternion frame = transform.rotation;
            slot.Bone.rotation = frame * rotation * Quaternion.Inverse(frame) * slot.Bone.rotation;
        }
    }

    /// <summary>
    /// マウスカーソルの画面座標（左上原点）。エディタでもプレイヤーでも同じ。
    /// </summary>
    public static class MascotCursor
    {
        public static Vector2 Screen
        {
            get
            {
                if (!MascotWindow.TryCursor(out int x, out int y))
                {
                    Vector3 mouse = Input.mousePosition;
                    return new Vector2(mouse.x, UnityEngine.Screen.height - mouse.y);
                }

                return new Vector2(x, y);
            }
        }
    }
}
