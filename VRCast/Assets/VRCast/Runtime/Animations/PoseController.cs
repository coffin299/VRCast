using System;
using UnityEngine;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// Humanoid の待機ポーズ。読込時の姿勢（通常 T ポーズ）を基準に、腕の上下と肘の曲げだけを変える。
    /// Animator Controller を持たないため、一度設定した姿勢は次の変更まで保持される。
    /// </summary>
    public class PoseController : MonoBehaviour
    {
        // 「腕を下ろし切る」「肘を曲げ切る」ときの筋肉値（-1〜1）
        private const float ArmDownMuscle = -0.6f;
        private const float ElbowBentMuscle = 0.3f;

        // 変更対象の筋肉名（HumanTrait.MuscleName と一致させる）
        private static readonly string[] ArmMuscleNames = { "Left Arm Down-Up", "Right Arm Down-Up" };
        private static readonly string[] ElbowMuscleNames = { "Left Forearm Stretch", "Right Forearm Stretch" };

        private AppSettings _settings;
        private HumanPoseHandler _handler;
        private HumanPose _basePose;
        private float[] _baseMuscles;
        private int[] _armIndices;
        private int[] _elbowIndices;

        // 基準姿勢へ誤差なく戻すための元の位置・回転
        private Transform[] _bones;
        private Vector3[] _bonePositions;
        private Quaternion[] _boneRotations;

        public bool IsAvailable => _handler != null;

        public float ArmDown
        {
            get => _settings.poseArmDown;
            set => SetValue(ref _settings.poseArmDown, value);
        }

        public float ElbowBend
        {
            get => _settings.poseElbowBend;
            set => SetValue(ref _settings.poseElbowBend, value);
        }

        public void Initialize(Animator animator, AppSettings settings)
        {
            _settings = settings;

            // Humanoid 以外はポーズ操作不可（設定値だけ保持）
            if (animator == null || !animator.isHuman || animator.avatar == null)
            {
                return;
            }

            // 基準となる読込時の姿勢を記録
            RecordBones(animator);
            _handler = new HumanPoseHandler(animator.avatar, animator.transform);
            _basePose.muscles = new float[HumanTrait.MuscleCount];
            _handler.GetHumanPose(ref _basePose);
            _baseMuscles = (float[])_basePose.muscles.Clone();

            // 対象筋肉のインデックスを解決
            _armIndices = ResolveMuscles(ArmMuscleNames);
            _elbowIndices = ResolveMuscles(ElbowMuscleNames);

            // 保存済みの度合いを反映
            Apply();
        }

        private void RecordBones(Animator animator)
        {
            // 全 Humanoid ボーンの位置・回転を保存（LastBone は列挙の終端）
            var count = (int)HumanBodyBones.LastBone;
            _bones = new Transform[count];
            _bonePositions = new Vector3[count];
            _boneRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                // 未割り当てのボーンは null のまま
                _bones[i] = animator.GetBoneTransform((HumanBodyBones)i);
                if (_bones[i] != null)
                {
                    _bonePositions[i] = _bones[i].localPosition;
                    _boneRotations[i] = _bones[i].localRotation;
                }
            }
        }

        private static int[] ResolveMuscles(string[] names)
        {
            var indices = new int[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                // 見つからなければ -1（Unity の仕様変更時も落ちないように）
                indices[i] = Array.IndexOf(HumanTrait.MuscleName, names[i]);
            }

            return indices;
        }

        private void SetValue(ref float field, float value)
        {
            // 範囲補正して変化があった場合のみ再適用
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(field, value))
            {
                return;
            }

            field = value;
            Apply();
        }

        private void Apply()
        {
            // 初期化前・非 Humanoid は何もしない
            if (_handler == null)
            {
                return;
            }

            // 度合いが 0 なら基準姿勢を誤差なく復元
            if (_settings.poseArmDown <= 0f && _settings.poseElbowBend <= 0f)
            {
                RestoreBones();
                return;
            }

            // 基準の筋肉値から対象だけを補間した姿勢を作る
            var muscles = (float[])_baseMuscles.Clone();
            LerpMuscles(muscles, _armIndices, ArmDownMuscle, _settings.poseArmDown);
            LerpMuscles(muscles, _elbowIndices, ElbowBentMuscle, _settings.poseElbowBend);

            // 体の位置・向きは基準のまま適用
            HumanPose pose = _basePose;
            pose.muscles = muscles;
            _handler.SetHumanPose(ref pose);
        }

        private void LerpMuscles(float[] muscles, int[] indices, float target, float t)
        {
            foreach (int index in indices)
            {
                // 解決できなかった筋肉は無視
                if (index >= 0)
                {
                    muscles[index] = Mathf.Lerp(_baseMuscles[index], target, t);
                }
            }
        }

        private void RestoreBones()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                // 割り当てのあるボーンだけ戻す
                if (_bones[i] != null)
                {
                    _bones[i].SetLocalPositionAndRotation(_bonePositions[i], _boneRotations[i]);
                }
            }
        }

        private void OnDestroy()
        {
            // ネイティブリソースを解放
            _handler?.Dispose();
            _handler = null;
        }
    }
}
