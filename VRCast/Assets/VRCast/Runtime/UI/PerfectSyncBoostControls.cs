using UnityEngine;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンクの口の開きの補正と「口・頬の動きを強調」の設定（ON/OFF・組ごとの倍率・頬のしきい値と、口の開き・頬・口の左右の今の値）。
    /// トラッキングタブと、パーフェクトシンク化の「実際のトラッキングで試す」で同じものを出す。
    /// </summary>
    public static class PerfectSyncBoostControls
    {
        // 数値欄の入力途中（この画面は一度に一つだけ開く）
        private static string _jawDeadzoneInput = string.Empty;
        private static string _jawGainInput = string.Empty;
        private static string _puckerInput = string.Empty;
        private static string _mouthInput = string.Empty;
        private static string _cheekPuffInput = string.Empty;
        private static string _lipsInput = string.Empty;
        private static string _cheekStartInput = string.Empty;
        private static string _cheekFullInput = string.Empty;

        /// <summary>
        /// 口・頬の強調の設定値の控え（試しに変えた値を保存せずに戻すため）。
        /// </summary>
        public readonly struct Snapshot
        {
            private readonly bool _enabled;
            private readonly float _pucker;
            private readonly float _mouth;
            private readonly float _cheekPuff;
            private readonly float _lips;
            private readonly float _cheekStart;
            private readonly float _cheekFull;
            private readonly float _jawDeadzone;
            private readonly float _jawGain;

            private Snapshot(AppSettings settings)
            {
                _jawDeadzone = settings.trackingJawOpenDeadzone;
                _jawGain = settings.trackingJawOpenGain;
                _enabled = settings.trackingPerfectSyncBoost;
                _pucker = settings.trackingBoostPucker;
                _mouth = settings.trackingBoostMouth;
                _cheekPuff = settings.trackingBoostCheekPuff;
                _lips = settings.trackingBoostLips;
                _cheekStart = settings.trackingCheekPuffStart;
                _cheekFull = settings.trackingCheekPuffFull;
            }

            /// <summary>
            /// 今の設定値を控える。
            /// </summary>
            public static Snapshot Capture(AppSettings settings)
            {
                return new Snapshot(settings);
            }

            /// <summary>
            /// 控えた値へ設定を戻す。
            /// </summary>
            public void Restore(AppSettings settings)
            {
                settings.trackingJawOpenDeadzone = _jawDeadzone;
                settings.trackingJawOpenGain = _jawGain;
                settings.trackingPerfectSyncBoost = _enabled;
                settings.trackingBoostPucker = _pucker;
                settings.trackingBoostMouth = _mouth;
                settings.trackingBoostCheekPuff = _cheekPuff;
                settings.trackingBoostLips = _lips;
                settings.trackingCheekPuffStart = _cheekStart;
                settings.trackingCheekPuffFull = _cheekFull;
            }

            /// <summary>
            /// 今の設定値が控えと同じなら true（変えていない）。
            /// </summary>
            public bool Matches(AppSettings settings)
            {
                return Equals(Capture(settings));
            }
        }

        /// <summary>
        /// 設定を描く。driver があれば頬の幅の今の広がりと推定値を出す（しきい値を合わせる目安）。
        /// </summary>
        public static void Draw(AppSettings settings, FaceTrackingDriver driver)
        {
            DrawJawOpen(settings, driver);

            // 口・頬の強調の ON/OFF（パーフェクトシンクで動かしている間だけ効く）
            settings.trackingPerfectSyncBoost = GUILayout.Toggle(
                settings.trackingPerfectSyncBoost,
                Loc.T("Boost mouth and cheeks", "口・頬の動きを強調", "입·볼 움직임 강조", "增强嘴部和脸颊动作", "增強嘴部和臉頰動作"));
            GuiControls.Hint(Loc.T(
                "Only while perfect sync is driving the face. Strengthens pouting, mouth shifting left/right, puffed cheeks and lip movements that the camera tends to pick up weakly.",
                "パーフェクトシンクで顔を動かしている間だけ効きます。カメラでは弱く出やすい、ふくれっ面・口の左右・頬をふくらませる・唇の動きを強めます。",
                "퍼펙트 싱크로 얼굴을 움직이는 동안에만 적용됩니다. 카메라에서 약하게 잡히기 쉬운 입 내밀기·입 좌우·볼 부풀리기·입술 움직임을 강하게 합니다.",
                "仅在完美同步驱动面部时生效。增强摄像头容易识别偏弱的嘟嘴、嘴巴左右移动、鼓腮和嘴唇动作。",
                "僅在完美同步驅動臉部時生效。增強攝影機容易辨識偏弱的嘟嘴、嘴巴左右移動、鼓腮和嘴唇動作。"));

            // 無効時は倍率・しきい値を出さない
            if (!settings.trackingPerfectSyncBoost)
            {
                return;
            }

            DrawGains(settings, driver);
            DrawCheekEstimate(settings, driver);
        }

        private static void DrawJawOpen(AppSettings settings, FaceTrackingDriver driver)
        {
            // 口の開き（パーフェクトシンクで動かしている間だけ効く。強調の ON/OFF とは別）
            GUILayout.Label(Loc.T("Mouth opening (perfect sync)", "口の開き（パーフェクトシンク）", "입 벌림 (퍼펙트 싱크)",
                "张嘴（完美同步）", "張嘴（完美同步）"));
            settings.trackingJawOpenDeadzone = GuiControls.SliderOrField(
                "  " + Loc.T("Ignore below", "無視する開き", "무시할 벌림", "忽略的张开度", "忽略的張開度"),
                settings.trackingJawOpenDeadzone, 0f, AppSettings.MaxJawOpenDeadzone, ref _jawDeadzoneInput,
                "ps-jaw-deadzone", "0.##");
            settings.trackingJawOpenGain = GuiControls.SliderOrField(
                "  " + Loc.T("Strength", "強さ", "강도", "强度", "強度"),
                settings.trackingJawOpenGain, AppSettings.MinJawOpenGain, AppSettings.MaxJawOpenGain, ref _jawGainInput,
                "ps-jaw-gain", "0.#");

            // 受け取った値と補正後の値（トラッキング中だけ。真顔のときの値を見て「無視する開き」を決める）
            if (driver != null && driver.IsTracking)
            {
                float received = driver.JawOpenReceived;
                float applied = driver.CurrentJawOpenAdjustment.Apply(received);
                GuiControls.Status(Loc.T($"Now: received {received:F2}, applied {applied:F2}",
                    $"今: 受信 {received:F2}、反映 {applied:F2}", $"현재: 수신 {received:F2}, 반영 {applied:F2}",
                    $"当前: 接收 {received:F2}，应用 {applied:F2}", $"目前: 接收 {received:F2}，套用 {applied:F2}"),
                    applied > 0f);
            }

            GuiControls.Hint(Loc.T(
                "If the mouth stays slightly open with your mouth closed, set Ignore below a little above the received value with your mouth closed. Lower Strength if the mouth opens too wide, raise it if it barely opens.",
                "口を閉じているのにアバターの口が開く場合は、「無視する開き」を口を閉じたときの受信値より少し上にしてください。口が開きすぎるなら「強さ」を下げ、開きが小さいなら上げます。",
                "입을 다물고 있는데 아바타의 입이 벌어지면 '무시할 벌림'을 입을 다물었을 때의 수신값보다 조금 높게 설정하세요. 입이 너무 크게 벌어지면 '강도'를 낮추고, 작게 벌어지면 올리세요.",
                "闭着嘴时虚拟形象的嘴却张开的话，请把“忽略的张开度”设为比闭嘴时的接收值稍高。嘴张得太大就调低“强度”，张得太小就调高。",
                "閉著嘴時虛擬形象的嘴卻張開的話，請把「忽略的張開度」設為比閉嘴時的接收值稍高。嘴張得太大就調低「強度」，張得太小就調高。"));
        }

        private static void DrawGains(AppSettings settings, FaceTrackingDriver driver)
        {
            // 組ごとの倍率（1 = そのまま）
            float max = AppSettings.MaxPerfectSyncBoost;
            settings.trackingBoostPucker = GuiControls.SliderOrField(
                Loc.T("Pout", "ふくれっ面", "입 내밀기", "嘟嘴", "嘟嘴"),
                settings.trackingBoostPucker, 1f, max, ref _puckerInput, "ps-pucker", "0.#");
            settings.trackingBoostMouth = GuiControls.SliderOrField(
                Loc.T("Mouth left / right", "口の左右", "입 좌우", "嘴左右", "嘴左右"),
                settings.trackingBoostMouth, 1f, max, ref _mouthInput, "ps-mouth", "0.#");
            if (driver != null && driver.IsTracking)
            {
                driver.MouthSideEstimates(out float left, out float right);
                GuiControls.Status(Loc.T($"Mouth now: left {left:F2}, right {right:F2}",
                    $"口の左右 今: 左 {left:F2}、右 {right:F2}", $"입 좌우 현재: 왼쪽 {left:F2}, 오른쪽 {right:F2}",
                    $"嘴左右 当前: 左 {left:F2}，右 {right:F2}", $"嘴左右 目前: 左 {left:F2}，右 {right:F2}"),
                    left > 0f || right > 0f);
            }

            settings.trackingBoostCheekPuff = GuiControls.SliderOrField(
                Loc.T("Puffed cheeks", "頬をふくらませる", "볼 부풀리기", "鼓腮", "鼓腮"),
                settings.trackingBoostCheekPuff, 1f, max, ref _cheekPuffInput, "ps-cheek", "0.#");
            settings.trackingBoostLips = GuiControls.SliderOrField(
                Loc.T("Lips", "唇", "입술", "嘴唇", "嘴唇"),
                settings.trackingBoostLips, 1f, max, ref _lipsInput, "ps-lips", "0.#");
            GuiControls.Hint(Loc.T(
                "Raise a value if the shape barely moves, lower it if it moves too easily. Mouth left / right is estimated from the shape of your face (the camera often misses it).",
                "動きが小さければ値を上げ、すぐ動きすぎるなら下げてください。口の左右は顔の形から推定します（カメラではほとんど出ません）。",
                "움직임이 작으면 값을 올리고, 너무 쉽게 움직이면 낮추세요. 입 좌우는 얼굴 형태로 추정합니다(카메라에서는 거의 나오지 않습니다).",
                "动作太小时调高，太容易动时调低。嘴巴左右根据脸型估算（摄像头几乎检测不到）。",
                "動作太小時調高，太容易動時調低。嘴巴左右依臉型估算（攝影機幾乎偵測不到）。"));
        }

        private static void DrawCheekEstimate(AppSettings settings, FaceTrackingDriver driver)
        {
            // 頬のふくらみの推定のしきい値（頬の幅が真顔より何 % 広がったら出し始め、何 % で最大か）
            GUILayout.Label(Loc.T("Puffed cheek detection (camera / MediaPipe only)",
                "頬のふくらみの検出（カメラ・MediaPipe のみ）", "볼 부풀리기 감지 (카메라·MediaPipe만)",
                "鼓腮检测（仅限摄像头・MediaPipe）", "鼓腮偵測（僅限攝影機・MediaPipe）"));
            float max = AppSettings.MaxCheekWidenPercent;
            settings.trackingCheekPuffStart = GuiControls.SliderOrField(
                "  " + Loc.T("Starts at (%)", "出し始め（%）", "시작 (%)", "开始 (%)", "開始 (%)"),
                settings.trackingCheekPuffStart, 0f, max - AppSettings.MinCheekWidenSpan, ref _cheekStartInput,
                "ps-cheek-start", "0.#");
            settings.trackingCheekPuffFull = GuiControls.SliderOrField(
                "  " + Loc.T("Full at (%)", "最大（%）", "최대 (%)", "最大 (%)", "最大 (%)"),
                settings.trackingCheekPuffFull, 0f, max, ref _cheekFullInput, "ps-cheek-full", "0.#");

            // 最大は出し始めより必ず大きくする（逆転すると推定が反転するため）
            settings.trackingCheekPuffFull = Mathf.Max(settings.trackingCheekPuffFull,
                settings.trackingCheekPuffStart + AppSettings.MinCheekWidenSpan);

            // 今の広がりと推定値（トラッキング中だけ。真顔と頬をふくらませたときの値を見てしきい値を決める）
            if (driver != null && driver.IsTracking)
            {
                float widen = driver.CheekWiden * 100f;
                float puff = driver.CheekPuffEstimate;
                GuiControls.Status(Loc.T($"Now: {widen:F1}% wider, puff {puff:F2}",
                    $"今: {widen:F1}% 広い、ふくらみ {puff:F2}", $"현재: {widen:F1}% 넓음, 부풀림 {puff:F2}",
                    $"当前: 宽 {widen:F1}%，鼓腮 {puff:F2}", $"目前: 寬 {widen:F1}%，鼓腮 {puff:F2}"), puff > 0f);
            }

            GuiControls.Hint(Loc.T(
                "Measures how much wider your lower cheeks are than your neutral face. Keep a neutral face for a few seconds after starting. Set Starts at a little above the value with a neutral face, and Full at about the value with puffed cheeks.",
                "頬の下側の幅が真顔よりどれだけ広いかで測ります。開始後の数秒は真顔にしておいてください。「出し始め」は真顔のときの値より少し上に、「最大」は頬をふくらませたときの値くらいにします。",
                "볼 아래쪽 폭이 무표정일 때보다 얼마나 넓은지로 측정합니다. 시작 후 몇 초 동안 무표정을 유지하세요. '시작'은 무표정일 때 값보다 조금 위로, '최대'는 볼을 부풀렸을 때 값 정도로 설정합니다.",
                "根据脸颊下侧比面无表情时宽多少来测量。开始后的几秒请保持面无表情。“开始”设为比面无表情时的数值稍高，“最大”设为鼓腮时的数值左右。",
                "依臉頰下側比面無表情時寬多少來測量。開始後的幾秒請保持面無表情。「開始」設為比面無表情時的數值稍高，「最大」設為鼓腮時的數值左右。"));
        }
    }
}
