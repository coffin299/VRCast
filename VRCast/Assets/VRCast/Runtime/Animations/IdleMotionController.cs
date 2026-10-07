using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// 待機モーション（呼吸・体の揺れ・頭のゆらぎ）。Humanoid の背骨・胸・肩・首・頭を、今の姿勢に少しだけ上乗せして回す。
    /// 前フレームに上乗せした分は、他（待機ポーズの変更・トラッキング）がボーンを書き直していなければ外してから付け直す
    /// （書き直されていればその姿勢を新しい基準にする）。OFF にすると 0.5 秒かけて止め、止まった後はボーンを触らない。
    /// 上半身の傾き・頭の向き（FaceTrackingDriver）の後、腕・手（HandTrackingDriver）と揺れもの（PhysBoneSimulator）の前に実行する。
    /// </summary>
    [DefaultExecutionOrder(-95)]
    public class IdleMotionController : MonoBehaviour
    {
        // ON/OFF・顔のトラッキングの開始/終了で動きを出し入れする秒数（急に止まったり動き出したりしないように）
        private const float FadeSeconds = 0.5f;

        // 上半身の揺れ・呼吸の反りのうち、首で打ち消す割合（頭が体と一緒に大きく傾かないように）
        private const float NeckCompensation = 0.6f;

        // 頭のゆらぎのうち首が受け持つ割合（残りは頭。FaceTrackingDriver と同じ）
        private const float NeckShare = 0.4f;

        /// <summary>
        /// 動かすボーン 1 本と、上乗せ前の回転・上乗せ後に書いた回転。
        /// </summary>
        private sealed class BoneSlot
        {
            public Transform Bone;
            public Quaternion Base;
            public Quaternion Written;
            public bool Applied;
        }

        private AppSettings _settings;
        private Func<bool> _isHeadTracked;

        // 上半身（あるものだけ）。揺れ・ひねりを等分し、呼吸の反りは胸側で受け持つ
        private readonly List<BoneSlot> _torso = new List<BoneSlot>();
        private readonly List<BoneSlot> _breathBones = new List<BoneSlot>();
        private BoneSlot _leftShoulder;
        private BoneSlot _rightShoulder;
        private BoneSlot _neck;
        private BoneSlot _head;

        // 親から子の順に並べた全ボーン（基準の記録・復元用）
        private readonly List<BoneSlot> _all = new List<BoneSlot>();

        private float _time;
        private float _weight;
        private float _headWeight = 1f;

        /// <summary>
        /// Humanoid で、動かせるボーンがあれば true。
        /// </summary>
        public bool IsAvailable => _all.Count > 0;

        /// <summary>
        /// isHeadTracked が true の間（顔のトラッキング中）は頭のゆらぎを止め、本人の頭の動きに任せる。
        /// </summary>
        public void Initialize(Animator animator, AppSettings settings, Func<bool> isHeadTracked)
        {
            _settings = settings;
            _isHeadTracked = isHeadTracked;

            // 非 Humanoid は何もしない
            if (animator == null || !animator.isHuman || animator.avatar == null)
            {
                return;
            }

            // 上半身（任意ボーンは無ければ飛ばす）
            BoneSlot spine = Add(animator, HumanBodyBones.Spine);
            BoneSlot chest = Add(animator, HumanBodyBones.Chest);
            BoneSlot upperChest = Add(animator, HumanBodyBones.UpperChest);
            foreach (BoneSlot slot in new[] { spine, chest, upperChest })
            {
                if (slot != null)
                {
                    _torso.Add(slot);
                }
            }

            // 呼吸の反りは胸・上胸で受け持つ（どちらも無ければ背骨）
            foreach (BoneSlot slot in new[] { chest, upperChest })
            {
                if (slot != null)
                {
                    _breathBones.Add(slot);
                }
            }

            if (_breathBones.Count == 0 && spine != null)
            {
                _breathBones.Add(spine);
            }

            // 肩・首・頭
            _leftShoulder = Add(animator, HumanBodyBones.LeftShoulder);
            _rightShoulder = Add(animator, HumanBodyBones.RightShoulder);
            _neck = Add(animator, HumanBodyBones.Neck);
            _head = Add(animator, HumanBodyBones.Head);
        }

        private BoneSlot Add(Animator animator, HumanBodyBones bone)
        {
            // 未割り当てのボーンは null
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
            // 未初期化・非 Humanoid は何もしない
            if (_settings == null || _all.Count == 0)
            {
                return;
            }

            // ON/OFF と、顔のトラッキング中かどうかを滑らかに切り替える
            float step = Time.deltaTime / FadeSeconds;
            _weight = Mathf.MoveTowards(_weight, _settings.idleMotionEnabled ? 1f : 0f, step);
            bool headTracked = _isHeadTracked != null && _isHeadTracked();
            _headWeight = Mathf.MoveTowards(_headWeight, headTracked ? 0f : 1f, step);

            // 前フレームの上乗せを外す（他が書き直したボーンはその姿勢のまま）
            Restore();

            // 止まり切ったらボーンを触らない（待機ポーズの変更などを妨げない）
            if (_weight <= 0f)
            {
                return;
            }

            // 速さの倍率を掛けて時刻を進める（倍率を変えても動きが飛ばないよう積算する）
            _time += Time.deltaTime * _settings.idleMotionSpeed;
            IdleMotionPose pose = IdleMotion.Evaluate(
                _time,
                _settings.idleBreathing * _weight,
                _settings.idleSway * _weight,
                _settings.idleHeadMotion * _weight * _headWeight);

            // 今の回転を基準として記録してから、親から子の順に上乗せする
            foreach (BoneSlot slot in _all)
            {
                slot.Base = slot.Bone.localRotation;
            }

            Apply(pose);

            // 書いた回転を記録（次のフレームで他が書き直したかを判定する）
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
                // 前フレームに書いたままなら基準へ戻す。書き直されていれば（トラッキング・待機ポーズ）そのままを基準にする
                if (slot.Applied && slot.Bone != null && slot.Bone.localRotation == slot.Written)
                {
                    slot.Bone.localRotation = slot.Base;
                }

                slot.Applied = false;
            }
        }

        private void Apply(IdleMotionPose pose)
        {
            // 上半身の揺れ・ひねりを背骨・胸・上胸で等分する
            float torsoShare = 1f / _torso.Count;
            Quaternion torso = Quaternion.Euler(0f, pose.TorsoYaw * torsoShare, pose.TorsoRoll * torsoShare);
            foreach (BoneSlot slot in _torso)
            {
                RotateInAvatarSpace(slot, torso);
            }

            // 吸ったときに胸を少し後ろへ反らす（X 軸の正が前傾なので符号を反転）
            if (_breathBones.Count > 0)
            {
                float breathShare = 1f / _breathBones.Count;
                Quaternion breath = Quaternion.Euler(-pose.ChestPitch * breathShare, 0f, 0f);
                foreach (BoneSlot slot in _breathBones)
                {
                    RotateInAvatarSpace(slot, breath);
                }
            }

            // 吸ったときに肩を上げる（右肩は +X 側なので Z の正、左肩は負で持ち上がる）
            RotateInAvatarSpace(_leftShoulder, Quaternion.Euler(0f, 0f, -pose.ShoulderRaise));
            RotateInAvatarSpace(_rightShoulder, Quaternion.Euler(0f, 0f, pose.ShoulderRaise));

            // 上半身の傾きの一部を首（無ければ頭）で打ち消し、頭が体と一緒に傾きすぎないようにする
            BoneSlot first = _neck ?? _head;
            Quaternion compensation = Quaternion.Euler(
                pose.ChestPitch * NeckCompensation, -pose.TorsoYaw * NeckCompensation, -pose.TorsoRoll * NeckCompensation);
            RotateInAvatarSpace(first, compensation);

            // 頭のゆらぎを首と頭で分担（首が無ければ頭だけ）
            Quaternion head = Quaternion.Euler(pose.Head);
            float headShare = _neck != null ? 1f - NeckShare : 1f;
            if (_neck != null)
            {
                RotateInAvatarSpace(_neck, Quaternion.Slerp(Quaternion.identity, head, NeckShare));
            }

            RotateInAvatarSpace(_head, Quaternion.Slerp(Quaternion.identity, head, headShare));
        }

        private void RotateInAvatarSpace(BoneSlot slot, Quaternion rotation)
        {
            // 未割り当てのボーンは無視
            if (slot == null)
            {
                return;
            }

            // アバタールート基準の回転をワールドへ変換して適用（Body yaw に追従）
            Quaternion frame = transform.rotation;
            slot.Bone.rotation = frame * rotation * Quaternion.Inverse(frame) * slot.Bone.rotation;
        }
    }
}
