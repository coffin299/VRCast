using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Dynamics
{
    /// <summary>
    /// 1 つの PhysBone の Verlet 近似。root 以下の Transform を粒子とし、
    /// 毎フレーム静止姿勢へ戻してから粒子を進め、子粒子の方向へ各 Transform を回転させる。
    /// </summary>
    internal sealed class PhysBoneChain
    {
        // パラメーター → 物理量の換算係数（見た目で調整した近似値）
        private const float PullStrength = 0.1f;
        private const float StiffnessStrength = 0.1f;
        private const float MinMomentum = 0.6f;
        private const float MaxMomentum = 0.95f;
        private const float GravityAcceleration = 9.81f;

        // ゼロ除算を避けるための最小長
        private const float Epsilon = 1e-8f;

        private sealed class Particle
        {
            // 仮想の末端粒子（endpointPosition）は Transform を持たない
            public Transform Transform;
            public int Parent;
            public readonly List<int> Children = new List<int>();

            // 親 Transform 空間での静止位置と、自身の静止回転
            public Vector3 RestLocalPosition;
            public Quaternion RestLocalRotation;

            // Humanoid ボーンは動かさず追従のみ、回転固定は複数子 Ignore 等
            public bool Fixed;
            public bool RotationLocked;

            // 複数子 Ignore の子はチェーンの起点として位置を固定（回転は子の方向へ向ける）
            public bool Anchored;

            // root からの段数（チェーン沿いのカーブ評価用）
            public int Depth;

            // カーブ適用済みのパラメーター
            public float Pull;
            public float Momentum;
            public float Stiffness;
            public float Gravity;
            public float Immobile;
            public float Radius;
            public float MaxAngle;

            // シミュレーション状態と、フレーム開始時の静止姿勢でのワールド位置
            public Vector3 Position;
            public Vector3 PreviousPosition;
            public Vector3 RestPosition;
        }

        private readonly List<Particle> _particles = new List<Particle>();
        private readonly PhysBoneData _data;
        private readonly List<PhysBoneCollider> _colliders;
        private Vector3 _lastRootPosition;

        public int ParticleCount => _particles.Count;

        private PhysBoneChain(PhysBoneData data, List<PhysBoneCollider> colliders)
        {
            _data = data;
            _colliders = colliders;
        }

        private void ResolveParameters(float scale)
        {
            // 最も深い粒子を 1 とした位置でカーブを評価する
            int maxDepth = 0;
            foreach (Particle particle in _particles)
            {
                maxDepth = Mathf.Max(maxDepth, particle.Depth);
            }

            foreach (Particle particle in _particles)
            {
                float t = maxDepth > 0 ? particle.Depth / (float)maxDepth : 0f;

                // 基本値 × 倍率カーブ（0〜1 の量は範囲内に丸める）
                particle.Pull = Mathf.Clamp01(_data.pull * PhysBoneData.EvaluateCurve(_data.pullCurve, t));
                float spring = Mathf.Clamp01(_data.spring * PhysBoneData.EvaluateCurve(_data.springCurve, t));
                particle.Stiffness = Mathf.Clamp01(_data.stiffness * PhysBoneData.EvaluateCurve(_data.stiffnessCurve, t));
                particle.Gravity = Mathf.Clamp(_data.gravity * PhysBoneData.EvaluateCurve(_data.gravityCurve, t), -1f, 1f);
                particle.Immobile = Mathf.Clamp01(_data.immobile * PhysBoneData.EvaluateCurve(_data.immobileCurve, t));
                particle.MaxAngle = Mathf.Clamp(_data.maxAngle * PhysBoneData.EvaluateCurve(_data.maxAngleCurve, t), 0f, 180f);

                // 半径は root のスケールで拡縮
                particle.Radius = Mathf.Max(0f, _data.radius * PhysBoneData.EvaluateCurve(_data.radiusCurve, t)) * scale;

                // spring が大きいほど速度を保って揺れ続ける
                particle.Momentum = Mathf.Lerp(MinMomentum, MaxMomentum, spring);
            }
        }

        /// <summary>
        /// チェーンを構築する。root が見つからない・揺れる粒子が無い場合は null。
        /// </summary>
        public static PhysBoneChain Create(
            Transform avatarRoot, PhysBoneData data, IReadOnlyList<PhysBoneCollider> allColliders,
            HashSet<Transform> humanBones, int maxParticles)
        {
            // root の解決
            Transform root = string.IsNullOrEmpty(data.rootPath) ? avatarRoot : avatarRoot.Find(data.rootPath);
            if (root == null)
            {
                return null;
            }

            // 除外 Transform の解決（見つからないものは無視）
            var ignored = new HashSet<Transform>();
            foreach (string path in data.ignorePaths)
            {
                Transform target = string.IsNullOrEmpty(path) ? avatarRoot : avatarRoot.Find(path);
                if (target != null)
                {
                    ignored.Add(target);
                }
            }

            // 参照コライダーのうち解決できたものだけ使う
            var colliders = new List<PhysBoneCollider>();
            foreach (int index in data.colliders)
            {
                if (allColliders[index] != null)
                {
                    colliders.Add(allColliders[index]);
                }
            }

            // 階層を粒子に展開
            var chain = new PhysBoneChain(data, colliders);
            chain.AddRecursive(root, -1, false, ignored, humanBones, maxParticles);

            // root だけでは揺れるものが無い
            if (chain._particles.Count < 2)
            {
                return null;
            }

            // カーブを評価して粒子ごとのパラメーターを確定
            chain.ResolveParameters(Mathf.Abs(root.lossyScale.x));
            return chain;
        }

        private void AddRecursive(
            Transform transform, int parent, bool anchored, HashSet<Transform> ignored, HashSet<Transform> humanBones,
            int maxParticles)
        {
            // 粒子数の上限
            if (_particles.Count >= maxParticles)
            {
                return;
            }

            // 自身の粒子を追加
            int index = AddParticle(new Particle
            {
                Transform = transform,
                Parent = parent,
                RestLocalPosition = transform.localPosition,
                RestLocalRotation = transform.localRotation,
                Fixed = humanBones.Contains(transform),
                Anchored = anchored,
            });

            // 除外されていない子
            var children = new List<Transform>();
            foreach (Transform child in transform)
            {
                if (!ignored.Contains(child))
                {
                    children.Add(child);
                }
            }

            // 複数の子を持つ Ignore 設定の Transform は回転させず、子を新しいチェーンの起点にする
            Particle particle = _particles[index];
            bool ignoreMultiChild = children.Count > 1 && _data.multiChildType == PhysBoneData.MultiChildIgnore;
            particle.RotationLocked = particle.Fixed || ignoreMultiChild;

            foreach (Transform child in children)
            {
                AddRecursive(child, index, ignoreMultiChild, ignored, humanBones, maxParticles);
            }

            // 末端には endpointPosition の仮想粒子を付けて末端ボーンも回転させる
            if (children.Count == 0 && _data.endpointPosition != Vector3.zero && _particles.Count < maxParticles)
            {
                AddParticle(new Particle { Parent = index, RestLocalPosition = _data.endpointPosition });
            }
        }

        private int AddParticle(Particle particle)
        {
            // 親の子リストへ登録し段数を決める（親は常に先に追加されている）
            int index = _particles.Count;
            _particles.Add(particle);
            if (particle.Parent >= 0)
            {
                Particle parent = _particles[particle.Parent];
                parent.Children.Add(index);
                particle.Depth = parent.Depth + 1;
            }

            return index;
        }

        /// <summary>
        /// 静止姿勢へ戻し、粒子を静止位置にリセットする（有効化時）。
        /// </summary>
        public void Reset()
        {
            BeginFrame();
            foreach (Particle particle in _particles)
            {
                particle.Position = particle.RestPosition;
                particle.PreviousPosition = particle.RestPosition;
            }

            _lastRootPosition = _particles[0].RestPosition;
        }

        /// <summary>
        /// 回転を静止姿勢へ戻す（無効化時）。
        /// </summary>
        public void RestoreRestPose()
        {
            foreach (Particle particle in _particles)
            {
                if (particle.Transform != null && !particle.Fixed)
                {
                    particle.Transform.localRotation = particle.RestLocalRotation;
                }
            }
        }

        /// <summary>
        /// フレーム開始処理: 回転を静止姿勢へ戻して静止位置を記録し、アバター移動分を immobile に応じて追従させる。
        /// </summary>
        public void BeginFrame()
        {
            RestoreRestPose();

            // 親から順に静止姿勢でのワールド位置を計算（仮想粒子は親 Transform 基準）
            foreach (Particle particle in _particles)
            {
                particle.RestPosition = particle.Transform != null
                    ? particle.Transform.position
                    : _particles[particle.Parent].Transform.TransformPoint(particle.RestLocalPosition);
            }

            // root の移動量のうち immobile 分だけ粒子も一緒に動かす（1 = 遅れなし）
            Vector3 movement = _particles[0].RestPosition - _lastRootPosition;
            _lastRootPosition = _particles[0].RestPosition;
            for (int i = 1; i < _particles.Count; i++)
            {
                Vector3 delta = movement * _particles[i].Immobile;
                _particles[i].Position += delta;
                _particles[i].PreviousPosition += delta;
            }
        }

        /// <summary>
        /// 固定時間 dt だけ粒子を進める。
        /// </summary>
        public void Step(float dt)
        {
            for (int i = 0; i < _particles.Count; i++)
            {
                Particle particle = _particles[i];

                // root は静止位置に固定
                if (particle.Parent < 0)
                {
                    particle.Position = particle.RestPosition;
                    particle.PreviousPosition = particle.Position;
                    continue;
                }

                // 親粒子の現在位置 + 静止姿勢での相対ベクトルを目標とする
                Particle parent = _particles[particle.Parent];
                Vector3 restVector = particle.RestPosition - parent.RestPosition;
                Vector3 target = parent.Position + restVector;

                // 固定粒子・チェーン起点は目標（静止位置）へそのまま追従
                if (particle.Fixed || particle.Anchored)
                {
                    particle.Position = target;
                    particle.PreviousPosition = target;
                    continue;
                }

                // 慣性（spring）と目標への引き戻し（pull）
                Vector3 velocity = (particle.Position - particle.PreviousPosition) * particle.Momentum;
                velocity += (target - particle.Position) * (particle.Pull * PullStrength);

                // 重力。静止時に下を向いているほど gravityFalloff で弱める
                float downness = Mathf.Max(0f, Vector3.Dot(restVector.normalized, Vector3.down));
                float gravity = particle.Gravity * (1f - _data.gravityFalloff * downness);
                velocity += Vector3.down * (gravity * GravityAcceleration * dt * dt);

                // 位置を更新
                particle.PreviousPosition = particle.Position;
                particle.Position += velocity;

                // 形状の維持（stiffness）
                particle.Position = Vector3.Lerp(particle.Position, target, particle.Stiffness * StiffnessStrength);

                // 角度制限（静止方向からの円錐）
                if (_data.limitType == PhysBoneData.LimitAngle)
                {
                    particle.Position = LimitAngle(parent.Position, particle.Position, restVector, particle.MaxAngle);
                }

                // 長さ拘束 → ボーン線分とコライダーの衝突 → 再度長さ拘束（衝突で回転した結果を長さに戻す）
                KeepLength(particle, parent.Position, restVector, target);
                if (_colliders.Count > 0)
                {
                    // 押し出し前の位置（押し出し量を速度に含めないために使う）
                    Vector3 beforeCollision = particle.Position;
                    foreach (PhysBoneCollider collider in _colliders)
                    {
                        collider.Collide(parent.Position, ref particle.Position, particle.Radius);
                    }

                    KeepLength(particle, parent.Position, restVector, target);

                    // 押し出し分だけ前回位置もずらし、次ステップで外向きに飛ばないようにする
                    particle.PreviousPosition += particle.Position - beforeCollision;
                }
            }
        }

        private static void KeepLength(Particle particle, Vector3 parentPosition, Vector3 restVector, Vector3 target)
        {
            // 親からの距離を静止時の長さに保つ（方向が定まらなければ目標位置）
            Vector3 direction = particle.Position - parentPosition;
            particle.Position = direction.sqrMagnitude > Epsilon
                ? parentPosition + direction.normalized * restVector.magnitude
                : target;
        }

        private static Vector3 LimitAngle(Vector3 parentPosition, Vector3 position, Vector3 restVector, float maxAngle)
        {
            // 静止方向との角度が上限以内ならそのまま
            Vector3 direction = position - parentPosition;
            float angle = Vector3.Angle(restVector, direction);
            if (angle <= maxAngle || direction.sqrMagnitude < Epsilon)
            {
                return position;
            }

            // 静止方向から上限角だけ傾けた方向へ戻す
            Vector3 limited = Vector3.Slerp(restVector.normalized, direction.normalized, maxAngle / angle);
            return parentPosition + limited * direction.magnitude;
        }

        /// <summary>
        /// 粒子の位置に合わせて、親から順に各 Transform を子粒子の方向へ回転させる。
        /// </summary>
        public void Apply()
        {
            foreach (Particle particle in _particles)
            {
                // 回転させない粒子・子が無い粒子はスキップ
                if (particle.Transform == null || particle.RotationLocked || particle.Children.Count == 0)
                {
                    continue;
                }

                // First は先頭の子のみ、それ以外は全ての子の平均方向
                int count = _data.multiChildType == PhysBoneData.MultiChildFirst ? 1 : particle.Children.Count;
                Vector3 restDirection = Vector3.zero;
                Vector3 currentDirection = Vector3.zero;
                for (int k = 0; k < count; k++)
                {
                    Particle child = _particles[particle.Children[k]];
                    restDirection += particle.Transform.TransformVector(child.RestLocalPosition);
                    currentDirection += child.Position - particle.Transform.position;
                }

                // 長さが無ければ回転できない
                if (restDirection.sqrMagnitude < Epsilon || currentDirection.sqrMagnitude < Epsilon)
                {
                    continue;
                }

                // 静止方向を現在の方向へ向ける回転を追加
                particle.Transform.rotation =
                    Quaternion.FromToRotation(restDirection, currentDirection) * particle.Transform.rotation;
            }
        }
    }
}
