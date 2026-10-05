using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// デバッグログタブ（カメラが認識されない等の原因調査用）。
    /// 環境・トラッカーの状態の要約と、アプリ内に保持したログを重要度・カテゴリ・文字列で絞り込んで表示し、コピーできる。
    /// </summary>
    public class LogSection
    {
        // ログのカテゴリ名
        private const string LogCategory = "DebugLog";

        // 一度に表示する最大件数（IMGUI は全件を毎フレーム配置するため、多すぎると重くなる）
        private const int MaxShown = 300;

        // エラーのスタックトレースを表示する最大行数（全文はコピーに含める）
        private const int MaxStackLines = 6;

        // 重要度ラベルの列幅
        private const float LevelWidth = 56f;

        // ログが増え続けている間に一覧を作り直す最短間隔（秒。トラッカー出力が多いときに毎フレームの全件走査を避ける）
        private const float RebuildInterval = 0.25f;

        // 重要度ごとの表示名（LogLevel の順。ログでよく使う英語の略称で、言語に関係なく同じ）
        private static readonly string[] LevelNames = { "DEBUG", "INFO", "WARN", "ERROR" };

        private readonly TrackerProcess _process;
        private readonly IFaceTrackingProvider _receiver;
        private readonly AppSettings _settings;

        // 絞り込み条件（重要度ごとの表示 ON/OFF、カテゴリ（-1 = すべて）、検索文字列、新しい順か）
        private readonly bool[] _showLevel = { true, true, true, true };
        private int _category = -1;
        private string _search = string.Empty;
        private bool _newestFirst = true;

        // 表示用の一覧（Layout と Repaint で配置が食い違わないよう、Layout の時だけ作り直す）
        private readonly List<LogBuffer.Entry> _all = new List<LogBuffer.Entry>();
        private readonly List<LogBuffer.Entry> _shown = new List<LogBuffer.Entry>();
        private readonly List<string> _categories = new List<string>();
        private int _matchedCount;
        private int _builtVersion = -1;
        private bool _filterChanged = true;
        private float _nextRebuildTime;

        // コピー直後の表示（どちらをコピーしたか）。押した瞬間に行を増やすと配置が食い違うため、次の Layout で反映する
        private string _copiedMessage;
        private string _nextCopiedMessage;

        public LogSection(TrackerProcess process, IFaceTrackingProvider receiver, AppSettings settings)
        {
            _process = process;
            _receiver = receiver;
            _settings = settings;
        }

        public void Draw()
        {
            // 条件の変更はすぐ、ログの追加・消去は間隔を空けて、Layout の時だけ一覧を作り直す
            if (Event.current.type == EventType.Layout)
            {
                _copiedMessage = _nextCopiedMessage;
                bool logChanged = _builtVersion != LogBuffer.Version && Time.unscaledTime >= _nextRebuildTime;
                if (logChanged || _filterChanged)
                {
                    Rebuild();
                    _nextRebuildTime = Time.unscaledTime + RebuildInterval;
                }
            }

            DrawDiagnostics();
            DrawFilters();
            DrawEntries();
        }

        private void DrawDiagnostics()
        {
            GuiControls.BeginCard(Loc.T("Environment", "環境", "환경", "环境", "環境"));

            // カメラ・トラッカーの不調の切り分けに要る情報だけを並べる
            foreach (string line in DiagnosticLines())
            {
                GuiControls.Hint(line);
            }

            if (GUILayout.Button(Loc.T("Copy environment", "環境をコピー", "환경 복사", "复制环境信息", "複製環境資訊"),
                    GuiControls.Shrinkable))
            {
                GUIUtility.systemCopyBuffer = string.Join("\n", DiagnosticLines());
                _nextCopiedMessage = Loc.T("Environment copied.", "環境をコピーしました。", "환경을 복사했습니다.",
                    "已复制环境信息。", "已複製環境資訊。");
            }

            GuiControls.EndCard();
        }

        private List<string> DiagnosticLines()
        {
            // アプリ・OS・GPU（描画の不具合）、トラッカーと受信の状態（カメラ・トラッキングの不具合）
            var lines = new List<string>
            {
                $"VRCast {Application.version} / Unity {Application.unityVersion}",
                $"OS: {SystemInfo.operatingSystem}",
                $"CPU: {SystemInfo.processorType} ({SystemInfo.processorCount})",
                $"GPU: {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), " +
                    $"setting {(_settings.gpuAdapter.Length > 0 ? _settings.gpuAdapter : _settings.gpuPreference.ToString())}",
                $"Process priority: {_settings.processPriority}",
                $"Tracking: {_settings.trackingSource}, " +
                    (_settings.trackingEnabled ? "enabled" : "disabled") +
                    (_settings.lowLoadMode ? ", low load" : string.Empty),
                $"Tracker: {_process.Status}",
                $"Receiver: {_receiver.Status}",
                $"Selected camera: {(string.IsNullOrEmpty(_settings.trackerCamera) ? "-" : _settings.trackerCamera)}",
            };

            // カメラ一覧（番号はトラッカーに渡すカメラ番号）
            IReadOnlyList<string> cameras = _process.Cameras;
            lines.Add($"Cameras ({cameras.Count}):");
            for (int i = 0; i < cameras.Count; i++)
            {
                lines.Add($"  {i}: {cameras[i]}");
            }

            return lines;
        }

        private void DrawFilters()
        {
            GuiControls.BeginCard(Loc.T("Debug log", "デバッグログ", "디버그 로그", "调试日志", "偵錯日誌"));

            // 詳細ログ（DEBUG）の記録 ON/OFF。受信統計などが定期的に増えるため、調査するときだけ ON にしてもらう
            _settings.detailedLogging = GUILayout.Toggle(_settings.detailedLogging, Loc.T(
                "Detailed logging (tracker statistics and state changes, DEBUG)",
                "詳細ログ（トラッカーの受信統計や状態の変化。DEBUG）",
                "상세 로그 (트래커 수신 통계 및 상태 변화, DEBUG)",
                "详细日志（追踪器接收统计和状态变化，DEBUG）",
                "詳細日誌（追蹤器接收統計和狀態變化，DEBUG）"));
            GuiControls.Hint(Loc.T(
                "Turn on only while investigating. It is saved and stays on after restarting.",
                "調査するときだけ ON にしてください。設定は保存され、再起動後も ON のままです。",
                "조사할 때만 켜 주세요. 설정은 저장되어 재시작 후에도 켜진 상태로 유지됩니다.",
                "请仅在排查问题时开启。设置会被保存，重启后仍保持开启。",
                "請僅在排查問題時開啟。設定會被儲存，重新啟動後仍保持開啟。"));

            // 重要度ごとの表示 ON/OFF（件数付き。選択中はアクセント色）
            GUILayout.BeginHorizontal();
            for (int i = 0; i < LevelNames.Length; i++)
            {
                string label = $"{LevelNames[i]} ({LogBuffer.CountOf((LogLevel)i)})";
                bool show = GUILayout.Toggle(_showLevel[i], label, GUI.skin.button, GuiControls.Shrinkable);
                if (show != _showLevel[i])
                {
                    _showLevel[i] = show;
                    _filterChanged = true;
                }
            }

            GUILayout.EndHorizontal();

            // カテゴリ（ログの出どころ。Tracker / TrackerOutput がカメラ関係）
            int category = GuiControls.Selector(
                Loc.T("Category", "カテゴリ", "카테고리", "类别", "類別"), _categories, _category,
                Loc.T("All", "すべて", "전체", "全部", "全部"));
            if (category != _category)
            {
                _category = category;
                _filterChanged = true;
            }

            // 文字列で絞り込み（大文字・小文字は区別しない）
            GUILayout.BeginHorizontal();
            GUILayout.Label(Loc.T("Search", "検索", "검색", "搜索", "搜尋"), GUILayout.ExpandWidth(false));
            string search = GUILayout.TextField(_search, GuiControls.Shrinkable);
            if (search != _search)
            {
                _search = search;
                _filterChanged = true;
            }

            GUILayout.EndHorizontal();

            // 並び順・コピー・消去・ログフォルダ
            GUILayout.BeginHorizontal();
            string order = _newestFirst
                ? Loc.T("Newest first", "新しい順", "최신순", "最新在前", "最新在前")
                : Loc.T("Oldest first", "古い順", "오래된 순", "最早在前", "最早在前");
            if (GUILayout.Button(order, GuiControls.Shrinkable))
            {
                _newestFirst = !_newestFirst;
                _filterChanged = true;
            }

            if (GUILayout.Button(Loc.T("Copy shown", "表示中をコピー", "표시 중 복사", "复制显示内容", "複製顯示內容"),
                    GuiControls.Shrinkable))
            {
                GUIUtility.systemCopyBuffer = FormatForCopy();
                _nextCopiedMessage = Loc.T("Shown logs and environment copied.", "表示中のログと環境をコピーしました。",
                    "표시 중인 로그와 환경을 복사했습니다.", "已复制显示的日志和环境信息。", "已複製顯示的日誌和環境資訊。");
            }

            if (GUILayout.Button(Loc.T("Clear", "消去", "지우기", "清除", "清除"), GuiControls.Shrinkable))
            {
                LogBuffer.Clear();
                _nextCopiedMessage = null;
            }

            if (GUILayout.Button(Loc.T("Log folder", "ログフォルダ", "로그 폴더", "日志文件夹", "日誌資料夾"),
                    GuiControls.Shrinkable))
            {
                OpenLogFolder();
            }

            GUILayout.EndHorizontal();

            // コピー結果と、表示件数（上限で切った場合はその旨）
            if (!string.IsNullOrEmpty(_copiedMessage))
            {
                GuiControls.Hint(_copiedMessage);
            }

            string count = Loc.T("Shown", "表示", "표시", "显示", "顯示") + $": {_shown.Count} / {_matchedCount}";
            if (_matchedCount > _shown.Count)
            {
                count += Loc.T($" (latest {MaxShown} only)", $"（最新の {MaxShown} 件のみ）", $" (최신 {MaxShown}건만)",
                    $"（仅最新 {MaxShown} 条）", $"（僅最新 {MaxShown} 筆）");
            }

            GuiControls.Hint(count);
            GuiControls.Hint(Loc.T(
                "Logs are cleared when VRCast closes. Player.log in the log folder keeps everything except tracker output and DEBUG.",
                "ログは VRCast を閉じると消えます。ログフォルダの Player.log には、トラッカーの出力と DEBUG 以外が残ります。",
                "로그는 VRCast를 닫으면 사라집니다. 로그 폴더의 Player.log에는 트래커 출력과 DEBUG 외의 내용이 남습니다.",
                "关闭 VRCast 后日志会被清除。日志文件夹中的 Player.log 会保留除追踪器输出和 DEBUG 以外的内容。",
                "關閉 VRCast 後日誌會被清除。日誌資料夾中的 Player.log 會保留追蹤器輸出和 DEBUG 以外的內容。"));
            GuiControls.EndCard();
        }

        private void DrawEntries()
        {
            UiTheme theme = UiTheme.Current;
            GuiControls.BeginCard(Loc.T("Entries", "ログ", "로그", "日志", "日誌"));

            if (_shown.Count == 0)
            {
                GuiControls.Hint(Loc.T("No logs match.", "該当するログはありません。", "해당하는 로그가 없습니다.",
                    "没有符合条件的日志。", "沒有符合條件的日誌。"));
            }

            foreach (LogBuffer.Entry entry in _shown)
            {
                // 重要度（色付き）と、時刻・カテゴリ・本文
                GUILayout.BeginHorizontal();
                GUILayout.Label(LevelNames[(int)entry.Level], LevelStyle(theme, entry.Level), GUILayout.Width(LevelWidth));
                GUILayout.Label(FormatLine(entry), GuiControls.Shrinkable);
                GUILayout.EndHorizontal();

                // エラーは発生箇所を先頭の数行だけ添える
                if (!string.IsNullOrEmpty(entry.StackTrace))
                {
                    GuiControls.Hint(FirstLines(entry.StackTrace, MaxStackLines));
                }
            }

            GuiControls.EndCard();
        }

        private void Rebuild()
        {
            _builtVersion = LogBuffer.Version;
            _filterChanged = false;
            LogBuffer.CopyTo(_all);

            // カテゴリの候補は保持中のログから集める（選択中のカテゴリは名前で引き継ぐ）
            string selected = _category >= 0 && _category < _categories.Count ? _categories[_category] : null;
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LogBuffer.Entry entry in _all)
            {
                names.Add(entry.Category);
            }

            _categories.Clear();
            _categories.AddRange(names);
            _category = selected != null ? _categories.IndexOf(selected) : -1;

            // 条件に合うものを古い順に集め、上限を超えたら新しいほうを残す
            var matched = new List<LogBuffer.Entry>();
            foreach (LogBuffer.Entry entry in _all)
            {
                if (Matches(entry, selected))
                {
                    matched.Add(entry);
                }
            }

            _matchedCount = matched.Count;
            int skip = Math.Max(0, matched.Count - MaxShown);
            _shown.Clear();
            _shown.AddRange(matched.GetRange(skip, matched.Count - skip));

            // 新しい順なら逆に並べる
            if (_newestFirst)
            {
                _shown.Reverse();
            }
        }

        private bool Matches(LogBuffer.Entry entry, string category)
        {
            // 重要度・カテゴリ・検索文字列（本文とカテゴリが対象）のすべてに合うもの
            if (!_showLevel[(int)entry.Level])
            {
                return false;
            }

            if (category != null && !string.Equals(entry.Category, category, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return string.IsNullOrEmpty(_search)
                || entry.Message.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.Category.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string FormatForCopy()
        {
            // 問い合わせに貼れるよう、環境と表示中のログ（スタックトレースは全文）をまとめる
            var text = new StringBuilder();
            foreach (string line in DiagnosticLines())
            {
                text.AppendLine(line);
            }

            text.AppendLine();
            foreach (LogBuffer.Entry entry in _shown)
            {
                text.AppendLine($"{LevelNames[(int)entry.Level]} {FormatLine(entry)}");
                if (!string.IsNullOrEmpty(entry.StackTrace))
                {
                    text.AppendLine(entry.StackTrace.TrimEnd());
                }
            }

            return text.ToString();
        }

        private static string FormatLine(LogBuffer.Entry entry)
        {
            // 時刻・カテゴリ・本文（連続した同じログはまとめて回数を添える）
            string line = $"{entry.Time:HH:mm:ss} [{entry.Category}] {entry.Message}";
            return entry.Count > 1 ? $"{line} (×{entry.Count})" : line;
        }

        private static void OpenLogFolder()
        {
            // Player.log のあるフォルダ（取得できなければ設定と同じ保存先）をエクスプローラーで開く
            string logPath = Application.consoleLogPath;
            string folder = string.IsNullOrEmpty(logPath) ? Application.persistentDataPath : Path.GetDirectoryName(logPath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            {
                return;
            }

            try
            {
                // 日本語を含むパスでも確実に開けるよう、URL ではなくエクスプローラーにパスを渡す
                Process.Start("explorer.exe", $"\"{folder}\"")?.Dispose();
            }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
                VRCastLog.Warning(LogCategory, "Failed to open log folder: " + e.Message);
            }
        }

        private static GUIStyle LevelStyle(UiTheme theme, LogLevel level)
        {
            // テーマ未作成時は既定のラベル
            if (theme == null)
            {
                return GUI.skin.label;
            }

            // 警告は黄、エラーは赤、通常は標準の文字、詳細は控えめな文字
            switch (level)
            {
                case LogLevel.Error:
                    return theme.ErrorText;
                case LogLevel.Warning:
                    return theme.WarningText;
                case LogLevel.Info:
                    return GUI.skin.label;
                default:
                    return theme.Hint;
            }
        }

        private static string FirstLines(string text, int maxLines)
        {
            // 先頭から maxLines 行まで（それ以上あれば省略記号）
            string[] lines = text.TrimEnd().Split('\n');
            if (lines.Length <= maxLines)
            {
                return string.Join("\n", lines);
            }

            return string.Join("\n", lines, 0, maxLines) + "\n…";
        }
    }
}
