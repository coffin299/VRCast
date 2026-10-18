using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Dynamics
{
    /// <summary>
    /// アバターの全 PhysBone を固定タイムステップで更新する。AppSettings.physicsEnabled で全体の ON/OFF、
    /// チェーンごとの Enabled で個別に止められる。
    /// </summary>
    public class PhysBoneSimulator : MonoBehaviour
    {
        // ログのカテゴリ名
        private const string LogCategory = "PhysBone";

        // 固定タイムステップと 1 フレームの最大ステップ数（低 FPS 時の暴走防止）
        internal const float TimeStep = 1f / 60f;
        private const int MaxSubSteps = 3;

        // アバター 1 体あたりの粒子数上限（負荷対策）
        private const int MaxParticles = 4096;

        /// <summary>
        /// 一覧・個別 ON/OFF 用の 1 チェーン。
        /// </summary>
        public sealed class Chain
        {
            // アバターのルートからの相対パスと、Unity 上の root オブジェクト名
            public string Path;
            public string NodeName;
            public bool Enabled;

            internal PhysBoneChain Source;

            /// <summary>
            /// 画面に出す名前（root のオブジェクト名）。
            /// </summary>
            public string Label => NodeName;
        }

        private readonly List<PhysBoneChain> _chains = new List<PhysBoneChain>();
        private readonly List<Chain> _entries = new List<Chain>();
        private readonly List<PhysBoneCollider> _colliders = new List<PhysBoneCollider>();
        private AppSettings _settings;
        private float _accumulator;
        private bool _wasEnabled;

        // 直前フレームで動かしていたか（個別に OFF → ON したときに静止位置から始める）
        private readonly List<bool> _wasChainEnabled = new List<bool>();

        public int ChainCount => _chains.Count;

        public bool IsAvailable => _chains.Count > 0;

        /// <summary>
        /// 構築済みのチェーン一覧（個別 ON/OFF 用）。
        /// </summary>
        public IReadOnlyList<Chain> Chains => _entries;

        /// <summary>
        /// ON にしているチェーンの数。
        /// </summary>
        public int EnabledCount
        {
            get
            {
                int count = 0;
                foreach (Chain chain in _entries)
                {
                    count += chain.Enabled ? 1 : 0;
                }

                return count;
            }
        }

        /// <summary>
        /// true の間は設定に関係なく静止姿勢で止める（パーフェクトシンク化の編集中など）。
        /// </summary>
        public bool Suspended { get; set; }

        public void Initialize(
            Animator animator, PhysBoneSet set, AppSettings settings, List<PhysBoneToggle> toggles)
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
                    _entries.Add(new Chain
                    {
                        Path = chain.RootPath,
                        NodeName = chain.RootName,
                        Enabled = true,
                        Source = chain,
                    });
                    _wasChainEnabled.Add(true);
                    particles += chain.ParticleCount;
                }
            }

            // 記録済みの ON/OFF を当てる（見つからないパスは無視。無いものは ON のまま）
            if (toggles != null)
            {
                foreach (PhysBoneToggle toggle in toggles)
                {
                    if (toggle == null || toggle.path == null)
                    {
                        continue;
                    }

                    Chain entry = _entries.Find(chain => chain.Path == toggle.path);
                    if (entry != null)
                    {
                        SetEnabled(entry, toggle.enabled, true);
                    }
                }
            }

            VRCastLog.Info(LogCategory,
                $"Built {_chains.Count}/{set.bones.Length} chains, {particles} particles, {_colliders.Count} colliders.");

            // スケール起因の不具合を切り分けるため、ワールド空間でのコライダー寸法を記録
            foreach (PhysBoneCollider collider in _colliders)
            {
                collider.UpdateWorld();
                VRCastLog.Info(LogCategory, "Collider " + collider.Describe());
            }
        }

        /// <summary>
        /// チェーンの揺れを ON/OFF する。
        /// </summary>
        public void SetEnabled(Chain chain, bool enabled)
        {
            SetEnabled(chain, enabled, false);
        }

        /// <summary>
        /// 全てのチェーンを ON/OFF する。
        /// </summary>
        public void SetAll(bool enabled)
        {
            foreach (Chain chain in _entries)
            {
                SetEnabled(chain, enabled, false);
            }
        }

        /// <summary>
        /// 保存用に、全てのチェーンの ON/OFF を書き出す。
        /// </summary>
        public List<PhysBoneToggle> Export()
        {
            return _entries.ConvertAll(chain => new PhysBoneToggle
            {
                path = chain.Path,
                enabled = chain.Enabled,
            });
        }

        private void SetEnabled(Chain chain, bool enabled, bool force)
        {
            if (!force && chain.Enabled == enabled)
            {
                return;
            }

            chain.Enabled = enabled;

            // OFF にした瞬間は静止姿勢へ戻す（ON にしたときの Reset は LateUpdate 側）
            if (!enabled)
            {
                chain.Source.RestoreRestPose();
            }
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

            // OFF・一時停止中は静止姿勢へ戻して停止
            if (!_settings.physicsEnabled || Suspended)
            {
                if (_wasEnabled)
                {
                    _chains.ForEach(chain => chain.RestoreRestPose());
                    _wasEnabled = false;
                    for (int i = 0; i < _wasChainEnabled.Count; i++)
                    {
                        _wasChainEnabled[i] = false;
                    }
                }

                return;
            }

            // ON にされた直後は粒子を静止位置から始める
            if (!_wasEnabled)
            {
                for (int i = 0; i < _chains.Count; i++)
                {
                    if (_entries[i].Enabled)
                    {
                        _chains[i].Reset();
                        _wasChainEnabled[i] = true;
                    }
                    else
                    {
                        _wasChainEnabled[i] = false;
                    }
                }

                _accumulator = 0f;
                _wasEnabled = true;
            }

            // 個別に ON にされたチェーンは静止位置から始める。OFF のままなら静止姿勢を保つ
            for (int i = 0; i < _chains.Count; i++)
            {
                bool enabled = _entries[i].Enabled;
                if (enabled && !_wasChainEnabled[i])
                {
                    _chains[i].Reset();
                }
                else if (!enabled && _wasChainEnabled[i])
                {
                    _chains[i].RestoreRestPose();
                }

                _wasChainEnabled[i] = enabled;
            }

            // 静止姿勢・コライダー位置を更新（OFF のチェーンは姿勢だけそろえる）
            for (int i = 0; i < _chains.Count; i++)
            {
                if (_entries[i].Enabled)
                {
                    _chains[i].BeginFrame();
                }
                else
                {
                    _chains[i].RestoreRestPose();
                }
            }

            _colliders.ForEach(collider => collider.UpdateWorld());

            // 経過時間分だけ固定ステップで進める（上限を超えた分は捨てる）
            _accumulator += Time.deltaTime;
            int steps = 0;
            while (_accumulator >= TimeStep && steps < MaxSubSteps)
            {
                for (int i = 0; i < _chains.Count; i++)
                {
                    if (_entries[i].Enabled)
                    {
                        _chains[i].Step(TimeStep);
                    }
                }

                _accumulator -= TimeStep;
                steps++;
            }

            if (steps == MaxSubSteps)
            {
                _accumulator = 0f;
            }

            // 結果を Transform へ反映
            for (int i = 0; i < _chains.Count; i++)
            {
                if (_entries[i].Enabled)
                {
                    _chains[i].Apply();
                }
            }
        }
    }
}
