using System;

namespace VRCast.Mascot
{
    public enum MascotSpeechProblem
    {
        None,
        Unavailable,
        Microphone,
        Network,
        Failed,
    }

    /// <summary>
    /// マスコットへの音声入力。Windows の音声認識で一文を確定し、読み上げ中はマイクを離す。
    /// </summary>
    public sealed class MascotSpeechInput : IDisposable
    {
        private string _pending = string.Empty;
        private bool _suppressComplete;
        private bool _open;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private UnityEngine.Windows.Speech.DictationRecognizer _recognizer;
#endif

        public bool Listening { get; private set; }

        public string Hypothesis { get; private set; } = string.Empty;

        public MascotSpeechProblem Problem { get; private set; }

        /// <summary>
        /// 空白だけの認識結果は捨て、前後の空白は除く。
        /// </summary>
        public static string Normalize(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return text.Trim();
        }

        /// <summary>
        /// enable のあいだ聞き取る。false のあいだは認識を止めてマイクを返す。
        /// </summary>
        public void Tick(bool enable)
        {
            if (!enable)
            {
                if (_open || Listening)
                {
                    Shutdown();
                }

                return;
            }

            if (Problem != MascotSpeechProblem.None)
            {
                return;
            }

            if (!Listening)
            {
                TryStart();
            }
        }

        public bool TryTake(out string text)
        {
            text = _pending;
            _pending = string.Empty;
            return !string.IsNullOrEmpty(text);
        }

        public void Dispose()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            _suppressComplete = true;
            Listening = false;
            Hypothesis = string.Empty;
            Problem = MascotSpeechProblem.None;
            _open = false;
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (_recognizer != null)
            {
                if (Listening)
                {
                    _recognizer.Stop();
                }

                _recognizer.DictationResult -= OnResult;
                _recognizer.DictationHypothesis -= OnHypothesis;
                _recognizer.DictationComplete -= OnComplete;
                _recognizer.DictationError -= OnError;
                _recognizer.Dispose();
                _recognizer = null;
            }
#endif
            _suppressComplete = false;
        }

        private void TryStart()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                if (_recognizer == null)
                {
                    _recognizer = new UnityEngine.Windows.Speech.DictationRecognizer();
                    // 少し間を置いたら一文として確定する。無音が続いても聞き取りはやり直す
                    _recognizer.AutoSilenceTimeoutSeconds = 1.2f;
                    _recognizer.InitialSilenceTimeoutSeconds = 10f;
                    _recognizer.DictationResult += OnResult;
                    _recognizer.DictationHypothesis += OnHypothesis;
                    _recognizer.DictationComplete += OnComplete;
                    _recognizer.DictationError += OnError;
                }

                _open = true;
                if (Listening)
                {
                    return;
                }

                _recognizer.Start();
                Listening = true;
            }
            catch (Exception)
            {
                Problem = MascotSpeechProblem.Failed;
                Listening = false;
            }
#else
            Problem = MascotSpeechProblem.Unavailable;
#endif
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        private void OnResult(string text, UnityEngine.Windows.Speech.ConfidenceLevel confidence)
        {
            if (confidence == UnityEngine.Windows.Speech.ConfidenceLevel.Rejected)
            {
                return;
            }

            string said = Normalize(text);
            if (said != null)
            {
                _pending = said;
            }

            Hypothesis = string.Empty;
        }

        private void OnHypothesis(string text)
        {
            Hypothesis = text ?? string.Empty;
        }

        private void OnComplete(UnityEngine.Windows.Speech.DictationCompletionCause cause)
        {
            Listening = false;
            if (_suppressComplete)
            {
                return;
            }

            switch (cause)
            {
                case UnityEngine.Windows.Speech.DictationCompletionCause.Complete:
                case UnityEngine.Windows.Speech.DictationCompletionCause.TimeoutExceeded:
                case UnityEngine.Windows.Speech.DictationCompletionCause.PauseLimitExceeded:
                case UnityEngine.Windows.Speech.DictationCompletionCause.Canceled:
                    break;
                case UnityEngine.Windows.Speech.DictationCompletionCause.MicrophoneUnavailable:
                    Problem = MascotSpeechProblem.Microphone;
                    break;
                case UnityEngine.Windows.Speech.DictationCompletionCause.NetworkFailure:
                    Problem = MascotSpeechProblem.Network;
                    break;
                default:
                    Problem = MascotSpeechProblem.Failed;
                    break;
            }
        }

        private void OnError(string error, int hresult)
        {
            Listening = false;
            if (_suppressComplete)
            {
                return;
            }

            Problem = MascotSpeechProblem.Failed;
        }
#endif
    }
}
