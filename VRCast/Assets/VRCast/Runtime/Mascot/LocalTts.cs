using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VRCast.Mascot
{
    public struct TtsSpeaker
    {
        public string Name;
        public int Id;
    }

    /// <summary>
    /// COEIROINK / VOICEVOX の共通 API。既定は COEIROINK。
    /// </summary>
    public static class LocalTts
    {
        public const string CoeiroinkUrl = "http://127.0.0.1:50032";
        public const string VoicevoxUrl = "http://127.0.0.1:50021";

        public static string UrlFor(int engine)
        {
            return engine == 1 ? VoicevoxUrl : CoeiroinkUrl;
        }

        public static string Normalize(string url)
        {
            return string.IsNullOrWhiteSpace(url) ? string.Empty : url.Trim().TrimEnd('/');
        }

        public static bool IsKnownDefault(string url)
        {
            string normalized = Normalize(url);
            return string.Equals(normalized, CoeiroinkUrl, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, VoicevoxUrl, StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryParseSpeakers(string json, List<TtsSpeaker> speakers)
        {
            speakers.Clear();
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            string wrapped = json.TrimStart();
            if (wrapped.StartsWith("[", StringComparison.Ordinal))
            {
                wrapped = "{\"items\":" + wrapped + "}";
            }

            SpeakerFile file = JsonUtility.FromJson<SpeakerFile>(wrapped);
            if (file?.items == null)
            {
                return false;
            }

            foreach (SpeakerEntry entry in file.items)
            {
                if (entry?.styles == null)
                {
                    continue;
                }

                foreach (StyleEntry style in entry.styles)
                {
                    if (style == null)
                    {
                        continue;
                    }

                    string name = string.IsNullOrEmpty(style.name) || style.name == "ノーマル"
                        ? entry.name ?? string.Empty
                        : (entry.name ?? string.Empty) + " (" + style.name + ")";
                    speakers.Add(new TtsSpeaker { Name = name, Id = style.id });
                }
            }

            return speakers.Count > 0;
        }

        /// <summary>
        /// 長い返事を文ごとに分ける。エンジンが一度に受け取れる長さを超えないようにする。
        /// </summary>
        public static List<string> SplitSentences(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var current = new StringBuilder();
            foreach (char character in text.Trim())
            {
                current.Append(character);
                bool end = character == '。' || character == '！' || character == '？'
                    || character == '!' || character == '?' || character == '\n';
                // 文の区切りで分ける。区切りが無い長い文は、エンジンが受け取れる長さで切る
                if (end || current.Length >= 100)
                {
                    Push(result, current);
                }
            }

            Push(result, current);
            return result;
        }

        public static bool TryDecodeWav(byte[] wav, out int sampleRate, out int channels, out float[] samples)
        {
            sampleRate = 0;
            channels = 0;
            samples = null;
            if (wav == null || wav.Length < 44 || wav[0] != 'R' || wav[1] != 'I' || wav[2] != 'F' || wav[3] != 'F')
            {
                return false;
            }

            int offset = 12;
            int data = -1;
            int dataSize = 0;
            short bits = 16;
            while (offset + 8 <= wav.Length)
            {
                string id = Encoding.ASCII.GetString(wav, offset, 4);
                int size = BitConverter.ToInt32(wav, offset + 4);
                int body = offset + 8;
                if (size < 0 || body + size > wav.Length)
                {
                    break;
                }

                if (id == "fmt " && size >= 16)
                {
                    channels = BitConverter.ToInt16(wav, body + 2);
                    sampleRate = BitConverter.ToInt32(wav, body + 4);
                    bits = BitConverter.ToInt16(wav, body + 14);
                }
                else if (id == "data")
                {
                    data = body;
                    dataSize = size;
                    break;
                }

                offset = body + size + (size & 1);
            }

            if (data < 0 || channels < 1 || sampleRate < 1)
            {
                return false;
            }

            int bytesPerSample = bits / 8;
            if (bytesPerSample != 2 && bytesPerSample != 4)
            {
                return false;
            }

            int count = dataSize / bytesPerSample;
            samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                int at = data + i * bytesPerSample;
                if (at + bytesPerSample > wav.Length)
                {
                    break;
                }

                samples[i] = bits == 32
                    ? BitConverter.ToSingle(wav, at)
                    : BitConverter.ToInt16(wav, at) / 32768f;
            }

            return true;
        }

        private static void Push(List<string> result, StringBuilder current)
        {
            string sentence = current.ToString().Trim();
            current.Clear();
            if (sentence.Length > 0)
            {
                result.Add(sentence);
            }
        }

        [Serializable]
        private class SpeakerFile
        {
            public SpeakerEntry[] items;
        }

        [Serializable]
        private class SpeakerEntry
        {
            public string name;
            public StyleEntry[] styles;
        }

        [Serializable]
        private class StyleEntry
        {
            public string name;
            public int id;
        }
    }

    /// <summary>
    /// 文を順に合成して再生する。再生中の音量は <see cref="Level"/>。
    /// </summary>
    public sealed class LocalTtsClient
    {
        private readonly AudioSource _source;
        private readonly Queue<string> _pending = new Queue<string>();
        private UnityWebRequest _request;
        private UnityWebRequest _speakersRequest;
        private string _queryJson;
        private int _phase;
        private string _baseUrl = LocalTts.CoeiroinkUrl;
        private int _speaker;
        private float[] _pcm;
        private int _rate = 24000;
        private int _channels = 1;
        private AudioClip _clip;

        private readonly List<TtsSpeaker> _speakerBuffer = new List<TtsSpeaker>();
        private bool _speakersReady;

        public bool Busy => _request != null || _pending.Count > 0 || (_source != null && _source.isPlaying);

        public string Error { get; private set; }

        public float Level { get; private set; }

        public LocalTtsClient(AudioSource source)
        {
            _source = source;
        }

        public void FetchSpeakers(string baseUrl)
        {
            _speakersRequest?.Dispose();
            _speakersReady = false;
            string root = LocalTts.Normalize(baseUrl);
            if (root.Length == 0)
            {
                root = LocalTts.CoeiroinkUrl;
            }

            _speakersRequest = UnityWebRequest.Get(root + "/speakers");
            _speakersRequest.timeout = 8;
            _speakersRequest.SendWebRequest();
        }

        public bool TryTakeSpeakers(List<TtsSpeaker> speakers)
        {
            if (!_speakersReady)
            {
                return false;
            }

            _speakersReady = false;
            speakers.Clear();
            speakers.AddRange(_speakerBuffer);
            return true;
        }

        public void Speak(string baseUrl, int speaker, string text)
        {
            Stop();
            _baseUrl = LocalTts.Normalize(baseUrl);
            if (_baseUrl.Length == 0)
            {
                _baseUrl = LocalTts.CoeiroinkUrl;
            }

            _speaker = speaker;
            foreach (string sentence in LocalTts.SplitSentences(text))
            {
                _pending.Enqueue(sentence);
            }
        }

        public void Stop()
        {
            _pending.Clear();
            DisposeRequest();
            if (_source != null)
            {
                _source.Stop();
            }

            DestroyClip();
            Level = 0f;
            Error = string.Empty;
        }

        public void Tick()
        {
            if (_speakersRequest != null && _speakersRequest.isDone)
            {
                bool ok = _speakersRequest.result == UnityWebRequest.Result.Success;
                string json = ok ? _speakersRequest.downloadHandler.text : string.Empty;
                _speakersRequest.Dispose();
                _speakersRequest = null;
                if (ok && LocalTts.TryParseSpeakers(json, _speakerBuffer))
                {
                    _speakersReady = true;
                    if (Error == "no speakers")
                    {
                        Error = string.Empty;
                    }
                }
                else
                {
                    _speakersReady = false;
                    Error = "no speakers";
                }
            }

            if (_request != null && _request.isDone)
            {
                FinishRequest();
            }

            if (_source != null && !_source.isPlaying && _clip != null && _request == null)
            {
                DestroyClip();
            }

            if (_request == null && _pending.Count > 0 && (_source == null || !_source.isPlaying))
            {
                StartQuery(_pending.Dequeue());
            }

            Level = CurrentLevel();
        }

        public void Dispose()
        {
            Stop();
            _speakersRequest?.Dispose();
            _speakersRequest = null;
        }

        private void StartQuery(string text)
        {
            string url = _baseUrl + "/audio_query?speaker=" + _speaker + "&text=" + UnityWebRequest.EscapeURL(text);
            _phase = 0;
            _request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            _request.uploadHandler = new UploadHandlerRaw(Array.Empty<byte>());
            _request.downloadHandler = new DownloadHandlerBuffer();
            _request.timeout = 60;
            _request.SendWebRequest();
        }

        private void FinishRequest()
        {
            bool ok = _request.result == UnityWebRequest.Result.Success;
            byte[] data = ok ? _request.downloadHandler.data : null;
            string text = ok ? _request.downloadHandler.text : string.Empty;
            string error = _request.error;
            int phase = _phase;
            DisposeRequest();
            if (!ok)
            {
                Error = error ?? "voice request failed";
                return;
            }

            Error = string.Empty;
            if (phase == 0)
            {
                _queryJson = text;
                string url = _baseUrl + "/synthesis?speaker=" + _speaker;
                _phase = 1;
                _request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                byte[] raw = Encoding.UTF8.GetBytes(_queryJson ?? "{}");
                _request.uploadHandler = new UploadHandlerRaw(raw);
                _request.downloadHandler = new DownloadHandlerBuffer();
                _request.SetRequestHeader("Content-Type", "application/json");
                _request.timeout = 60;
                _request.SendWebRequest();
                return;
            }

            if (!LocalTts.TryDecodeWav(data, out _rate, out _channels, out _pcm) || _source == null)
            {
                Error = "bad audio";
                return;
            }

            DestroyClip();
            int frames = _pcm.Length / _channels;
            _clip = AudioClip.Create("mascot-voice", frames, _channels, _rate, false);
            _clip.SetData(_pcm, 0);
            _source.clip = _clip;
            _source.Play();
        }

        private float CurrentLevel()
        {
            if (_source == null || !_source.isPlaying || _pcm == null || _pcm.Length == 0)
            {
                return 0f;
            }

            int frame = Mathf.Clamp((int)(_source.time * _rate), 0, (_pcm.Length / _channels) - 1);
            int start = frame * _channels;
            int end = Mathf.Min(_pcm.Length, start + 700 * _channels);
            float sum = 0f;
            int count = 0;
            for (int i = start; i < end; i++)
            {
                sum += _pcm[i] * _pcm[i];
                count++;
            }

            if (count == 0)
            {
                return 0f;
            }

            return Mathf.Clamp01(Mathf.Sqrt(sum / count) * 8f);
        }

        private void DisposeRequest()
        {
            _request?.Dispose();
            _request = null;
        }

        private void DestroyClip()
        {
            if (_clip != null)
            {
                UnityEngine.Object.Destroy(_clip);
                _clip = null;
            }

            _pcm = null;
        }
    }
}
