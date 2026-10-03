using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Dynamics
{
    /// <summary>
    /// 1 つの Constraint を解決済みの Transform で評価する。結果はターゲットのローカル値として書き込む。
    /// 重み 0・ソース無しは静止値、影響しない軸は現在値を保つ（Unity / VRC Constraint と同じ考え方）。
    /// </summary>
    public sealed class ConstraintEvaluator
    {
        // ほぼ 0 とみなす閾値
        private const float Epsilon = 1e-6f;

        private readonly ConstraintData _data;
        private readonly Transform[] _sources;
        private readonly ConstraintSourceData[] _sourceData;
        private readonly Transform _worldUp;

        // 正規化済みの静止回転・オフセット回転
        private readonly Quaternion _rotationAtRest;
        private readonly Quaternion _rotationOffset;

        public Transform Target { get; }

        private ConstraintEvaluator(
            ConstraintData data, Transform target, Transform[] sources, ConstraintSourceData[] sourceData, Transform worldUp)
        {
            _data = data;
            Target = target;
            _sources = sources;
            _sourceData = sourceData;
            _worldUp = worldUp;
            _rotationAtRest = Normalize(data.rotationAtRest);
            _rotationOffset = Normalize(data.rotationOffset);
        }

        /// <summary>
        /// パスを解決して評価器を作る。ターゲットが見つからない・アバタールート自身なら null。
        /// </summary>
        public static ConstraintEvaluator Create(Transform avatarRoot, ConstraintData data)
        {
            // ルートは配置をアプリ側で管理するため動かさない
            Transform target = Resolve(avatarRoot, data.targetPath);
            if (target == null || target == avatarRoot)
            {
                return null;
            }

            // 見つからないソースは除外
            var sources = new List<Transform>();
            var sourceData = new List<ConstraintSourceData>();
            foreach (ConstraintSourceData source in data.sources)
            {
                Transform transform = Resolve(avatarRoot, source.path);
                if (transform != null)
                {
                    sources.Add(transform);
                    sourceData.Add(source);
                }
            }

            // 上方向の参照（空なら無し）
            Transform worldUp = string.IsNullOrEmpty(data.worldUpPath) ? null : Resolve(avatarRoot, data.worldUpPath);
            return new ConstraintEvaluator(data, target, sources.ToArray(), sourceData.ToArray(), worldUp);
        }

        private static Transform Resolve(Transform avatarRoot, string path)
        {
            // 空はルート自身
            return string.IsNullOrEmpty(path) ? avatarRoot : avatarRoot.Find(path);
        }

        /// <summary>
        /// other のターゲット（またはその子孫）を参照しているか。評価順の決定に使う。
        /// </summary>
        public bool DependsOn(ConstraintEvaluator other)
        {
            // ソースと上方向の参照のいずれかが other のターゲット配下
            foreach (Transform source in _sources)
            {
                if (source.IsChildOf(other.Target))
                {
                    return true;
                }
            }

            return _worldUp != null && _worldUp.IsChildOf(other.Target);
        }

        public void Evaluate()
        {
            // 無効、または破棄・非表示のターゲットは触らない
            if (!_data.active || Target == null || !Target.gameObject.activeInHierarchy)
            {
                return;
            }

            // 有効なソースの合計重み（0 なら静止値）
            float total = TotalWeight();
            switch (_data.type)
            {
                case ConstraintData.TypePosition:
                    EvaluatePosition(total);
                    break;
                case ConstraintData.TypeRotation:
                    EvaluateRotation(total);
                    break;
                case ConstraintData.TypeScale:
                    EvaluateScale(total);
                    break;
                case ConstraintData.TypeParent:
                    EvaluateParent(total);
                    break;
                case ConstraintData.TypeAim:
                case ConstraintData.TypeLookAt:
                    EvaluateAim(total);
                    break;
            }
        }

        private float TotalWeight()
        {
            // 破棄済みのソースは数えない
            float total = 0f;
            for (int i = 0; i < _sources.Length; i++)
            {
                if (_sources[i] != null)
                {
                    total += _sourceData[i].weight;
                }
            }

            return total;
        }

        private void EvaluatePosition(float total)
        {
            Vector3 local = _data.positionAtRest;
            if (total > Epsilon)
            {
                // ソース位置の重み付き平均（ローカル空間指定ならソースのローカル値）
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < _sources.Length; i++)
                {
                    if (_sources[i] != null)
                    {
                        sum += (_data.localSpace ? _sources[i].localPosition : _sources[i].position) * _sourceData[i].weight;
                    }
                }

                // 親空間へ変換してオフセットを加え、静止値から重みぶん寄せる
                Vector3 solved = ToLocalPoint(sum / total) + _data.positionOffset;
                local = Vector3.Lerp(_data.positionAtRest, solved, _data.weight);
            }

            Target.localPosition = Mask(local, Target.localPosition, _data.positionAxes);
        }

        private void EvaluateRotation(float total)
        {
            Quaternion local = _rotationAtRest;
            if (total > Epsilon)
            {
                // ソース回転の重み付き平均
                Vector4 sum = Vector4.zero;
                for (int i = 0; i < _sources.Length; i++)
                {
                    if (_sources[i] != null)
                    {
                        Accumulate(ref sum, _data.localSpace ? _sources[i].localRotation : _sources[i].rotation, _sourceData[i].weight);
                    }
                }

                // 親空間へ変換してオフセットを掛け、静止値から重みぶん寄せる
                Quaternion solved = ToLocalRotation(ToQuaternion(sum)) * _rotationOffset;
                local = Quaternion.Slerp(_rotationAtRest, solved, _data.weight);
            }

            Target.localRotation = Mask(local, Target.localRotation, _data.rotationAxes);
        }

        private void EvaluateScale(float total)
        {
            Vector3 local = _data.scaleAtRest;
            if (total > Epsilon)
            {
                // ソースのスケール（ワールドは lossyScale）の重み付き平均
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < _sources.Length; i++)
                {
                    if (_sources[i] != null)
                    {
                        sum += (_data.localSpace ? _sources[i].localScale : _sources[i].lossyScale) * _sourceData[i].weight;
                    }
                }

                // オフセット倍率を掛け、ワールド指定なら親のスケールで割ってローカルへ
                Vector3 solved = Vector3.Scale(sum / total, _data.scaleOffset);
                if (!_data.localSpace && Target.parent != null)
                {
                    solved = Divide(solved, Target.parent.lossyScale);
                }

                local = Vector3.Lerp(_data.scaleAtRest, solved, _data.weight);
            }

            Target.localScale = Mask(local, Target.localScale, _data.scaleAxes);
        }

        private void EvaluateParent(float total)
        {
            Vector3 position = _data.positionAtRest;
            Quaternion rotation = _rotationAtRest;
            if (total > Epsilon)
            {
                // 各ソースにオフセットを付けた姿勢の重み付き平均
                Vector3 positionSum = Vector3.zero;
                Vector4 rotationSum = Vector4.zero;
                for (int i = 0; i < _sources.Length; i++)
                {
                    Transform source = _sources[i];
                    if (source == null)
                    {
                        continue;
                    }

                    // オフセットはソースのローカル空間（スケール込み）
                    ConstraintSourceData data = _sourceData[i];
                    Quaternion offsetRotation = Normalize(data.rotationOffset);
                    Vector3 point = _data.localSpace
                        ? source.localPosition + source.localRotation * Vector3.Scale(source.localScale, data.positionOffset)
                        : source.TransformPoint(data.positionOffset);
                    Quaternion orientation = (_data.localSpace ? source.localRotation : source.rotation) * offsetRotation;
                    positionSum += point * data.weight;
                    Accumulate(ref rotationSum, orientation, data.weight);
                }

                // 親空間へ変換し、静止値から重みぶん寄せる
                position = Vector3.Lerp(_data.positionAtRest, ToLocalPoint(positionSum / total), _data.weight);
                rotation = Quaternion.Slerp(_rotationAtRest, ToLocalRotation(ToQuaternion(rotationSum)), _data.weight);
            }

            Target.localPosition = Mask(position, Target.localPosition, _data.positionAxes);
            Target.localRotation = Mask(rotation, Target.localRotation, _data.rotationAxes);
        }

        private void EvaluateAim(float total)
        {
            Quaternion local = _rotationAtRest;
            if (total > Epsilon)
            {
                // 向く先はソース位置の重み付き平均
                Vector3 sum = Vector3.zero;
                for (int i = 0; i < _sources.Length; i++)
                {
                    if (_sources[i] != null)
                    {
                        sum += _sources[i].position * _sourceData[i].weight;
                    }
                }

                // ソースと重なっている等で向きが決まらない場合は現状維持
                Vector3 direction = sum / total - Target.position;
                if (direction.sqrMagnitude < Epsilon)
                {
                    return;
                }

                // ワールド回転を求めて親空間へ変換し、オフセットを掛けて静止値から重みぶん寄せる
                Quaternion world = _data.type == ConstraintData.TypeLookAt ? LookAt(direction) : Aim(direction);
                Quaternion solved = ToLocalRotation(world) * _rotationOffset;
                local = Quaternion.Slerp(_rotationAtRest, solved, _data.weight);
            }

            Target.localRotation = Mask(local, Target.localRotation, _data.rotationAxes);
        }

        private Quaternion Aim(Vector3 direction)
        {
            // 上方向を使わない場合は静止姿勢から最小回転で向ける
            Quaternion restWorld = ToWorldRotation(_rotationAtRest);
            if (_data.worldUpType == ConstraintData.UpNone)
            {
                return Quaternion.FromToRotation(restWorld * _data.aimAxis, direction) * restWorld;
            }

            // 上方向の決め方ごとのワールド上ベクトル
            Vector3 up;
            switch (_data.worldUpType)
            {
                case ConstraintData.UpObject:
                    up = _worldUp != null ? _worldUp.position - Target.position : Vector3.up;
                    break;
                case ConstraintData.UpObjectRotation:
                    up = _worldUp != null ? _worldUp.rotation * _data.worldUpVector : _data.worldUpVector;
                    break;
                case ConstraintData.UpVector:
                    up = _data.worldUpVector;
                    break;
                default:
                    up = Vector3.up;
                    break;
            }

            // 「aimAxis → 前、upAxis → 上」の基準を打ち消してから、前を direction・上を up へ向ける
            Quaternion basis = Quaternion.LookRotation(_data.aimAxis, _data.upAxis);
            return Quaternion.LookRotation(direction, SafeUp(direction, up, restWorld * _data.upAxis)) * Quaternion.Inverse(basis);
        }

        private Quaternion LookAt(Vector3 direction)
        {
            // Z 軸をソースへ、Y 軸を上へ向けてから視線軸まわりに roll 回す
            Vector3 up = _data.useUpObject && _worldUp != null ? _worldUp.up : Vector3.up;
            Vector3 fallback = ToWorldRotation(_rotationAtRest) * Vector3.up;
            return Quaternion.LookRotation(direction, SafeUp(direction, up, fallback)) * Quaternion.Euler(0f, 0f, _data.roll);
        }

        private static Vector3 SafeUp(Vector3 direction, Vector3 up, Vector3 fallback)
        {
            // 上ベクトルが視線と平行（または 0）なら代わりを使う
            return Vector3.Cross(direction, up).sqrMagnitude > Epsilon ? up : fallback;
        }

        private Vector3 ToLocalPoint(Vector3 point)
        {
            // ローカル空間指定・親無しはそのまま
            return _data.localSpace || Target.parent == null ? point : Target.parent.InverseTransformPoint(point);
        }

        private Quaternion ToLocalRotation(Quaternion rotation)
        {
            // ローカル空間指定・親無しはそのまま
            return _data.localSpace || Target.parent == null ? rotation : Quaternion.Inverse(Target.parent.rotation) * rotation;
        }

        private Quaternion ToWorldRotation(Quaternion local)
        {
            // 親無しはそのまま
            return Target.parent == null ? local : Target.parent.rotation * local;
        }

        private static void Accumulate(ref Vector4 sum, Quaternion rotation, float weight)
        {
            // 同じ向きの q と -q が打ち消し合わないよう、合計と同じ半球へ揃えてから加算
            var value = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
            if (Vector4.Dot(sum, value) < 0f)
            {
                value = -value;
            }

            sum += value * weight;
        }

        private static Quaternion ToQuaternion(Vector4 sum)
        {
            // 合計を正規化して回転へ（打ち消し合って 0 なら無回転）
            float length = sum.magnitude;
            return length < Epsilon
                ? Quaternion.identity
                : new Quaternion(sum.x / length, sum.y / length, sum.z / length, sum.w / length);
        }

        private static Quaternion Normalize(Quaternion rotation)
        {
            // JSON 往復での誤差を除去（長さ 0 は検証で弾かれる）
            return ToQuaternion(new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));
        }

        private static Vector3 Divide(Vector3 value, Vector3 divisor)
        {
            // 0 に近い成分は割らない
            return new Vector3(
                Mathf.Abs(divisor.x) > Epsilon ? value.x / divisor.x : value.x,
                Mathf.Abs(divisor.y) > Epsilon ? value.y / divisor.y : value.y,
                Mathf.Abs(divisor.z) > Epsilon ? value.z / divisor.z : value.z);
        }

        private static Vector3 Mask(Vector3 value, Vector3 current, int axes)
        {
            // 影響しない軸は現在値を保つ
            return new Vector3(
                (axes & 1) != 0 ? value.x : current.x,
                (axes & 2) != 0 ? value.y : current.y,
                (axes & 4) != 0 ? value.z : current.z);
        }

        private static Quaternion Mask(Quaternion value, Quaternion current, int axes)
        {
            // 全軸ならそのまま、無しなら現在値
            if (axes == ConstraintData.AllAxes)
            {
                return value;
            }

            if (axes == 0)
            {
                return current;
            }

            // 一部の軸はオイラー角の成分ごとに選ぶ
            return Quaternion.Euler(Mask(value.eulerAngles, current.eulerAngles, axes));
        }
    }
}
