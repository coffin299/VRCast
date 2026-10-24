using System.Collections.Generic;
using UnityEngine;

namespace VRCast.Animations
{
    /// <summary>
    /// 既定の立ちポーズを Humanoid のボーンへ上乗せする。マスコットモードで、VRChat のクリップを再生していない間だけ動く。
    /// 前フレームに上乗せした分は、他がボーンを書き直していなければ外してから付け直す。
    /// </summary>
    [DefaultExecutionOrder(20)]
    public class StandPosePlayer : MonoBehaviour
    {
        private sealed class BoneSlot
        {
            public Transform Bone;
            public Quaternion Base;
            public Quaternion Written;
            public bool Applied;
        }

        private readonly List<BoneSlot> _all = new List<BoneSlot>();
        private BoneSlot _hips;
        private BoneSlot _spine;
        private BoneSlot _chest;
        private BoneSlot _leftKnee;
        private BoneSlot _rightKnee;
        private BoneSlot _leftArm;
        private BoneSlot _rightArm;
        private BoneSlot _neck;
        private BoneSlot _head;

        private float _time;
        private float _weight;

        /// <summary>
        /// true の間、強さ 1 で立ちポーズを付ける。
        /// </summary>
        public bool Active { get; set; }

        /// <summary>
        /// true の間は頭を動かさない（カーソルを見ている間、視線側に任せる）。
        /// </summary>
        public bool SuppressHead { get; set; }

        public void Initialize(Animator animator)
        {
            _all.Clear();
            if (animator == null || !animator.isHuman)
            {
                return;
            }

            _hips = Add(animator, HumanBodyBones.Hips);
            _spine = Add(animator, HumanBodyBones.Spine);
            _chest = Add(animator, HumanBodyBones.Chest) ?? Add(animator, HumanBodyBones.UpperChest);
            _leftKnee = Add(animator, HumanBodyBones.LeftLowerLeg);
            _rightKnee = Add(animator, HumanBodyBones.RightLowerLeg);
            _leftArm = Add(animator, HumanBodyBones.LeftUpperArm);
            _rightArm = Add(animator, HumanBodyBones.RightUpperArm);
            _neck = Add(animator, HumanBodyBones.Neck);
            _head = Add(animator, HumanBodyBones.Head);
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
            if (_all.Count == 0)
            {
                return;
            }

            _weight = Mathf.MoveTowards(_weight, Active ? 1f : 0f, Time.deltaTime / 0.45f);
            Restore();
            if (_weight <= 0f)
            {
                return;
            }

            _time += Time.deltaTime;
            StandPoseSample sample = StandPose.Evaluate(_time, _weight);
            foreach (BoneSlot slot in _all)
            {
                slot.Base = slot.Bone.localRotation;
            }

            Apply(sample);
            foreach (BoneSlot slot in _all)
            {
                slot.Written = slot.Bone.localRotation;
                slot.Applied = true;
            }
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

        private void Apply(StandPoseSample sample)
        {
            // 腰で重心を移し、背骨で打ち消す。正のロールは右へ傾く（Unity は軸を見て時計回り）
            Rotate(_hips, Quaternion.Euler(0f, sample.HipYaw, sample.HipRoll));
            Rotate(_spine, Quaternion.Euler(0f, 0f, sample.SpineRoll));

            // 胸は上向きの骨。正の X で後ろへ反る（吸ったとき）
            Rotate(_chest, Quaternion.Euler(sample.ChestPitch, 0f, 0f));

            // 下腿は下向き。正の X で足が後ろへ行き、膝が曲がる
            Rotate(_leftKnee, Quaternion.Euler(sample.LeftKnee, 0f, 0f));
            Rotate(_rightKnee, Quaternion.Euler(sample.RightKnee, 0f, 0f));

            // 腕も下向き。正の値は前へ出すので、X は逆に回す
            Rotate(_leftArm, Quaternion.Euler(-sample.LeftArm, 0f, 0f));
            Rotate(_rightArm, Quaternion.Euler(-sample.RightArm, 0f, 0f));

            if (SuppressHead)
            {
                return;
            }

            Quaternion head = Quaternion.Euler(sample.Head);
            if (_neck != null)
            {
                Rotate(_neck, Quaternion.Slerp(Quaternion.identity, head, 0.4f));
                Rotate(_head, Quaternion.Slerp(Quaternion.identity, head, 0.6f));
            }
            else
            {
                Rotate(_head, head);
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
}
