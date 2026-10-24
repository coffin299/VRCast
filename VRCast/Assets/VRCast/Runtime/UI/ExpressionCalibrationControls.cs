using System.Collections.Generic;
using UnityEngine;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// 表情のキャリブレーションの操作（真顔・表情ごとの登録状況と登録・削除、まとめて登録）と、
    /// 記録中に画面の上中央へ出す「〇〇の顔をしてください」の案内。
    /// 案内は OnGUI で描くため、仮想カメラ・Spout の映像には映らない。
    /// </summary>
    public sealed class ExpressionCalibrationControls
    {
        // 表情名の列の幅
        private const float LabelWidth = 130f;

        // 画面の案内の大きさと上端からの位置。ウィンドウ ID は操作パネル（0x5643）と重ならない
        private const float OverlayWidth = 560f;
        private const float OverlayHeight = 150f;
        private const float OverlayTop = 40f;
        private const int PromptFontSize = 30;
        private const int OverlayWindowId = 0x5660;

        // ウィンドウ関数から読む、いま出している案内
        private static ExpressionCalibrationRecorder _overlayRecorder;
        private static UiTheme _overlayTheme;

        // 案内の大きな文字（テーマが替わったら作り直す）
        private static GUIStyle _promptStyle;
        private static UiTheme _promptTheme;

        public ExpressionCalibrationRecorder Recorder { get; } = new ExpressionCalibrationRecorder();

        /// <summary>
        /// 毎フレーム呼ぶ。記録が終わった表情があれば設定を保存する。
        /// </summary>
        public void Tick(IFaceTrackingProvider tracker, AppSettings settings)
        {
            if (Recorder.Tick(tracker, settings.expressionCalibrations, Time.unscaledDeltaTime, Time.frameCount))
            {
                AppBootstrap.Save();
            }
        }

        public void Draw(IFaceTrackingProvider tracker, AppSettings settings)
        {
            GuiControls.SubHeading(Loc.T("Calibrate expressions to your face", "表情を自分の顔に合わせる（キャリブレーション）",
                "표정을 내 얼굴에 맞추기 (캘리브레이션)", "让表情适配自己的脸（校准）", "讓表情配合自己的臉（校準）"));
            GuiControls.Hint(Loc.T(
                "Register your neutral face and each expression, and the avatar switches when your face gets close to the registered one. Registered expressions ignore the built-in detection. No face images are saved, only the numbers describing your face's movement. Registered separately for each source.",
                "真顔と各表情を登録すると、登録した顔に近づいたときにその表情に切り替わります（登録した表情は組み込みの判定の代わりに使います）。顔の画像は保存せず、顔の動きの数値だけを保存します。入力元ごとに登録します。",
                "무표정과 각 표정을 등록하면 등록한 얼굴에 가까워졌을 때 그 표정으로 바뀝니다 (등록한 표정은 기본 판정 대신 사용합니다). 얼굴 이미지는 저장하지 않고 얼굴 움직임의 수치만 저장합니다. 입력 소스별로 등록합니다.",
                "登记无表情和各个表情后，脸接近登记的样子时就会切换到该表情（已登记的表情代替内置判定）。不保存面部图像，只保存面部动作的数值。按输入源分别登记。",
                "登記無表情和各個表情後，臉接近登記的樣子時就會切換到該表情（已登記的表情代替內建判定）。不儲存臉部影像，只儲存臉部動作的數值。依輸入來源分別登記。"));

            // 記録中は進み具合とやめるボタンだけ
            if (Recorder.IsRunning)
            {
                DrawProgress();
                return;
            }

            DrawOutcome();

            TrackingSource source = tracker.FaceSource;
            bool receiving = tracker.TryGetFrame(out FaceTrackingFrame face) && face.BlendShapes != null;
            if (!receiving)
            {
                GuiControls.Hint(Loc.T("Start tracking and face the camera to register.",
                    "登録するには、トラッキング中にカメラに顔を映してください。",
                    "등록하려면 트래킹 중에 카메라에 얼굴을 비춰 주세요.",
                    "要登记，请在追踪中让摄像头拍到脸。",
                    "要登記，請在追蹤中讓攝影機拍到臉。"));
            }

            List<ExpressionCalibrationSample> samples = settings.expressionCalibrations;
            bool hasNeutral = ExpressionCalibration.Find(samples, source, FaceExpression.Neutral) != null;
            GUI.enabled = receiving;
            DrawRow(FaceExpression.Neutral, samples, source, hasNeutral);
            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                DrawRow(expression, samples, source, hasNeutral);
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Register all (neutral + every expression)", "まとめて登録（真顔 + 全部の表情）",
                    "한꺼번에 등록 (무표정 + 모든 표정)", "一次登记（无表情 + 全部表情）", "一次登記（無表情 + 全部表情）"),
                    GuiControls.Shrinkable))
            {
                Recorder.Start(AllExpressions(), source);
            }

            GUI.enabled = hasNeutral || HasAnyExpression(samples, source);
            if (GuiControls.DangerButton(Loc.T("Clear all", "全部消す", "모두 지우기", "全部清除", "全部清除"),
                    GuiControls.Shrinkable))
            {
                ExpressionCalibration.RemoveAll(samples, source);
                AppBootstrap.Save();
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            GuiControls.Hint(Loc.T(
                "Each step gives you 3 seconds to make the face, then records for 2 seconds. Make the face a little stronger than usual. Expressions without a registration use the built-in detection.",
                "各表情は 3 秒の準備のあと 2 秒記録します。いつもより少しはっきりした顔をしてください。登録していない表情は組み込みの判定を使います。",
                "각 표정은 3초 준비 후 2초 동안 기록합니다. 평소보다 조금 뚜렷한 표정을 지어 주세요. 등록하지 않은 표정은 기본 판정을 사용합니다.",
                "每个表情准备 3 秒后记录 2 秒。请做出比平时稍明显的表情。未登记的表情使用内置判定。",
                "每個表情準備 3 秒後記錄 2 秒。請做出比平時稍明顯的表情。未登記的表情使用內建判定。"));
        }

        private void DrawRow(FaceExpression expression, List<ExpressionCalibrationSample> samples, TrackingSource source,
            bool hasNeutral)
        {
            bool registered = ExpressionCalibration.Find(samples, source, expression) != null;
            UiTheme theme = UiTheme.Current;
            bool wasEnabled = GUI.enabled;

            GUILayout.BeginHorizontal();
            GUILayout.Label(ExpressionName(expression), GUILayout.Width(LabelWidth));

            // 表情は真顔が無いと使えない（真顔から登録し直す）
            string state = !registered ? Loc.T("Not registered", "未登録", "미등록", "未登记", "未登記")
                : expression != FaceExpression.Neutral && !hasNeutral
                    ? Loc.T("Needs neutral", "真顔が必要", "무표정 필요", "需要无表情", "需要無表情")
                    : Loc.T("Registered", "登録済み", "등록됨", "已登记", "已登記");
            bool usable = registered && (expression == FaceExpression.Neutral || hasNeutral);
            GUILayout.Label(state, theme == null ? GUI.skin.label : usable ? theme.HintSuccess : theme.Hint);
            GUILayout.FlexibleSpace();

            if (GUILayout.Button(registered ? Loc.T("Redo", "登録し直す", "다시 등록", "重新登记", "重新登記")
                    : Loc.T("Register", "登録", "등록", "登记", "登記"), GuiControls.Shrinkable))
            {
                // 真顔が無いまま表情だけを登録するときは、先に真顔も記録する
                var queue = new List<FaceExpression>();
                if (expression != FaceExpression.Neutral && !hasNeutral)
                {
                    queue.Add(FaceExpression.Neutral);
                }

                queue.Add(expression);
                Recorder.Start(queue, source);
            }

            // 消すのは顔を受け取っていなくてもできる
            GUI.enabled = registered;
            if (GUILayout.Button(Loc.T("Clear", "消す", "지우기", "清除", "清除"), GuiControls.Shrinkable))
            {
                ExpressionCalibration.Remove(samples, source, expression);
                AppBootstrap.Save();
            }

            GUI.enabled = wasEnabled;
            GUILayout.EndHorizontal();
        }

        private void DrawProgress()
        {
            UiTheme theme = UiTheme.Current;
            GUILayout.Label(Prompt(Recorder.Target), theme != null ? theme.SectionTitle : GUI.skin.label);
            GuiControls.Hint(ProgressText(Recorder));
            if (GUILayout.Button(Loc.T("Stop", "やめる", "그만두기", "停止", "停止"), GuiControls.Shrinkable))
            {
                Recorder.Cancel();
            }
        }

        private void DrawOutcome()
        {
            // 直前の記録の結果（完了は出さない。一覧の「登録済み」で分かるため）
            switch (Recorder.LastOutcome)
            {
                case ExpressionCalibrationRecorder.Outcome.SourceChanged:
                    GuiControls.Warning(Loc.T("Stopped because the source changed.", "入力元が変わったため中止しました。",
                        "입력 소스가 바뀌어 중지했습니다.", "输入源已更改，已中止。", "輸入來源已變更，已中止。"));
                    break;
                case ExpressionCalibrationRecorder.Outcome.NotDistinct:
                    var names = new List<string>();
                    foreach (FaceExpression expression in Recorder.NotDistinct)
                    {
                        names.Add(ExpressionName(expression));
                    }

                    GuiControls.Warning(Loc.T(
                        "Not registered because it looked almost the same as your neutral face: ",
                        "真顔とほとんど違いが無かったため登録しませんでした: ",
                        "무표정과 거의 차이가 없어 등록하지 않았습니다: ",
                        "与无表情几乎没有区别，未登记: ",
                        "與無表情幾乎沒有差別，未登記: ") + string.Join(Loc.T(", ", "、", ", ", "、", "、"), names));
                    break;
            }
        }

        /// <summary>
        /// 記録中なら、画面の上中央に「〇〇の顔をしてください」と残り時間を大きく出す（screenWidth は UI の倍率で割った幅）。
        /// </summary>
        public static void DrawOverlay(ExpressionCalibrationRecorder recorder, float screenWidth)
        {
            if (recorder == null || !recorder.IsRunning)
            {
                return;
            }

            UiTheme theme = UiTheme.Current;
            if (_promptStyle == null || _promptTheme != theme)
            {
                _promptTheme = theme;
                _promptStyle = new GUIStyle(theme != null ? theme.Title : GUI.skin.label)
                {
                    fontSize = PromptFontSize,
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                };
            }

            float width = Mathf.Min(OverlayWidth, screenWidth - 20f);
            var rect = new Rect((screenWidth - width) * 0.5f, OverlayTop, width, OverlayHeight);
            _overlayRecorder = recorder;
            _overlayTheme = theme;
            FrontPrompt.Draw(OverlayWindowId, rect, theme != null ? theme.Card : GUI.skin.box, DrawOverlayWindow);
        }

        private static void DrawOverlayWindow(int id)
        {
            UiTheme theme = _overlayTheme;
            GUILayout.FlexibleSpace();
            GUILayout.Label(Prompt(_overlayRecorder.Target), _promptStyle);
            GUILayout.Label(ProgressText(_overlayRecorder), theme != null ? theme.Centered : GUI.skin.label);
            GUILayout.FlexibleSpace();
        }

        private static string ProgressText(ExpressionCalibrationRecorder recorder)
        {
            // 準備中は残り秒数、記録中は顔を保つよう促す（顔が見つからなければその旨）
            string text;
            if (recorder.IsWaitingForFace && recorder.Current == ExpressionCalibrationRecorder.Phase.Record)
            {
                text = Loc.T("Face not found. Face the camera.", "顔が見つかりません。カメラに顔を向けてください。",
                    "얼굴을 찾을 수 없습니다. 카메라를 향해 주세요.", "找不到脸。请面向摄像头。", "找不到臉。請面向攝影機。");
            }
            else if (recorder.Current == ExpressionCalibrationRecorder.Phase.Prepare)
            {
                int seconds = Mathf.CeilToInt(recorder.Remaining);
                text = Loc.T($"Recording in {seconds}...", $"{seconds} 秒後に記録します…", $"{seconds}초 후 기록합니다…",
                    $"{seconds} 秒后开始记录…", $"{seconds} 秒後開始記錄…");
            }
            else
            {
                text = Loc.T("Recording... hold the face", "記録中… そのまま保ってください", "기록 중… 그대로 유지하세요",
                    "记录中… 请保持", "記錄中… 請保持");
            }

            // まとめて登録中は残りの数も出す
            if (recorder.QueuedCount > 0)
            {
                text += Loc.T($"  ({recorder.QueuedCount} more)", $"（あと {recorder.QueuedCount} つ）",
                    $" (남은 {recorder.QueuedCount}개)", $"（还剩 {recorder.QueuedCount} 个）", $"（還剩 {recorder.QueuedCount} 個）");
            }

            return text;
        }

        private static string Prompt(FaceExpression expression)
        {
            switch (expression)
            {
                case FaceExpression.Smile:
                    return Loc.T("Smile", "笑顔にしてください", "웃어 주세요", "请微笑", "請微笑");
                case FaceExpression.Surprise:
                    return Loc.T("Make a surprised face", "驚いた顔をしてください", "놀란 표정을 지어 주세요",
                        "请做出惊讶的表情", "請做出驚訝的表情");
                case FaceExpression.Angry:
                    return Loc.T("Make an angry face", "怒った顔をしてください", "화난 표정을 지어 주세요",
                        "请做出生气的表情", "請做出生氣的表情");
                case FaceExpression.Sad:
                    return Loc.T("Make a sad face", "悲しい顔をしてください", "슬픈 표정을 지어 주세요",
                        "请做出悲伤的表情", "請做出悲傷的表情");
                case FaceExpression.Wink:
                    return Loc.T("Wink", "ウインクしてください", "윙크해 주세요", "请眨单眼", "請眨單眼");
                case FaceExpression.Squint:
                    return Loc.T("Half-close your eyes", "ジト目にしてください", "실눈을 떠 주세요", "请眯眼", "請瞇眼");
                case FaceExpression.Pout:
                    return Loc.T("Pout", "ふくれっ面をしてください", "뾰로통한 표정을 지어 주세요", "请嘟嘴", "請嘟嘴");
                default:
                    return Loc.T("Relax your face (neutral)", "真顔にしてください", "무표정을 해 주세요",
                        "请保持无表情", "請保持無表情");
            }
        }

        private static string ExpressionName(FaceExpression expression)
        {
            return expression == FaceExpression.Neutral
                ? Loc.T("Neutral", "真顔", "무표정", "无表情", "無表情")
                : TrackingSection.ExpressionLabel(expression);
        }

        private static IEnumerable<FaceExpression> AllExpressions()
        {
            yield return FaceExpression.Neutral;
            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                yield return expression;
            }
        }

        private static bool HasAnyExpression(List<ExpressionCalibrationSample> samples, TrackingSource source)
        {
            for (var expression = FaceExpressions.First; expression <= FaceExpressions.Last; expression++)
            {
                if (ExpressionCalibration.Find(samples, source, expression) != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
