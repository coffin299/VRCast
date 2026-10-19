using System;
using System.Collections.Generic;
using NUnit.Framework;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.Tests
{
    /// <summary>
    /// 表情のキャリブレーション（登録した顔との比較による表情の強さ・入力元ごとの記録・記録係の進行）を検証する。
    /// </summary>
    public class ExpressionCalibrationTests
    {
        private static readonly int Count = ArKitFace.BlendShapeNames.Length;

        private static float[] Face(params (string name, float value)[] values)
        {
            // 真顔の基準として全体に少し値を入れ、指定の名前だけ上書きする
            var face = new float[Count];
            for (int i = 0; i < Count; i++)
            {
                face[i] = 0.05f;
            }

            foreach ((string name, float value) in values)
            {
                face[ArKitFace.IndexOf(name)] = value;
            }

            return face;
        }

        private static float[] Lerp(float[] a, float[] b, float t)
        {
            var result = new float[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                result[i] = a[i] + (b[i] - a[i]) * t;
            }

            return result;
        }

        [Test]
        public void SampleValueCount_MatchesArKitNames()
        {
            Assert.That(ExpressionCalibrationSample.ValueCount, Is.EqualTo(Count));
        }

        [Test]
        public void Sample_MigrateValues_PadsLegacyCount()
        {
            // tongueOut 追加前の 51 要素は末尾 0 を足して有効になること
            var sample = new ExpressionCalibrationSample
            {
                expression = (int)FaceExpression.Neutral,
                values = new float[ExpressionCalibrationSample.ValueCount - 1],
            };
            Assert.That(sample.IsValid, Is.False);
            sample.MigrateValues();
            Assert.That(sample.IsValid, Is.True);
            Assert.That(sample.values.Length, Is.EqualTo(ExpressionCalibrationSample.ValueCount));
            Assert.That(sample.values[ExpressionCalibrationSample.ValueCount - 1], Is.EqualTo(0f));
        }

        [Test]
        public void Score_IsZeroAtNeutralAndOneAtRegisteredFace()
        {
            float[] neutral = Face();
            float[] sad = Face(("mouthFrownLeft", 0.6f), ("mouthFrownRight", 0.6f), ("browInnerUp", 0.5f));

            Assert.That(ExpressionCalibration.Score(neutral, neutral, sad), Is.EqualTo(0f).Within(0.0001f));
            Assert.That(ExpressionCalibration.Score(sad, neutral, sad), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(ExpressionCalibration.Score(Lerp(neutral, sad, 0.5f), neutral, sad),
                Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void Score_IsLowForADifferentExpression()
        {
            // 別の表情（笑顔）は悲しみの向きにほとんど進まないので弱いこと
            float[] neutral = Face();
            float[] sad = Face(("mouthFrownLeft", 0.6f), ("mouthFrownRight", 0.6f), ("browInnerUp", 0.5f));
            float[] smile = Face(("mouthSmileLeft", 0.8f), ("mouthSmileRight", 0.8f));

            Assert.That(ExpressionCalibration.Score(smile, neutral, sad), Is.LessThan(0.1f));
            Assert.That(ExpressionCalibration.Score(smile, neutral, smile), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Score_IgnoresGaze()
        {
            // 視線（eyeLook*）だけが違っても強さは変わらないこと
            float[] neutral = Face();
            float[] angry = Face(("browDownLeft", 0.7f), ("browDownRight", 0.7f));
            float[] lookingAway = Face(("browDownLeft", 0.7f), ("browDownRight", 0.7f), ("eyeLookOutLeft", 0.9f),
                ("eyeLookInRight", 0.9f));

            Assert.That(ExpressionCalibration.Score(lookingAway, neutral, angry), Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Apply_ReplacesOnlyRegisteredExpressionsForTheSameSource()
        {
            float[] neutral = Face();
            float[] sad = Face(("mouthFrownLeft", 0.6f), ("mouthFrownRight", 0.6f), ("browInnerUp", 0.5f));
            var samples = new List<ExpressionCalibrationSample>();
            ExpressionCalibration.Set(samples, TrackingSource.MediaPipe, FaceExpression.Neutral, neutral);
            ExpressionCalibration.Set(samples, TrackingSource.MediaPipe, FaceExpression.Sad, sad);
            var builtIn = new ExpressionScores { Smile = 0.4f, Sad = 0.1f };

            // 登録した悲しみは登録した顔との比較の値、登録の無い笑顔は組み込みの値のまま
            ExpressionScores scores = ExpressionCalibration.Apply(builtIn, sad, samples, TrackingSource.MediaPipe);
            Assert.That(scores.Sad, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(scores.Smile, Is.EqualTo(0.4f));

            // 別の入力元では使わないこと
            scores = ExpressionCalibration.Apply(builtIn, sad, samples, TrackingSource.Vmc);
            Assert.That(scores.Sad, Is.EqualTo(0.1f));

            // 真顔が無ければ使わないこと
            ExpressionCalibration.Remove(samples, TrackingSource.MediaPipe, FaceExpression.Neutral);
            scores = ExpressionCalibration.Apply(builtIn, sad, samples, TrackingSource.MediaPipe);
            Assert.That(scores.Sad, Is.EqualTo(0.1f));
        }

        [Test]
        public void Apply_MatchesWinkWithEitherEye()
        {
            float[] neutral = Face();
            float[] winkLeft = Face(("eyeBlinkLeft", 0.9f), ("eyeSquintLeft", 0.4f));
            float[] winkRight = Face(("eyeBlinkRight", 0.9f), ("eyeSquintRight", 0.4f));
            var samples = new List<ExpressionCalibrationSample>();
            ExpressionCalibration.Set(samples, TrackingSource.MediaPipe, FaceExpression.Neutral, neutral);
            ExpressionCalibration.Set(samples, TrackingSource.MediaPipe, FaceExpression.Wink, winkLeft);

            ExpressionScores scores = ExpressionCalibration.Apply(default, winkRight, samples, TrackingSource.MediaPipe);
            Assert.That(scores.Wink, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void Set_ReplacesTheSameSourceAndExpression()
        {
            var samples = new List<ExpressionCalibrationSample>();
            ExpressionCalibration.Set(samples, TrackingSource.MediaPipe, FaceExpression.Sad, Face());
            ExpressionCalibration.Set(samples, TrackingSource.MediaPipe, FaceExpression.Sad,
                Face(("mouthFrownLeft", 0.5f)));
            ExpressionCalibration.Set(samples, TrackingSource.Vmc, FaceExpression.Sad, Face());

            Assert.That(samples.Count, Is.EqualTo(2));
            ExpressionCalibrationSample found =
                ExpressionCalibration.Find(samples, TrackingSource.MediaPipe, FaceExpression.Sad);
            Assert.That(found.values[ArKitFace.IndexOf("mouthFrownLeft")], Is.EqualTo(0.5f));

            ExpressionCalibration.RemoveAll(samples, TrackingSource.MediaPipe);
            Assert.That(samples.Count, Is.EqualTo(1));
        }

        [Test]
        public void Sanitize_DropsBrokenSamples()
        {
            var settings = new AppSettings();
            settings.expressionCalibrations.Add(null);
            settings.expressionCalibrations.Add(new ExpressionCalibrationSample { values = new float[3] });
            settings.expressionCalibrations.Add(new ExpressionCalibrationSample { values = Face() });
            float[] broken = Face();
            broken[0] = float.NaN;
            settings.expressionCalibrations.Add(new ExpressionCalibrationSample { values = broken });

            settings.Sanitize();
            Assert.That(settings.expressionCalibrations.Count, Is.EqualTo(1));
        }

        [Test]
        public void Recorder_RecordsNeutralThenExpression()
        {
            var provider = new FakeProvider { Values = Face() };
            var samples = new List<ExpressionCalibrationSample>();
            var recorder = new ExpressionCalibrationRecorder();
            recorder.Start(new[] { FaceExpression.Neutral, FaceExpression.Smile }, TrackingSource.MediaPipe);

            // 真顔: 準備のあと記録して登録
            int frame = 0;
            bool stored = Run(recorder, provider, samples, ref frame);
            Assert.That(stored, Is.True);
            Assert.That(ExpressionCalibration.Find(samples, TrackingSource.MediaPipe, FaceExpression.Neutral),
                Is.Not.Null);
            Assert.That(recorder.Target, Is.EqualTo(FaceExpression.Smile));

            // 笑顔: 値を変えて記録し、平均が登録されること
            provider.Values = Face(("mouthSmileLeft", 0.8f), ("mouthSmileRight", 0.8f));
            Run(recorder, provider, samples, ref frame);
            ExpressionCalibrationSample smile =
                ExpressionCalibration.Find(samples, TrackingSource.MediaPipe, FaceExpression.Smile);
            Assert.That(smile, Is.Not.Null);
            Assert.That(smile.values[ArKitFace.IndexOf("mouthSmileLeft")], Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(recorder.IsRunning, Is.False);
            Assert.That(recorder.LastOutcome, Is.EqualTo(ExpressionCalibrationRecorder.Outcome.Completed));
        }

        [Test]
        public void Recorder_SkipsExpressionsLikeNeutral()
        {
            // 真顔と同じ顔は登録しないこと
            var provider = new FakeProvider { Values = Face() };
            var samples = new List<ExpressionCalibrationSample>();
            var recorder = new ExpressionCalibrationRecorder();
            recorder.Start(new[] { FaceExpression.Neutral, FaceExpression.Angry }, TrackingSource.MediaPipe);
            int frame = 0;
            Run(recorder, provider, samples, ref frame);
            Run(recorder, provider, samples, ref frame);

            Assert.That(ExpressionCalibration.Find(samples, TrackingSource.MediaPipe, FaceExpression.Angry), Is.Null);
            Assert.That(recorder.LastOutcome, Is.EqualTo(ExpressionCalibrationRecorder.Outcome.NotDistinct));
            Assert.That(recorder.NotDistinct, Is.EquivalentTo(new[] { FaceExpression.Angry }));
        }

        [Test]
        public void Recorder_StopsWhenSourceChanges()
        {
            var provider = new FakeProvider { Values = Face() };
            var samples = new List<ExpressionCalibrationSample>();
            var recorder = new ExpressionCalibrationRecorder();
            recorder.Start(new[] { FaceExpression.Neutral }, TrackingSource.MediaPipe);

            provider.Source = TrackingSource.Vmc;
            recorder.Tick(provider, samples, 0.1f, 1);
            Assert.That(recorder.IsRunning, Is.False);
            Assert.That(recorder.LastOutcome, Is.EqualTo(ExpressionCalibrationRecorder.Outcome.SourceChanged));
            Assert.That(samples, Is.Empty);
        }

        private static bool Run(ExpressionCalibrationRecorder recorder, FakeProvider provider,
            List<ExpressionCalibrationSample> samples, ref int frame)
        {
            // 1 つの表情の準備と記録が終わるまで 0.05 秒ずつ進める（終わったら true）
            FaceExpression target = recorder.Target;
            for (int i = 0; i < 1000 && recorder.IsRunning; i++)
            {
                frame++;
                if (recorder.Tick(provider, samples, 0.05f, frame) || recorder.Target != target || !recorder.IsRunning)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class FakeProvider : IFaceTrackingProvider
        {
            public float[] Values;
            public TrackingSource Source = TrackingSource.MediaPipe;

            public string Status => string.Empty;
            public int FramesPerSecond => 30;
            public TrackingSource FaceSource => Source;

            public bool TryGetFrame(out FaceTrackingFrame frame)
            {
                frame = new FaceTrackingFrame { BlendShapes = (float[])Values.Clone(), HasExpression = true };
                return true;
            }
        }
    }
}
