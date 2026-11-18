using System;
using UnityEngine;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// 3 次元の薄板スプライン（カーネルは距離 r）。
    /// 目印の対応から、手本の顔の位置をアバターの顔の位置へ写し、移動量はヤコビアンで向きを変える。
    /// </summary>
    public sealed class ThinPlateSpline
    {
        // 対角に足す滑らかさ（制御点の大きさに対する比）。0 だと点が重なったとき解けない
        private const double LambdaRatio = 1e-4d;

        // ヤコビアンで距離 0 の核を省く下限
        private const float KernelEpsilon = 1e-8f;

        private readonly Vector3[] _source;
        private readonly Vector3[] _weights;
        private readonly Vector3 _a0;
        private readonly Vector3 _ax;
        private readonly Vector3 _ay;
        private readonly Vector3 _az;

        private ThinPlateSpline(Vector3[] source, Vector3[] weights, Vector3 a0, Vector3 ax, Vector3 ay, Vector3 az)
        {
            _source = source;
            _weights = weights;
            _a0 = a0;
            _ax = ax;
            _ay = ay;
            _az = az;
        }

        /// <summary>
        /// source[i] を target[i] へ写すスプラインを求める。点が 4 未満、または解けなければ null。
        /// </summary>
        public static ThinPlateSpline Fit(Vector3[] source, Vector3[] target)
        {
            // 対応点が揃っていること
            if (source == null || target == null || source.Length != target.Length || source.Length < 4)
            {
                return null;
            }

            int count = source.Length;
            int size = count + 4;
            double scale = 0d;
            for (int i = 0; i < count; i++)
            {
                // 核の大きさを、制御点の原点からの距離の最大に合わせる
                scale = Math.Max(scale, source[i].magnitude);
            }

            // 点が原点付近だけなら、滑らかさの絶対値を小さくしすぎない
            double lambda = Math.Max(scale, 1e-3d) * LambdaRatio;
            var matrix = new double[size][];
            var rhs = new double[size][];
            for (int row = 0; row < size; row++)
            {
                matrix[row] = new double[size];
                rhs[row] = new double[3];
            }

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    // K_ij = |source_i - source_j|。対角は滑らかさだけ（自分自身の距離は 0）
                    double distance = i == j ? lambda : Vector3.Distance(source[i], source[j]);
                    matrix[i][j] = distance;
                }

                // 多項式の列（1, x, y, z）と、その転置を下の行に置く
                matrix[i][count] = 1d;
                matrix[i][count + 1] = source[i].x;
                matrix[i][count + 2] = source[i].y;
                matrix[i][count + 3] = source[i].z;
                matrix[count][i] = 1d;
                matrix[count + 1][i] = source[i].x;
                matrix[count + 2][i] = source[i].y;
                matrix[count + 3][i] = source[i].z;

                // 写り先。多項式の係数に対応する下 4 行は 0 のまま
                rhs[i][0] = target[i].x;
                rhs[i][1] = target[i].y;
                rhs[i][2] = target[i].z;
            }

            if (!Solve(matrix, rhs))
            {
                return null;
            }

            // 解は重み、その後に 1, x, y, z の係数
            var weights = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                weights[i] = new Vector3((float)rhs[i][0], (float)rhs[i][1], (float)rhs[i][2]);
            }

            return new ThinPlateSpline(
                source,
                weights,
                new Vector3((float)rhs[count][0], (float)rhs[count][1], (float)rhs[count][2]),
                new Vector3((float)rhs[count + 1][0], (float)rhs[count + 1][1], (float)rhs[count + 1][2]),
                new Vector3((float)rhs[count + 2][0], (float)rhs[count + 2][1], (float)rhs[count + 2][2]),
                new Vector3((float)rhs[count + 3][0], (float)rhs[count + 3][1], (float)rhs[count + 3][2]));
        }

        /// <summary>
        /// 手本の位置 point を、アバターの顔の局所座標へ写す。
        /// </summary>
        public Vector3 Evaluate(Vector3 point)
        {
            // アフィン部分
            Vector3 value = _a0 + _ax * point.x + _ay * point.y + _az * point.z;
            for (int i = 0; i < _source.Length; i++)
            {
                // 核 r を重み分だけ足す
                value += _weights[i] * Vector3.Distance(point, _source[i]);
            }

            return value;
        }

        /// <summary>
        /// 位置 point でのヤコビアン。戻り値の X/Y/Z は、入力の x/y/z で微分した出力の向き。
        /// 手本の移動量 d は X * d.x + Y * d.y + Z * d.z でアバター側の移動量になる。
        /// </summary>
        public void Jacobian(Vector3 point, out Vector3 x, out Vector3 y, out Vector3 z)
        {
            // アフィン部分の微分
            x = _ax;
            y = _ay;
            z = _az;
            for (int i = 0; i < _source.Length; i++)
            {
                Vector3 offset = point - _source[i];
                float distance = offset.magnitude;
                if (distance < KernelEpsilon)
                {
                    // 制御点の上では核の微分を 0 にする
                    continue;
                }

                float inverse = 1f / distance;
                Vector3 weight = _weights[i];
                x += weight * (offset.x * inverse);
                y += weight * (offset.y * inverse);
                z += weight * (offset.z * inverse);
            }
        }

        /// <summary>
        /// 連立方程式をガウス・ジョルダン法で解く。解けなければ false（行列と右辺は壊れる）。
        /// </summary>
        private static bool Solve(double[][] matrix, double[][] rhs)
        {
            int size = matrix.Length;
            int columns = rhs[0].Length;
            for (int col = 0; col < size; col++)
            {
                // この列で絶対値が最大の行を軸にする
                int pivot = col;
                double best = Math.Abs(matrix[col][col]);
                for (int row = col + 1; row < size; row++)
                {
                    double value = Math.Abs(matrix[row][col]);
                    if (value > best)
                    {
                        best = value;
                        pivot = row;
                    }
                }

                if (best < 1e-10d)
                {
                    return false;
                }

                if (pivot != col)
                {
                    // 軸の行を入れ替える
                    double[] swap = matrix[col];
                    matrix[col] = matrix[pivot];
                    matrix[pivot] = swap;
                    swap = rhs[col];
                    rhs[col] = rhs[pivot];
                    rhs[pivot] = swap;
                }

                // 軸を 1 にする
                double divisor = matrix[col][col];
                for (int k = col; k < size; k++)
                {
                    matrix[col][k] /= divisor;
                }

                for (int k = 0; k < columns; k++)
                {
                    rhs[col][k] /= divisor;
                }

                // 他の行からこの列を消す
                for (int row = 0; row < size; row++)
                {
                    if (row == col)
                    {
                        continue;
                    }

                    double factor = matrix[row][col];
                    if (factor == 0d)
                    {
                        continue;
                    }

                    for (int k = col; k < size; k++)
                    {
                        matrix[row][k] -= factor * matrix[col][k];
                    }

                    for (int k = 0; k < columns; k++)
                    {
                        rhs[row][k] -= factor * rhs[col][k];
                    }
                }
            }

            return true;
        }
    }
}
