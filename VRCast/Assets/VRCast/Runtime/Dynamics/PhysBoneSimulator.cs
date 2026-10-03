using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Dynamics
{
    /// <summary>
    /// アバターの全 PhysBone を固定タイムステップで更新する。AppSettings.physicsEnabled で ON/OFF。
    /// </summary>
    public class PhysBoneSimulator : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "PhysBone";

        // 固定タイムステップと 1 フレームの最大ステップ数（低 FPS 時の暴走防止）
        private const float TimeStep = 1f / 60f;
        private const int MaxSubSteps = 3;

        // アバター 1 体あたりの粒子数上限（負荷対策）
        private const int MaxParticles = 4096;

        private readonly List<PhysBoneChain> _chains = new List<PhysBoneChain>();
        private readonly List<PhysBoneCollider> _colliders = new List<PhysBoneCollider>();
        private AppSettings _settings;
        private float _accumulator;
        private bool _wasEnabled;

        public int ChainCount => _chains.Count;

        public bool IsAvailable => _chains.Count > 0;

        public void Initialize(Animator animator, PhysBoneSet set, AppSettings settings)
        {
            _settings = settings;
            Transform root = transform;

            // Humanoid ボーンは揺らさない（待機ポーズ・将来のトラッキングと競合させない）
            HashSet<Transform> humanBones = CollectHumanBones(animator);

            // コライダーを解決（見つからないものは null のまま残し、インデックスを保つ）
            var resolved = new List<PhysBoneCollider>();
            foreach (PhysBoneColliderData data in set.colliders)
            {
                PhysBoneCollider collider = PhysBoneCollider.Create(root, data);
                resolved.Add(collider);
                if (collider != null)
                {
                    _colliders.Add(collider);
                }
            }

            // チェーンを構築（残り粒子数の範囲内）
            int particles = 0;
            foreach (PhysBoneData data in set.bones)
            {
                PhysBoneChain chain = PhysBoneChain.Create(root, data, resolved, humanBones, MaxParticles - particles);
                if (chain != null)
                {
                    _chains.Add(chain);
                    particles += chain.ParticleCount;
                }
            }

            VRCastLog.Info(LogCategory,
                $"Built {_chains.Count}/{set.bones.Length} chains, {particles} particles, {_colliders.Count} colliders.");
        }

        private static HashSet<Transform> CollectHumanBones(Animator animator)
        {
            var bones = new HashSet<Transform>();

            // 非 Humanoid なら空
            if (animator == null || !animator.isHuman)
            {
                return bones;
            }

            // 割り当て済みの全 Humanoid ボーン
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null)
                {
                    bones.Add(bone);
                }
            }

            return bones;
        }

        private void LateUpdate()
        {
            // 未初期化・対象なし
            if (_settings == null || !IsAvailable)
            {
                return;
            }

            // OFF にされたら静止姿勢へ戻して停止
            if (!_settings.physicsEnabled)
            {
                if (_wasEnabled)
                {
                    _chains.ForEach(chain => chain.RestoreRestPose());
                    _wasEnabled = false;
                }

                return;
            }

            // ON にされた直後は粒子を静止位置から始める
            if (!_wasEnabled)
            {
                _chains.ForEach(chain => chain.Reset());
                _accumulator = 0f;
                _wasEnabled = true;
            }

            // 静止姿勢・コライダー位置を更新
            _chains.ForEach(chain => chain.BeginFrame());
            _colliders.ForEach(collider => collider.UpdateWorld());

            // 経過時間分だけ固定ステップで進める（上限を超えた分は捨てる）
            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= TimeStep && steps < MaxSubSteps)
            {
                foreach (PhysBoneChain chain in _chains)
                {
                    chain.Step(TimeStep);
                }

                _accumulator -= TimeStep;
                steps++;
            }

            if (steps == MaxSubSteps)
            {
                _accumulator = 0f;
            }

            // 結果を Transform へ反映
            _chains.ForEach(chain => chain.Apply());
        }
    }
}
