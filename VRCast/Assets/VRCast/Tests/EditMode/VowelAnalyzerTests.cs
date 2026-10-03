using NUnit.Framework;
using UnityEngine;
using VRCast.Audio;

namespace VRCast.Tests
{
    public class VowelAnalyzerTests
    {
        // 合成音のサンプリングレート・声の高さ・フォルマントの帯域幅・第 3 フォルマント
        private const int SampleRate = 48000;
        private const float Pitch = 120f;
        private const float Bandwidth = 80f;
        private const float F3 = 2900f;

        // 解析区間（約 11kHz へ間引いて 512 サンプル）
        private static readonly int WindowLength = 512 * VowelAnalyzer.DecimationOf(SampleRate);

        [TestCase(VowelAnalyzer.A, 780f, 1320f)]
        [TestCase(VowelAnalyzer.I, 320f, 2550f)]
        [TestCase(VowelAnalyzer.U, 370f, 1450f)]
        [TestCase(VowelAnalyzer.E, 520f, 2100f)]
        [TestCase(VowelAnalyzer.O, 520f, 920f)]
        public void Analyze_SynthesizedVowel_ReturnsThatVowel(int vowel, float f1, float f2)
        {
            float[] samples = Synthesize(f1, f2, vowel == VowelAnalyzer.I ? 3300f : F3);
            var analyzer = new VowelAnalyzer();
            var weights = new float[VowelAnalyzer.VowelCount];

            // 推定でき、その母音の重みが最大で、フォルマントが F1 は 25%（低い F1 は高めに出る）・F2 は 15% 以内であること
            Assert.IsTrue(analyzer.Analyze(samples, SampleRate, 1f, weights));
            Assert.AreEqual(vowel, IndexOfMax(weights));
            Assert.That(analyzer.F1, Is.EqualTo(f1).Within(f1 * 0.25f));
            Assert.That(analyzer.F2, Is.EqualTo(f2).Within(f2 * 0.15f));
        }

        [Test]
        public void Analyze_Silence_ReturnsFalseAndKeepsWeights()
        {
            var analyzer = new VowelAnalyzer();
            var weights = new float[] { 1f, 0f, 0f, 0f, 0f };

            // 無音は推定せず、重みを変えないこと
            Assert.IsFalse(analyzer.Analyze(new float[WindowLength], SampleRate, 1f, weights));
            Assert.AreEqual(1f, weights[VowelAnalyzer.A]);
        }

        [Test]
        public void Analyze_Weights_SumToOne()
        {
            var analyzer = new VowelAnalyzer();
            var weights = new float[VowelAnalyzer.VowelCount];
            analyzer.Analyze(Synthesize(650f, 1700f, F3), SampleRate, 1f, weights);

            // 中間的な声でも合計 1 に正規化されること
            float sum = 0f;
            foreach (float weight in weights)
            {
                sum += weight;
            }

            Assert.That(sum, Is.EqualTo(1f).Within(1e-4f));
        }

        private static float[] Synthesize(float f1, float f2, float f3)
        {
            // パルス列（声帯）を 3 つの共振器（声道）に通し、立ち上がりを捨てて後半を使う
            int total = WindowLength * 2;
            var signal = new float[total];
            int period = Mathf.RoundToInt(SampleRate / Pitch);
            for (int i = 0; i < total; i += period)
            {
                signal[i] = 1f;
            }

            Resonate(signal, f1);
            Resonate(signal, f2);
            Resonate(signal, f3);

            // 振幅を -1〜1 に収める
            float peak = 1e-6f;
            foreach (float value in signal)
            {
                peak = Mathf.Max(peak, Mathf.Abs(value));
            }

            var window = new float[WindowLength];
            for (int i = 0; i < WindowLength; i++)
            {
                window[i] = signal[total - WindowLength + i] / peak * 0.5f;
            }

            return window;
        }

        private static void Resonate(float[] signal, float frequency)
        {
            // 2 次の共振器（中心周波数・帯域幅）
            float r = Mathf.Exp(-Mathf.PI * Bandwidth / SampleRate);
            float c1 = 2f * r * Mathf.Cos(2f * Mathf.PI * frequency / SampleRate);
            float c2 = -r * r;
            float y1 = 0f;
            float y2 = 0f;
            for (int i = 0; i < signal.Length; i++)
            {
                float y = signal[i] + c1 * y1 + c2 * y2;
                y2 = y1;
                y1 = y;
                signal[i] = y;
            }
        }

        private static int IndexOfMax(float[] values)
        {
            int best = 0;
            for (int i = 1; i < values.Length; i++)
            {
                best = values[i] > values[best] ? i : best;
            }

            return best;
        }
    }
}
