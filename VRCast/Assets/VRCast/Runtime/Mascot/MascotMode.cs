using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Animations;
using UnityEngine.Rendering;
using VRCast.Animations;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Platform;
using VRCast.Rendering;
using VRCast.Tracking;
using VRCast.UI;

namespace VRCast.Mascot
{
    /// <summary>
    /// マスコットモード。起動時の画面は残し、アバターは別ウィンドウへ出す。
    /// 会話・読み上げ・立ちポーズ・カーソルを見る向きもここから進める。
    /// </summary>
    public sealed class MascotMode
    {
        private const int AvatarLayer = 31;
        private const int HistoryLimit = 16;

        private readonly MonoBehaviour _host;
        private readonly AvatarSession _session;
        private readonly Camera _main;
        private readonly AppSettings _settings;
        private readonly AudioSource _audio;
        private readonly LocalLlmClient _llm = new LocalLlmClient();
        private readonly LocalTtsClient _tts;
        private readonly List<LlmMessage> _history = new List<LlmMessage>();
        private readonly List<(bool mine, string text)> _log = new List<(bool, string)>();
        private readonly List<string> _models = new List<string>();
        private readonly List<TtsSpeaker> _speakers = new List<TtsSpeaker>();
        private readonly Dictionary<int, int> _layers = new Dictionary<int, int>();

        private Camera _view;
        private RenderTexture _target;
        private RenderTexture _scene;
        private bool _readback;
        private PlayableGraph _graph;
        private int _graphClip = int.MinValue;
        private int _boundAvatar;
        private string _input = string.Empty;
        private Vector2 _scroll;
        private string _scaleInput = "100";
        private Vector2 _chatScroll;
        private string _bubble = string.Empty;
        private TextMesh _text;
        private TextMesh _shadow;
        private Transform _textTransform;
        private int _framedAvatar;
        private float _frameDistance;
        private Coroutine _readLoop;
        private int _savedMask;
        private bool _maskSaved;

        public bool Active { get; private set; }

        public MascotMode(MonoBehaviour host, AvatarSession session, Camera main, AppSettings settings)
        {
            _host = host;
            _session = session;
            _main = main;
            _settings = settings;
            _audio = host.gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _tts = new LocalTtsClient(_audio);
        }

        public void Open()
        {
            if (Active)
            {
                return;
            }

            Active = true;
            EnsureView();
            HideFromMainCamera();
            if (!MascotWindow.Open(_settings.mascotWindowX, _settings.mascotWindowY, _settings.mascotScale))
            {
                VRCastLog.Warning("Mascot", "Could not open the mascot window.");
            }

            _llm.FetchModels(_settings.mascotLlmUrl);
            FetchSpeakers();
            _readLoop = _host.StartCoroutine(PresentLoop());
        }

        public void Close()
        {
            if (!Active)
            {
                return;
            }

            if (MascotWindow.TryPosition(out int x, out int y))
            {
                _settings.mascotWindowX = x;
                _settings.mascotWindowY = y;
            }

            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            if (instance != null)
            {
                StandPosePlayer pose = instance.GetComponent<StandPosePlayer>();
                if (pose != null)
                {
                    pose.Active = false;
                }

                MascotLookAt look = instance.GetComponent<MascotLookAt>();
                if (look != null)
                {
                    look.Active = false;
                }
            }

            Active = false;
            if (_readLoop != null)
            {
                _host.StopCoroutine(_readLoop);
                _readLoop = null;
            }

            _tts.Stop();
            StopGraph();
            RestoreLayers();
            RestoreMask();
            SetIdleSuspended(false);
            SetSpeech(0f);
            MascotWindow.Close();
            if (_textTransform != null)
            {
                _textTransform.gameObject.SetActive(false);
            }
        }

        public void Tick()
        {
            if (!Active)
            {
                return;
            }

            _llm.Tick();
            _tts.Tick();
            if (_llm.TryTakeModels(_models) && string.IsNullOrEmpty(_settings.mascotLlmModel) && _models.Count > 0)
            {
                _settings.mascotLlmModel = _models[0];
            }

            if (_llm.TryTakeReply(out string reply))
            {
                _history.Add(new LlmMessage("assistant", reply));
                _log.Add((false, reply));
                TrimHistory();
                _bubble = reply.Length > 80 ? reply.Substring(0, 80) + "…" : reply;
                _tts.Speak(_settings.mascotTtsUrl, _settings.mascotTtsSpeaker, reply);
            }

            _tts.TryTakeSpeakers(_speakers);
            if (MascotWindow.TryPosition(out int windowX, out int windowY))
            {
                _settings.mascotWindowX = windowX;
                _settings.mascotWindowY = windowY;
            }

            if (MascotWindow.DraggingScale)
            {
                _settings.mascotScale = MascotWindow.Scale;
            }
            else if (MascotWindow.IsOpen)
            {
                MascotWindow.ApplyScale(_settings.mascotScale);
            }

            BindAvatar();
            FrameCamera();
            SyncSceneTarget();
            UpdateBubble();
            SetSpeech(_tts.Level);
        }

        public void Draw()
        {
            if (!MascotWindow.IsOpen)
            {
                GuiControls.Warning(Loc.T(
                    "The mascot window could not be opened.",
                    "マスコットのウィンドウを開けませんでした。",
                    "마스코트 창을 열지 못했습니다.",
                    "无法打开吉祥物窗口。",
                    "無法打開吉祥物視窗。"));
            }

            // 言語・モードボタン・下の説明を残し、あいだを設定のスクロールにする
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            DrawSize();
            DrawLanguageModel();
            DrawVoice();
            DrawPose();
            DrawChat();
            GUILayout.EndScrollView();
        }

        public void Dispose()
        {
            Close();
            _llm.Dispose();
            _tts.Dispose();
            if (_target != null)
            {
                _target.Release();
                UnityEngine.Object.Destroy(_target);
                _target = null;
            }

            if (_scene != null)
            {
                _scene.Release();
                UnityEngine.Object.Destroy(_scene);
                _scene = null;
            }

            if (_view != null)
            {
                UnityEngine.Object.Destroy(_view.gameObject);
                _view = null;
            }
        }

        private void DrawSize()
        {
            GuiControls.BeginCard(Loc.T("Size", "大きさ", "크기", "大小", "大小"));
            float percent = GuiControls.SliderOrField(
                Loc.T("Avatar size", "アバターの大きさ", "아바타 크기", "形象大小", "形象大小"),
                _settings.mascotScale * 100f,
                MascotWindow.MinScale * 100f,
                MascotWindow.MaxScale * 100f,
                ref _scaleInput,
                "mascot-scale");
            _settings.mascotScale = MascotWindow.ClampScale(percent / 100f);
            GuiControls.Hint(Loc.T(
                "Type a percent or drag the slider. Right-drag up or down on the avatar to resize it too.",
                "パーセントを入力するか、スライダーをドラッグします。アバターを右ボタンで上下にドラッグしても変えられます。",
                "퍼센트를 입력하거나 슬라이더를 드래그하세요. 아바타 위에서 오른쪽 버튼으로 위아래 드래그해도 바뀝니다.",
                "输入百分比，或拖动滑块。在形象上按住右键上下拖动也可以改变大小。",
                "輸入百分比，或拖動滑桿。在形象上按住右鍵上下拖動也可以改變大小。"));
            GuiControls.EndCard();
        }

        private void DrawLanguageModel()
        {
            GuiControls.BeginCard(Loc.T("Language model", "言語モデル", "언어 모델", "语言模型", "語言模型"));
            _settings.mascotLlmUrl = GuiControls.TextField("URL", _settings.mascotLlmUrl, 200);
            if (_models.Count > 0)
            {
                int index = _models.IndexOf(_settings.mascotLlmModel);
                int next = GuiControls.Selector(Loc.T("Model", "モデル", "모델", "模型", "模型"), _models, index, null);
                if (next >= 0 && next < _models.Count)
                {
                    _settings.mascotLlmModel = _models[next];
                }
            }
            else
            {
                _settings.mascotLlmModel = GuiControls.TextField(
                    Loc.T("Model", "モデル", "모델", "模型", "模型"), _settings.mascotLlmModel, 120);
            }

            if (GUILayout.Button(Loc.T("Refresh models", "モデルを更新", "모델 새로고침", "刷新模型", "重新整理模型")))
            {
                _llm.FetchModels(_settings.mascotLlmUrl);
            }

            GUILayout.Label(Loc.T("System prompt", "システムプロンプト", "시스템 프롬프트", "系统提示", "系統提示"));
            _settings.mascotSystemPrompt = GUILayout.TextArea(_settings.mascotSystemPrompt ?? string.Empty, GUILayout.Height(52));
            if (string.IsNullOrWhiteSpace(_settings.mascotSystemPrompt))
            {
                GuiControls.Hint(LocalLlm.DefaultSystemPrompt);
            }

            if (!string.IsNullOrEmpty(_llm.Error))
            {
                GuiControls.Warning(Loc.T(
                    "Couldn't reach the language model. Start the local server in LM Studio (or another OpenAI-compatible server).",
                    "言語モデルに繋がりません。LM Studio などでローカルサーバーを開始してください。",
                    "언어 모델에 연결하지 못했습니다. LM Studio 등에서 로컬 서버를 시작하세요.",
                    "连不上语言模型。请在 LM Studio 等里启动本地服务器。",
                    "連不上語言模型。請在 LM Studio 等裡啟動本地伺服器。"));
            }

            GuiControls.EndCard();
        }

        private void DrawVoice()
        {
            GuiControls.BeginCard(Loc.T("Voice", "読み上げ", "음성", "语音", "語音"));
            int engine = GuiControls.LabeledChoice(
                Loc.T("Engine", "エンジン", "엔진", "引擎", "引擎"),
                new[] { "COEIROINK", "VOICEVOX" },
                _settings.mascotTtsEngine == 1 ? 1 : 0);
            if (engine != _settings.mascotTtsEngine)
            {
                if (LocalTts.IsKnownDefault(_settings.mascotTtsUrl) || string.IsNullOrWhiteSpace(_settings.mascotTtsUrl))
                {
                    _settings.mascotTtsUrl = LocalTts.UrlFor(engine);
                }

                _settings.mascotTtsEngine = engine;
                FetchSpeakers();
            }

            GuiControls.Hint(Loc.T(
                "COEIROINK is recommended: you can train your own voice and have the mascot speak with it. Training is done in COEIROINK, then you pick that speaker here.",
                "COEIROINK を推奨します。自分の声を学習させて、その声でマスコットに話させることができます。学習は COEIROINK 側で行い、できた話者をここで選びます。",
                "COEIROINK를 권장합니다. 자신의 목소리를 학습시켜 그 목소리로 마스코트가 말하게 할 수 있습니다. 학습은 COEIROINK에서 하고, 만들어진 화자를 여기서 고릅니다.",
                "推荐使用 COEIROINK。可以训练自己的声音，让吉祥物用那个声音说话。训练在 COEIROINK 里完成，然后在这里选择得到的说话人。",
                "推薦使用 COEIROINK。可以訓練自己的聲音，讓吉祥物用那個聲音說話。訓練在 COEIROINK 裡完成，然後在這裡選擇得到的說話人。"));
            _settings.mascotTtsUrl = GuiControls.TextField("URL", _settings.mascotTtsUrl, 200);
            if (_speakers.Count > 0)
            {
                var names = new string[_speakers.Count];
                int selected = 0;
                for (int i = 0; i < _speakers.Count; i++)
                {
                    names[i] = _speakers[i].Name;
                    if (_speakers[i].Id == _settings.mascotTtsSpeaker)
                    {
                        selected = i;
                    }
                }

                int next = GuiControls.Selector(Loc.T("Speaker", "話者", "화자", "说话人", "說話人"), names, selected, null);
                if (next >= 0 && next < _speakers.Count)
                {
                    _settings.mascotTtsSpeaker = _speakers[next].Id;
                }
            }

            if (GUILayout.Button(Loc.T("Refresh speakers", "話者を更新", "화자 새로고침", "刷新说话人", "重新整理說話人")))
            {
                FetchSpeakers();
            }

            if (_tts.Busy && GUILayout.Button(Loc.T("Stop", "止める", "정지", "停止", "停止")))
            {
                _tts.Stop();
                SetSpeech(0f);
            }

            if (!string.IsNullOrEmpty(_tts.Error))
            {
                GuiControls.Warning(Loc.T(
                    "Couldn't reach the voice engine. Start COEIROINK or VOICEVOX, then refresh the speakers.",
                    "読み上げに繋がりません。COEIROINK か VOICEVOX を起動して、話者を更新してください。",
                    "음성 엔진에 연결하지 못했습니다. COEIROINK나 VOICEVOX를 켜고 화자를 새로고침하세요.",
                    "连不上语音引擎。请启动 COEIROINK 或 VOICEVOX，然后刷新说话人。",
                    "連不上語音引擎。請啟動 COEIROINK 或 VOICEVOX，然後重新整理說話人。"));
            }

            GuiControls.EndCard();
        }

        private void DrawPose()
        {
            GuiControls.BeginCard(Loc.T("Standing pose", "立ちポーズ", "서 있는 포즈", "站立姿势", "站立姿勢"));
            StandClipHolder holder = Holder();
            var labels = new List<string>
            {
                Loc.T("Default standing pose", "既定の立ちポーズ", "기본 서 있는 포즈", "默认站立姿势", "預設站立姿勢"),
            };
            int count = holder != null && holder.clips != null ? holder.clips.Length : 0;
            for (int i = 0; i < count; i++)
            {
                string label = holder.labels != null && i < holder.labels.Length && !string.IsNullOrEmpty(holder.labels[i])
                    ? holder.labels[i]
                    : holder.clips[i] != null ? holder.clips[i].name : "Pose";
                labels.Add(label);
            }

            int selected = Mathf.Clamp(_settings.mascotStandClip + 1, 0, labels.Count - 1);
            int next = GuiControls.Selector(Loc.T("Pose", "ポーズ", "포즈", "姿势", "姿勢"), labels, selected, null);
            if (next >= 0 && next != selected)
            {
                _settings.mascotStandClip = next - 1;
                _graphClip = int.MinValue;
            }

            if (count == 0)
            {
                GuiControls.Hint(Loc.T(
                    "If the VRChat avatar has a standing animation, re-export it and it will show up here.",
                    "VRChat に立ちポーズがあれば、書き出し直すとここに選べるようになります。",
                    "VRChat에 서 있는 포즈가 있으면 다시 내보내면 여기서 고를 수 있습니다.",
                    "若 VRChat 形象有站立动作，重新导出后即可在这里选择。",
                    "若 VRChat 形象有站立動作，重新匯出後即可在這裡選擇。"));
            }

            GuiControls.EndCard();
        }

        private void DrawChat()
        {
            GuiControls.BeginCard(Loc.T("Chat", "会話", "대화", "对话", "對話"));
            _chatScroll = GUILayout.BeginScrollView(_chatScroll, GUILayout.Height(140));
            if (_log.Count == 0)
            {
                GuiControls.Hint(Loc.T("Say something to the mascot.", "マスコットに話しかけてください。",
                    "마스코트에게 말을 걸어 보세요.", "对吉祥物说点什么。", "對吉祥物說點什麼。"));
            }

            foreach ((bool mine, string text) in _log)
            {
                GUILayout.Label((mine ? "> " : "") + text);
            }

            GUILayout.EndScrollView();
            GUI.SetNextControlName("mascot-input");
            _input = GUILayout.TextField(_input ?? string.Empty);
            bool enter = Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                && GUI.GetNameOfFocusedControl() == "mascot-input";
            if ((GUILayout.Button(Loc.T("Send", "送る", "보내기", "发送", "傳送")) || enter) && !_llm.Busy)
            {
                Send();
                if (enter)
                {
                    Event.current.Use();
                }
            }

            if (_llm.Busy)
            {
                GuiControls.Hint(Loc.T("Thinking...", "考えています…", "생각 중…", "正在想…", "正在想…"));
            }
            else if (_tts.Busy)
            {
                GuiControls.Hint(Loc.T("Speaking...", "話しています…", "말하는 중…", "正在说…", "正在說…"));
            }

            GuiControls.EndCard();
        }

        private void Send()
        {
            string text = (_input ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return;
            }

            _input = string.Empty;
            _history.Add(new LlmMessage("user", text));
            _log.Add((true, text));
            TrimHistory();
            _llm.Ask(_settings.mascotLlmUrl, _settings.mascotLlmModel, _settings.mascotSystemPrompt, _history);
        }

        private void TrimHistory()
        {
            while (_history.Count > HistoryLimit)
            {
                _history.RemoveAt(0);
            }

            while (_log.Count > 40)
            {
                _log.RemoveAt(0);
            }
        }

        private void FetchSpeakers()
        {
            _tts.FetchSpeakers(_settings.mascotTtsUrl);
        }

        private void BindAvatar()
        {
            LoadedAvatar avatar = _session.Current;
            GameObject instance = avatar != null ? avatar.Instance : null;
            if (instance == null)
            {
                SetIdleSuspended(false);
                return;
            }

            ApplyLayer(instance);
            var pose = instance.GetComponent<StandPosePlayer>() ?? instance.AddComponent<StandPosePlayer>();
            var look = instance.GetComponent<MascotLookAt>() ?? instance.AddComponent<MascotLookAt>();
            if (instance.GetInstanceID() != _boundAvatar)
            {
                _boundAvatar = instance.GetInstanceID();
                pose.Initialize(avatar.Animator);
                look.Initialize(avatar.Animator);
                _graphClip = int.MinValue;
                EnsureBubble();
            }

            SetIdleSuspended(true);
            AnimationClip clip = SelectedClip(instance);
            bool playing = clip != null;
            FullBodyTrackingDriver body = instance.GetComponent<FullBodyTrackingDriver>();
            pose.Active = !playing && (body == null || !body.IsTracking);
            pose.SuppressHead = look.Weight > 0.2f;
            look.Active = true;
            look.View = _view;
            look.ScreenRect = MascotWindow.ScreenRect;
            SyncClip(avatar.Animator, clip);
        }

        private AnimationClip SelectedClip(GameObject instance)
        {
            StandClipHolder holder = instance.GetComponent<StandClipHolder>();
            int index = _settings.mascotStandClip;
            if (holder == null || holder.clips == null || index < 0 || index >= holder.clips.Length)
            {
                return null;
            }

            return holder.clips[index];
        }

        private StandClipHolder Holder()
        {
            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            return instance != null ? instance.GetComponent<StandClipHolder>() : null;
        }

        private void SyncClip(Animator animator, AnimationClip clip)
        {
            int id = clip != null ? clip.GetInstanceID() : -1;
            if (id == _graphClip)
            {
                if (_graph.IsValid())
                {
                    Playable root = _graph.GetRootPlayable(0);
                    double length = clip != null ? clip.length : 0;
                    if (length > 0.01 && root.GetTime() >= length)
                    {
                        root.SetTime(0);
                    }
                }

                return;
            }

            StopGraph();
            _graphClip = id;
            if (clip == null || animator == null)
            {
                instancePose()?.Reapply();
                return;
            }

            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _graph = PlayableGraph.Create("VRCastStand");
            AnimationClipPlayable playable = AnimationClipPlayable.Create(_graph, clip);
            AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "stand", animator);
            output.SetSourcePlayable(playable);
            _graph.Play();
        }

        private PoseController instancePose()
        {
            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            return instance != null ? instance.GetComponent<PoseController>() : null;
        }

        private void StopGraph()
        {
            if (_graph.IsValid())
            {
                _graph.Destroy();
            }

            _graphClip = int.MinValue;
        }

        private void SetIdleSuspended(bool suspended)
        {
            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            IdleMotionController idle = instance != null ? instance.GetComponent<IdleMotionController>() : null;
            if (idle != null)
            {
                idle.Suspended = suspended;
            }
        }

        private void SetSpeech(float level)
        {
            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            LipSyncController lips = instance != null ? instance.GetComponent<LipSyncController>() : null;
            if (lips != null)
            {
                lips.SpeechLevel = level;
            }
        }

        private void EnsureView()
        {
            if (_view != null)
            {
                return;
            }

            var go = new GameObject("VRCast Mascot View");
            go.hideFlags = HideFlags.HideAndDontSave;
            _view = go.AddComponent<Camera>();
            _view.clearFlags = CameraClearFlags.SolidColor;
            _view.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _view.fieldOfView = 24f;
            _view.nearClipPlane = 0.05f;
            _view.farClipPlane = 50f;
            _view.cullingMask = 1 << AvatarLayer;
            _view.depth = -50f;
            SyncSceneTarget();
            HideFromMainCamera();
        }

        /// <summary>
        /// 配信と同じ MSAA・FXAA・高解像度で描き、ウィンドウの大きさへ縮めてから読み出す。
        /// </summary>
        private void SyncSceneTarget()
        {
            if (_view == null || _readback)
            {
                return;
            }

            float scale = CameraAntiAliasing.RenderScaleOf(_settings);
            int samples = CameraAntiAliasing.MsaaSamplesOf(_settings);
            int width = Mathf.Max(1, Mathf.RoundToInt(MascotWindow.PixelWidth * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(MascotWindow.PixelHeight * scale));
            _view.allowMSAA = samples > 1;
            bool sceneMatches = _scene != null && _scene.width == width && _scene.height == height
                && _scene.antiAliasing == samples;
            bool outputMatches = _target != null && _target.width == MascotWindow.PixelWidth
                && _target.height == MascotWindow.PixelHeight;
            if (sceneMatches && outputMatches)
            {
                _view.targetTexture = _scene;
                return;
            }

            if (!outputMatches)
            {
                if (_target != null)
                {
                    _target.Release();
                    UnityEngine.Object.Destroy(_target);
                }

                _target = new RenderTexture(MascotWindow.PixelWidth, MascotWindow.PixelHeight, 0, RenderTextureFormat.ARGB32)
                {
                    name = "VRCast Mascot Output",
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
                _target.Create();
            }

            if (_scene != null)
            {
                if (_view.targetTexture == _scene)
                {
                    _view.targetTexture = null;
                }

                _scene.Release();
                UnityEngine.Object.Destroy(_scene);
            }

            _scene = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                name = "VRCast Mascot Scene",
                antiAliasing = samples,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _scene.Create();
            _view.targetTexture = _scene;
        }

        private void HideFromMainCamera()
        {
            if (_main == null)
            {
                return;
            }

            if (!_maskSaved)
            {
                _savedMask = _main.cullingMask;
                _maskSaved = true;
            }

            _main.cullingMask &= ~(1 << AvatarLayer);
        }

        private void FrameCamera()
        {
            if (_view == null)
            {
                return;
            }

            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            if (instance == null)
            {
                return;
            }

            var bounds = new Bounds(instance.transform.position, Vector3.one * 0.5f);
            bool any = false;
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            Vector3 focus = bounds.center;
            Animator animator = instance.GetComponent<Animator>();
            if (animator != null && animator.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (head != null && hips != null)
                {
                    focus = Vector3.Lerp(hips.position, head.position, 0.62f);
                }
            }

            int id = instance.GetInstanceID();
            if (id != _framedAvatar || _frameDistance <= 0f)
            {
                _framedAvatar = id;
                float radius = Mathf.Max(0.4f, bounds.extents.magnitude);
                _frameDistance = radius / Mathf.Sin(_view.fieldOfView * 0.5f * Mathf.Deg2Rad);
            }

            // アバターの正面（+Z）から見る。距離は読み込み時の大きさで固定し、呼吸でズームしない
            Vector3 forward = instance.transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f)
            {
                forward = Vector3.forward;
            }

            _view.transform.position = focus + forward.normalized * _frameDistance;
            _view.transform.LookAt(focus, Vector3.up);
        }

        private void ApplyLayer(GameObject root)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                int id = transform.gameObject.GetInstanceID();
                if (!_layers.ContainsKey(id))
                {
                    _layers[id] = transform.gameObject.layer;
                }

                transform.gameObject.layer = AvatarLayer;
            }
        }

        private void RestoreLayers()
        {
            foreach (KeyValuePair<int, int> pair in _layers)
            {
                // 破棄済みは無視
            }

            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            if (instance == null)
            {
                _layers.Clear();
                return;
            }

            foreach (Transform transform in instance.GetComponentsInChildren<Transform>(true))
            {
                if (_layers.TryGetValue(transform.gameObject.GetInstanceID(), out int layer))
                {
                    transform.gameObject.layer = layer;
                }
            }

            _layers.Clear();
        }

        private void RestoreMask()
        {
            if (_maskSaved && _main != null)
            {
                _main.cullingMask = _savedMask;
                _maskSaved = false;
            }
        }

        private void EnsureBubble()
        {
            if (_textTransform != null)
            {
                return;
            }

            var go = new GameObject("VRCast Mascot Bubble");
            go.hideFlags = HideFlags.HideAndDontSave;
            go.layer = AvatarLayer;
            _text = go.AddComponent<TextMesh>();
            _text.anchor = TextAnchor.LowerCenter;
            _text.alignment = TextAlignment.Center;
            _text.fontSize = 48;
            _text.characterSize = 0.015f;
            _text.color = Color.white;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.font = font;
            _textTransform = go.transform;

            var shadowObject = new GameObject("shadow");
            shadowObject.hideFlags = HideFlags.HideAndDontSave;
            shadowObject.layer = AvatarLayer;
            shadowObject.transform.SetParent(go.transform, false);
            shadowObject.transform.localPosition = new Vector3(0.006f, -0.006f, 0.02f);
            _shadow = shadowObject.AddComponent<TextMesh>();
            _shadow.anchor = TextAnchor.LowerCenter;
            _shadow.alignment = TextAlignment.Center;
            _shadow.fontSize = 48;
            _shadow.characterSize = 0.015f;
            _shadow.color = new Color(0.12f, 0.08f, 0.06f, 0.9f);
            _shadow.font = font;
        }

        private void UpdateBubble()
        {
            if (_text == null)
            {
                return;
            }

            bool show = !string.IsNullOrEmpty(_bubble);
            _textTransform.gameObject.SetActive(show);
            if (!show || _view == null)
            {
                return;
            }

            _text.text = _bubble;
            if (_shadow != null)
            {
                _shadow.text = _bubble;
            }
            GameObject instance = _session.Current != null ? _session.Current.Instance : null;
            Vector3 head = instance != null ? instance.transform.position + Vector3.up * 1.55f : _view.transform.position;
            Animator animator = instance != null ? instance.GetComponent<Animator>() : null;
            if (animator != null && animator.isHuman)
            {
                Transform bone = animator.GetBoneTransform(HumanBodyBones.Head);
                if (bone != null)
                {
                    head = bone.position;
                }
            }

            _textTransform.position = head + _view.transform.up * 0.18f;
            _textTransform.rotation = _view.transform.rotation;
        }

        private IEnumerator PresentLoop()
        {
            // カメラの描画が終わってから読む。Update 中に読むと、書き込み途中の絵になる
            while (Active)
            {
                yield return new WaitForEndOfFrame();
                if (_scene != null && _target != null)
                {
                    CameraAntiAliasing.BlitProcessed(_settings, _scene, _target);
                }

                RequestReadback();
            }
        }

        private void RequestReadback()
        {
            if (_readback || _target == null || !MascotWindow.IsOpen)
            {
                return;
            }

            _readback = true;
            AsyncGPUReadback.Request(_target, 0, OnReadback);
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            _readback = false;
            if (!Active || request.hasError || !MascotWindow.IsOpen)
            {
                return;
            }

            MascotWindow.Present(request.GetData<byte>().ToArray());
        }
    }
}
