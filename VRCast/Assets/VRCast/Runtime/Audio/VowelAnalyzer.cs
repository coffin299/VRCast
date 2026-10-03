using UnityEngine;

namespace VRCast.Audio
{
    /// <summary>
    /// マイク音声の短い区間から母音（あいうえお）の近さを推定する。
    /// 約 11kHz へ間引き → 高域強調 → 窓掛け → LPC（線形予測）でスペクトル包絡を求め、
    /// 第 1・第 2 フォルマント（F1 / F2）を各母音の代表値と対数周波数上で比べて重みにする。
    /// 追加ライブラリなし・1 回あたり数千回の積和で済む軽量な近似。
    /// </summary>
    public sealed class VowelAnalyzer
    {
        // 母音の数と並び（重み配列の添字）
        public const int VowelCount = 5;
        public const int A = 0;
        public const int I = 1;
        public const int U = 2;
        public const int E = 3;
        public const int O = 4;

        // 間引き後の目標レート・LPC 次数・高域強調係数
        private const float TargetRate = 11025f;
        private const int Order = 12;
        private const float PreEmphasis = 0.97f;

        // 包絡を調べる上限周波数と分割数（25Hz 刻み）
        private const float MaxFrequency = 4000f;
        private const int EnvelopeBins = 160;

        // フォルマントを探す範囲（Hz）と、F1 と F2 の最小間隔
        private const float MinF1 = 200f;
        private const float MaxF1 = 1100f;
        private const float MaxF2 = 3200f;
        private const float MinGap = 150f;

        // 代表値との距離の許容幅（対数周波数、小さいほど判定がはっきりする）
        private const float SigmaF1 = 0.18f;
        private const float SigmaF2 = 0.15f;

        // 各母音の代表フォルマント（F1, F2）。日本語の成人男性・女性の中間程度
        private static readonly Vector2[] Prototypes =
        {
            new Vector2(780f, 1320f),
            new Vector2(320f, 2550f),
            new Vector2(370f, 1450f),
            new Vector2(520f, 2100f),
            new Vector2(520f, 920f),
        };

        private readonly float[] _autocorrelation = new float[Order + 1];
        private readonly float[] _lpc = new float[Order + 1];
        private readonly float[] _previous = new float[Order + 1];
        private readonly float[] _envelope = new float[EnvelopeBins];
        private float[] _work = new float[0];

        /// <summary>
        /// 直近の推定で求めた第 1・第 2 フォルマント（Hz、推定できなかった場合は前回の値）。
        /// </summary>
        public float F1 { get; private set; }

        public float F2 { get; private set; }

        /// <summary>
        /// サンプリングレートに対する間引き率（約 11kHz になる整数、1 以上）。
        /// </summary>
        public static int DecimationOf(int sampleRate)
        {
            return Mathf.Max(1, Mathf.RoundToInt(sampleRate / TargetRate));
        }

        /// <summary>
        /// samples（モノラル、-1〜1）の母音の重み（合計 1）を weights へ書く。推定できなければ false で weights は変えない。
        /// </summary>
        /// <param name="formantScale">代表値に掛ける倍率（声が高い・子供は大きく、低い声は小さく）</param>
        public bool Analyze(float[] samples, int sampleRate, float formantScale, float[] weights)
        {
            // 間引き後の長さが LPC に足りなければ推定しない
            int factor = DecimationOf(sampleRate);
            int count = samples.Length / factor;
            if (count < Order * 4 || weights.Length < VowelCount)
            {
                return false;
            }

            float rate = sampleRate / (float)factor;
            Prepare(samples, factor, count);
            if (!SolveLpc(count))
            {
                return false;
            }

            ComputeEnvelope(rate);
            if (!FindFormants(out float f1, out float f2))
            {
                return false;
            }

            F1 = f1;
            F2 = f2;
            Classify(f1, f2, Mathf.Max(formantScale, 0.01f), weights);
            return true;
        }

        private void Prepare(float[] samples, int factor, int count)
        {
            // 作業領域を確保（長さが変わったときだけ）
            if (_work.Length != count)
            {
                _work = new float[count];
            }

            // 平均で間引き（簡易ローパス）、高域強調で F2 以上を持ち上げる
            float previous = 0f;
            for (int i = 0; i < count; i++)
            {
                float sum = 0f;
                for (int j = 0; j < factor; j++)
                {
                    sum += samples[i * factor + j];
                }

                float value = sum / factor;
                _work[i] = value - PreEmphasis * previous;
                previous = value;
            }

            // ハミング窓で区間の端の影響を抑える
            float step = 2f * Mathf.PI / (count - 1);
            for (int i = 0; i < count; i++)
            {
                _work[i] *= 0.54f - 0.46f * Mathf.Cos(step * i);
            }
        }

        private bool SolveLpc(int count)
        {
            // 自己相関
            for (int lag = 0; lag <= Order; lag++)
            {
                float sum = 0f;
                for (int i = lag; i < count; i++)
                {
                    sum += _work[i] * _work[i - lag];
                }

                _autocorrelation[lag] = sum;
            }

            // 無音（数値的に解けない）
            if (_autocorrelation[0] <= 1e-9f)
            {
                return false;
            }

            // 安定化のため対角を僅かに持ち上げる
            _autocorrelation[0] *= 1.0001f;

            // Levinson-Durbin 法で予測係数 a[0..Order]（a[0] = 1）を求める
            System.Array.Clear(_lpc, 0, _lpc.Length);
            _lpc[0] = 1f;
            float error = _autocorrelation[0];
            for (int i = 1; i <= Order; i++)
            {
                // 反射係数
                float acc = _autocorrelation[i];
                for (int j = 1; j < i; j++)
                {
                    acc += _lpc[j] * _autocorrelation[i - j];
                }

                float k = -acc / error;

                // 係数の更新（前回値を写してから対称に足し込む）
                System.Array.Copy(_lpc, _previous, i);
                for (int j = 1; j < i; j++)
                {
                    _lpc[j] = _previous[j] + k * _previous[i - j];
                }

                _lpc[i] = k;
                error *= 1f - k * k;

                // 誤差が尽きたら不安定
                if (error <= 0f)
                {
                    return false;
                }
            }

            return true;
        }

        private void ComputeEnvelope(float rate)
        {
            // 各周波数で 1 / |A(e^jw)|^2（LPC スペクトル包絡）
            for (int b = 0; b < EnvelopeBins; b++)
            {
                float omega = 2f * Mathf.PI * BinFrequency(b) / rate;
                float re = 0f;
                float im = 0f;
                for (int k = 0; k <= Order; k++)
                {
                    re += _lpc[k] * Mathf.Cos(k * omega);
                    im -= _lpc[k] * Mathf.Sin(k * omega);
                }

                _envelope[b] = 1f / Mathf.Max(re * re + im * im, 1e-12f);
            }
        }

        private bool FindFormants(out float f1, out float f2)
        {
            f1 = 0f;
            f2 = 0f;

            // 包絡の山を低い方から見て、F1 の範囲の最初の山と、その上の最初の山を採る
            for (int b = 1; b < EnvelopeBins - 1; b++)
            {
                // 山（両隣より大きい）でなければ飛ばす
                if (_envelope[b] <= _envelope[b - 1] || _envelope[b] < _envelope[b + 1])
                {
                    continue;
                }

                float frequency = RefinePeak(b);
                if (f1 == 0f)
                {
                    // F1 の範囲より上で最初の山が見つかった（F1 なし）
                    if (frequency > MaxF1)
                    {
                        return false;
                    }

                    // F1 の範囲内なら採用
                    if (frequency >= MinF1)
                    {
                        f1 = frequency;
                    }
                }
                else if (frequency >= f1 + MinGap && frequency <= MaxF2)
                {
                    f2 = frequency;
                    return true;
                }
            }

            return false;
        }

        private float RefinePeak(int bin)
        {
            // 両隣との放物線補間で刻み幅より細かく山の位置を求める
            float left = _envelope[bin - 1];
            float center = _envelope[bin];
            float right = _envelope[bin + 1];
            float denominator = left - 2f * center + right;
            float offset = Mathf.Abs(denominator) > 1e-12f ? 0.5f * (left - right) / denominator : 0f;
            return BinFrequency(bin) + Mathf.Clamp(offset, -0.5f, 0.5f) * (MaxFrequency / EnvelopeBins);
        }

        private static float BinFrequency(int bin)
        {
            return (bin + 1) * (MaxFrequency / EnvelopeBins);
        }

        private static void Classify(float f1, float f2, float scale, float[] weights)
        {
            // 代表値との対数周波数の距離をガウス重みにする
            float total = 0f;
            int nearest = 0;
            float nearestDistance = float.MaxValue;
            for (int v = 0; v < VowelCount; v++)
            {
                float d1 = Mathf.Log(f1 / (Prototypes[v].x * scale)) / SigmaF1;
                float d2 = Mathf.Log(f2 / (Prototypes[v].y * scale)) / SigmaF2;
                float distance = d1 * d1 + d2 * d2;
                weights[v] = Mathf.Exp(-0.5f * distance);
                total += weights[v];

                // 全て遠い場合に備えて最も近い母音を覚える
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = v;
                }
            }

            // 合計 1 に正規化（全て極端に遠ければ最も近い母音だけ）
            for (int v = 0; v < VowelCount; v++)
            {
                weights[v] = total > 1e-6f ? weights[v] / total : (v == nearest ? 1f : 0f);
            }
        }
    }
}
