using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace VRCast.Mascot
{
    public struct LlmMessage
    {
        public string Role;
        public string Content;

        public LlmMessage(string role, string content)
        {
            Role = role;
            Content = content;
        }
    }

    /// <summary>
    /// OpenAI 互換チャット API（LM Studio など）の JSON。通信は <see cref="LocalLlmClient"/>。
    /// </summary>
    public static class LocalLlm
    {
        public const string DefaultUrl = "http://127.0.0.1:1234/v1";

        public const string DefaultSystemPrompt =
            "あなたはデスクトップのマスコットです。短く親しみやすい口調で、相手の言語に合わせて返事してください。";

        public static string BuildChatBody(string model, string systemPrompt, IReadOnlyList<LlmMessage> messages)
        {
            var request = new ChatRequest
            {
                model = string.IsNullOrEmpty(model) ? "local-model" : model,
                messages = new ChatMessage[(messages != null ? messages.Count : 0) + 1],
            };
            request.messages[0] = new ChatMessage
            {
                role = "system",
                content = string.IsNullOrWhiteSpace(systemPrompt) ? DefaultSystemPrompt : systemPrompt.Trim(),
            };
            for (int i = 0; messages != null && i < messages.Count; i++)
            {
                request.messages[i + 1] = new ChatMessage
                {
                    role = messages[i].Role,
                    content = messages[i].Content ?? string.Empty,
                };
            }

            return JsonUtility.ToJson(request);
        }

        public static bool TryParseModels(string json, List<string> ids)
        {
            ids.Clear();
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            ModelsResponse response = JsonUtility.FromJson<ModelsResponse>(json);
            if (response?.data == null)
            {
                return false;
            }

            foreach (ModelEntry entry in response.data)
            {
                if (entry != null && !string.IsNullOrEmpty(entry.id) && !ids.Contains(entry.id))
                {
                    ids.Add(entry.id);
                }
            }

            return ids.Count > 0;
        }

        public static bool TryParseReply(string json, out string content)
        {
            content = string.Empty;
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }

            ChatResponse response = JsonUtility.FromJson<ChatResponse>(json);
            if (response?.choices == null || response.choices.Length == 0 || response.choices[0]?.message == null)
            {
                return false;
            }

            content = (response.choices[0].message.content ?? string.Empty).Trim();
            return content.Length > 0;
        }

        [Serializable]
        private class ChatRequest
        {
            public string model;
            public ChatMessage[] messages;
        }

        [Serializable]
        private class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class ChatResponse
        {
            public Choice[] choices;
        }

        [Serializable]
        private class Choice
        {
            public ChatMessage message;
        }

        [Serializable]
        private class ModelsResponse
        {
            public ModelEntry[] data;
        }

        [Serializable]
        private class ModelEntry
        {
            public string id;
        }
    }

    /// <summary>
    /// ローカルの言語モデルへ 1 件ずつ聞く。Tick で完了を見る。
    /// </summary>
    public sealed class LocalLlmClient
    {
        private const int ChatTimeout = 90;
        private const int ListTimeout = 8;

        private UnityWebRequest _request;
        private bool _models;
        private string _pendingReply;
        private bool _replyReady;
        private readonly List<string> _pendingModels = new List<string>();
        private bool _modelsReady;

        public bool Busy => _request != null;

        public string Error { get; private set; }

        public void Ask(string baseUrl, string model, string systemPrompt, IReadOnlyList<LlmMessage> messages)
        {
            string url = Join(baseUrl, "chat/completions");
            string body = LocalLlm.BuildChatBody(model, systemPrompt, messages);
            Begin(url, body, ChatTimeout, false);
        }

        public void FetchModels(string baseUrl)
        {
            Begin(Join(baseUrl, "models"), null, ListTimeout, true);
        }

        public bool TryTakeReply(out string text)
        {
            text = _pendingReply;
            if (!_replyReady)
            {
                return false;
            }

            _replyReady = false;
            _pendingReply = string.Empty;
            return true;
        }

        public bool TryTakeModels(List<string> ids)
        {
            if (!_modelsReady)
            {
                return false;
            }

            _modelsReady = false;
            ids.Clear();
            ids.AddRange(_pendingModels);
            return true;
        }

        public void Tick()
        {
            if (_request == null || !_request.isDone)
            {
                return;
            }

            bool ok = _request.result == UnityWebRequest.Result.Success;
            string text = ok ? _request.downloadHandler.text : string.Empty;
            string error = _request.error;
            bool models = _models;
            _request.Dispose();
            _request = null;

            if (!ok)
            {
                Error = error ?? "request failed";
                return;
            }

            Error = string.Empty;
            if (models)
            {
                _modelsReady = LocalLlm.TryParseModels(text, _pendingModels);
                if (!_modelsReady)
                {
                    Error = "no models";
                }

                return;
            }

            _replyReady = LocalLlm.TryParseReply(text, out _pendingReply);
            if (!_replyReady)
            {
                Error = "empty reply";
            }
        }

        public void Dispose()
        {
            _request?.Dispose();
            _request = null;
        }

        private void Begin(string url, string body, int timeout, bool models)
        {
            Dispose();
            Error = string.Empty;
            _replyReady = false;
            _modelsReady = false;
            _models = models;
            if (models)
            {
                _request = UnityWebRequest.Get(url);
            }
            else
            {
                _request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
                byte[] raw = Encoding.UTF8.GetBytes(body ?? "{}");
                _request.uploadHandler = new UploadHandlerRaw(raw);
                _request.downloadHandler = new DownloadHandlerBuffer();
                _request.SetRequestHeader("Content-Type", "application/json");
            }

            _request.timeout = timeout;
            _request.SendWebRequest();
        }

        private static string Join(string baseUrl, string path)
        {
            string root = string.IsNullOrWhiteSpace(baseUrl) ? LocalLlm.DefaultUrl : baseUrl.Trim().TrimEnd('/');
            return root + "/" + path;
        }
    }
}
