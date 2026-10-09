using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// Settings タブの「設定プリセット」カード。選んだカテゴリの設定を .vrcastpreset へ書き出し、
    /// 読み込むときはファイルに入っているカテゴリを確認・選択してから反映する（ドロップしたファイルも同じ確認を経る）。
    /// </summary>
    public class PresetSection
    {
        // 書き出すときの最初のファイル名
        private const string DefaultFileName = "VRCast" + SettingsPreset.Extension;

        // 結果表示の種類
        private enum Result
        {
            None,
            Exported,
            ExportFailed,
            Applied,
            ApplyFailed,
            ReadFailed,
            TooLarge,
            NotPreset,
            Malformed,
            Empty,
        }

        private readonly AppSettings _settings;
        private readonly Action _reapply;

        // 書き出すカテゴリ（初期状態は全部）
        private readonly HashSet<string> _exportSelection = new HashSet<string>();

        // 読み込み確認中のプリセット・ファイル名・反映するカテゴリ（null = 確認中でない）
        private PresetFile _pending;
        private string _pendingName;
        private readonly HashSet<string> _importSelection = new HashSet<string>();

        // 直前の結果と、その対象（ファイル名・反映したカテゴリ名）
        private Result _result;
        private string _resultDetail = string.Empty;

        // 最後に使ったファイル（ダイアログの最初のフォルダ。起動中だけ覚える）
        private string _lastPath = string.Empty;

        /// <param name="reapply">設定を書き換えた後に各機能へ反映し直す処理</param>
        public PresetSection(AppSettings settings, Action reapply)
        {
            _settings = settings;
            _reapply = reapply;
            SelectAll(SettingsPreset.Categories, _exportSelection);
        }

        public void Draw()
        {
            GuiControls.BeginCard(Loc.T("Settings preset", "設定プリセット", "설정 프리셋", "设置预设", "設定預設"));
            GuiControls.Hint(Loc.T(
                "Save settings to a .vrcastpreset file (readable text) and load them on another PC or later. " +
                "Per-avatar data (shape keys, camera, expression keys) and PC-specific values " +
                "(microphone, camera, GPU name, ports) are not included.",
                "設定を .vrcastpreset ファイル（テキストで読めます）に書き出し、別の PC や後から読み込めます。" +
                "アバターごとの記録（シェイプキー・カメラ・表情のキー）と PC ごとの値（マイク・カメラ・GPU 名・ポート）は含みません。",
                "설정을 .vrcastpreset 파일(텍스트로 읽을 수 있음)로 내보내고 다른 PC나 나중에 불러올 수 있습니다. " +
                "아바타별 기록(셰이프 키·카메라·표정 키)과 PC별 값(마이크·카메라·GPU 이름·포트)은 포함하지 않습니다.",
                "可将设置导出为 .vrcastpreset 文件（可作为文本阅读），并在其他电脑或之后导入。" +
                "不包含各虚拟形象的记录（形态键·相机·表情按键）和各电脑的值（麦克风·摄像头·GPU 名称·端口）。",
                "可將設定匯出為 .vrcastpreset 檔案（可作為文字閱讀），並在其他電腦或之後匯入。" +
                "不包含各虛擬形象的紀錄（形態鍵·相機·表情按鍵）和各電腦的值（麥克風·攝影機·GPU 名稱·連接埠）。"));

            // 確認中は読み込み、それ以外は書き出しと読み込みのボタン
            if (_pending != null)
            {
                DrawImportConfirm();
            }
            else
            {
                DrawExport();
            }

            DrawResult();
            GuiControls.EndCard();
        }

        /// <summary>
        /// ファイルを読んで確認欄を出す（まだ反映しない）。読めなければ理由を表示する。
        /// </summary>
        public void Open(string path)
        {
            _lastPath = path;
            _pending = null;
            _pendingName = Path.GetFileName(path);
            _resultDetail = _pendingName;

            // 大きすぎるファイルは読まない
            string text;
            try
            {
                if (new FileInfo(path).Length > SettingsPreset.MaxFileBytes)
                {
                    _result = Result.TooLarge;
                    return;
                }

                text = File.ReadAllText(path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException)
            {
                _result = Result.ReadFailed;
                return;
            }

            // 解析できたら、入っているカテゴリを全部選んだ状態で確認欄を出す
            if (!SettingsPreset.TryParse(text, out PresetFile preset, out PresetError error))
            {
                _result = ToResult(error);
                return;
            }

            _pending = preset;
            _importSelection.Clear();
            SelectAll(preset.Categories, _importSelection);
            _result = Result.None;
        }

        /// <summary>
        /// パスがプリセットの拡張子なら true（ドロップされたファイルの振り分け用）。
        /// </summary>
        public static bool IsPresetFile(string path)
        {
            return !string.IsNullOrEmpty(path)
                && string.Equals(Path.GetExtension(path), SettingsPreset.Extension, StringComparison.OrdinalIgnoreCase);
        }

        private void DrawExport()
        {
            GuiControls.SubHeading(Loc.T("Export", "書き出し", "내보내기", "导出", "匯出"));
            DrawCategorySelection(SettingsPreset.Categories, _exportSelection);

            // 選んだカテゴリが無ければ押せない
            GUI.enabled = FileDialog.IsSupported && _exportSelection.Count > 0;
            if (GUILayout.Button(Loc.T("Export...", "書き出す...", "내보내기...", "导出...", "匯出..."), GuiControls.Shrinkable))
            {
                Export();
            }

            GUI.enabled = true;

            GuiControls.SubHeading(Loc.T("Import", "読み込み", "불러오기", "导入", "匯入"));
            GuiControls.Hint(Loc.T(
                "After choosing a file you can pick which categories to apply. You can also drop a .vrcastpreset file onto the window.",
                "ファイルを選んだ後で、反映するカテゴリを選べます。.vrcastpreset ファイルをウィンドウにドロップしても読み込めます。",
                "파일을 고른 뒤 적용할 카테고리를 선택할 수 있습니다. .vrcastpreset 파일을 창에 끌어다 놓아도 불러옵니다.",
                "选择文件后可以选择要应用的类别。也可以将 .vrcastpreset 文件拖放到窗口中导入。",
                "選擇檔案後可以選擇要套用的類別。也可以將 .vrcastpreset 檔案拖放到視窗中匯入。"));
            GUI.enabled = FileDialog.IsSupported;
            if (GUILayout.Button(Loc.T("Import...", "読み込む...", "불러오기...", "导入...", "匯入..."), GuiControls.Shrinkable))
            {
                Browse();
            }

            GUI.enabled = true;
        }

        private void DrawImportConfirm()
        {
            GuiControls.SubHeading(Loc.T("Import", "読み込み", "불러오기", "导入", "匯入") + $": {_pendingName}");

            // 作成した版と日時（空なら出さない）
            if (_pending.AppVersion.Length > 0 || _pending.CreatedAt.Length > 0)
            {
                GuiControls.Hint($"VRCast {_pending.AppVersion}  {_pending.CreatedAt}".Trim());
            }

            // 新しい版で作られたファイルは、読める項目だけを反映する
            if (_pending.IsNewerFormat)
            {
                GuiControls.Warning(Loc.T(
                    "⚠ Made with a newer VRCast. Only the settings this version understands are applied.",
                    "⚠ 新しいバージョンの VRCast で作られたプリセットです。この版で読める設定だけを反映します。",
                    "⚠ 새 버전의 VRCast로 만든 프리셋입니다. 이 버전에서 읽을 수 있는 설정만 적용합니다.",
                    "⚠ 此预设由较新版本的 VRCast 创建。仅应用此版本可读取的设置。",
                    "⚠ 此預設由較新版本的 VRCast 建立。僅套用此版本可讀取的設定。"));
            }

            GuiControls.Hint(Loc.T("Choose the categories to apply. Unchecked ones keep their current values.",
                "反映するカテゴリを選んでください。外したカテゴリは今の設定のままです。",
                "적용할 카테고리를 선택하세요. 체크를 해제한 카테고리는 현재 설정 그대로입니다.",
                "请选择要应用的类别。取消勾选的类别保持当前设置。",
                "請選擇要套用的類別。取消勾選的類別保持目前設定。"));
            DrawCategorySelection(_pending.Categories, _importSelection);

            GUILayout.BeginHorizontal();

            // 選んだカテゴリが無ければ反映できない
            GUI.enabled = _importSelection.Count > 0;
            if (GUILayout.Button(Loc.T("Apply", "適用", "적용", "应用", "套用"), GuiControls.Shrinkable))
            {
                ApplyPending();
            }

            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("Cancel", "キャンセル", "취소", "取消", "取消"), GuiControls.Shrinkable))
            {
                _pending = null;
                _result = Result.None;
            }

            GUILayout.EndHorizontal();
        }

        private static void DrawCategorySelection(IReadOnlyList<PresetCategory> categories, HashSet<string> selection)
        {
            // カテゴリごとのチェック（アバター関連には印を付ける）
            foreach (PresetCategory category in categories)
            {
                bool selected = selection.Contains(category.Id);
                string label = CategoryLabel(category);
                if (category.IsAvatarRelated)
                {
                    label += Loc.T(" (avatar)", "（アバター関連）", " (아바타 관련)", "（虚拟形象相关）", "（虛擬形象相關）");
                }

                if (GUILayout.Toggle(selected, label) != selected)
                {
                    // 押したらそのカテゴリの選択を反転
                    if (selected)
                    {
                        selection.Remove(category.Id);
                    }
                    else
                    {
                        selection.Add(category.Id);
                    }
                }
            }

            GUILayout.BeginHorizontal();

            // 全部選ぶ
            if (GUILayout.Button(Loc.T("Select all", "全て選ぶ", "모두 선택", "全选", "全選"), GuiControls.Shrinkable))
            {
                SelectAll(categories, selection);
            }

            // アバター関連（ライト・ポーズ・表情の割り当て）だけをまとめて外す
            if (GUILayout.Button(Loc.T("Exclude avatar-related", "アバター関連を外す", "아바타 관련 제외", "排除虚拟形象相关",
                    "排除虛擬形象相關"), GuiControls.Shrinkable))
            {
                foreach (PresetCategory category in categories)
                {
                    if (category.IsAvatarRelated)
                    {
                        selection.Remove(category.Id);
                    }
                }
            }

            GUILayout.EndHorizontal();
        }

        private void DrawResult()
        {
            // 結果の文言（表示言語の切り替えに追従するよう描画時に作る）
            switch (_result)
            {
                case Result.Exported:
                    GuiControls.Status(Loc.T("Exported", "書き出しました", "내보냈습니다", "已导出", "已匯出") + $": {_resultDetail}",
                        true);
                    break;
                case Result.Applied:
                    GuiControls.Status(Loc.T("Applied", "反映しました", "적용했습니다", "已应用", "已套用") + $": {_resultDetail}",
                        true);
                    break;
                case Result.ExportFailed:
                    GuiControls.Warning(Loc.T("Could not write the file", "ファイルを書き込めませんでした", "파일을 쓰지 못했습니다",
                        "无法写入文件", "無法寫入檔案") + $": {_resultDetail}");
                    break;
                case Result.ApplyFailed:
                    GuiControls.Warning(Loc.T("Some values in the preset could not be read. Nothing was changed.",
                        "プリセットに読めない値がありました。設定は変更していません。",
                        "프리셋에 읽을 수 없는 값이 있었습니다. 설정은 변경하지 않았습니다.",
                        "预设中有无法读取的值。未更改设置。", "預設中有無法讀取的值。未變更設定。"));
                    break;
                case Result.ReadFailed:
                    GuiControls.Warning(Loc.T("Could not read the file", "ファイルを読み込めませんでした", "파일을 읽지 못했습니다",
                        "无法读取文件", "無法讀取檔案") + $": {_resultDetail}");
                    break;
                case Result.TooLarge:
                    GuiControls.Warning(Loc.T("The file is too large to be a preset", "プリセットとしては大きすぎるファイルです",
                        "프리셋으로는 너무 큰 파일입니다", "文件过大，不是预设", "檔案過大，不是預設") + $": {_resultDetail}");
                    break;
                case Result.NotPreset:
                    GuiControls.Warning(Loc.T("This is not a VRCast preset", "VRCast のプリセットではありません",
                        "VRCast 프리셋이 아닙니다", "这不是 VRCast 预设", "這不是 VRCast 預設") + $": {_resultDetail}");
                    break;
                case Result.Malformed:
                    GuiControls.Warning(Loc.T("The preset is broken (check the text if you edited it)",
                        "プリセットが壊れています（手で編集した場合は書式をご確認ください）",
                        "프리셋이 손상되었습니다 (직접 편집했다면 형식을 확인하세요)",
                        "预设已损坏（如果手动编辑过，请检查格式）", "預設已損壞（如果手動編輯過，請檢查格式）") + $": {_resultDetail}");
                    break;
                case Result.Empty:
                    GuiControls.Warning(Loc.T("The preset contains no settings this version can use",
                        "この版で使える設定がプリセットに入っていません", "이 버전에서 사용할 수 있는 설정이 프리셋에 없습니다",
                        "预设中没有此版本可用的设置", "預設中沒有此版本可用的設定") + $": {_resultDetail}");
                    break;
            }
        }

        private void Export()
        {
            // 保存先を選ぶ（最初は前回のフォルダ、無ければドキュメント）
            string initial = _lastPath.Length > 0
                ? _lastPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string path = FileDialog.SaveFile(
                Loc.T("Export settings preset", "設定プリセットを書き出す", "설정 프리셋 내보내기", "导出设置预设", "匯出設定預設"),
                Loc.T("VRCast preset", "VRCast プリセット", "VRCast 프리셋", "VRCast 预设", "VRCast 預設"),
                SettingsPreset.Extension, initial, DefaultFileName);
            if (path == null)
            {
                return;
            }

            _lastPath = path;
            _resultDetail = Path.GetFileName(path);
            try
            {
                // 選んだカテゴリをテキストにして書く
                string text = SettingsPreset.Export(_settings, _exportSelection, Application.version, DateTimeOffset.Now);
                AtomicFile.WriteAllText(path, text);
                _result = Result.Exported;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                || e is NotSupportedException)
            {
                _result = Result.ExportFailed;
                _resultDetail += $" ({e.Message})";
            }
        }

        private void Browse()
        {
            // 読み込むファイルを選ぶ（最初は前回のフォルダ、無ければドキュメント）
            string initial = _lastPath.Length > 0
                ? _lastPath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string path = FileDialog.OpenFile(
                Loc.T("Import settings preset", "設定プリセットを読み込む", "설정 프리셋 불러오기", "导入设置预设", "匯入設定預設"),
                Loc.T("VRCast preset", "VRCast プリセット", "VRCast 프리셋", "VRCast 预设", "VRCast 預設"),
                new[] { SettingsPreset.Extension }, initial);
            if (path != null)
            {
                Open(path);
            }
        }

        private void ApplyPending()
        {
            // 反映したカテゴリ名を結果に出す（並びはファイルのカテゴリの並び）
            var names = new List<string>();
            foreach (PresetCategory category in _pending.Categories)
            {
                if (_importSelection.Contains(category.Id))
                {
                    names.Add(CategoryLabel(category));
                }
            }

            // 値が読めなければ設定は変わらない
            if (!SettingsPreset.Apply(_pending, _importSelection, _settings))
            {
                _result = Result.ApplyFailed;
                return;
            }

            // 設定変更時にしか反映しない機能へ反映し直す
            _reapply();
            _pending = null;
            _result = Result.Applied;
            _resultDetail = string.Join(Loc.T(", ", "、", ", ", "、", "、"), names);
        }

        private static void SelectAll(IReadOnlyList<PresetCategory> categories, HashSet<string> selection)
        {
            foreach (PresetCategory category in categories)
            {
                selection.Add(category.Id);
            }
        }

        private static Result ToResult(PresetError error)
        {
            // 解析の失敗理由を表示の種類へ
            switch (error)
            {
                case PresetError.TooLarge:
                    return Result.TooLarge;
                case PresetError.NotPreset:
                    return Result.NotPreset;
                case PresetError.Empty:
                    return Result.Empty;
                default:
                    return Result.Malformed;
            }
        }

        private static string CategoryLabel(PresetCategory category)
        {
            // カテゴリ名（表示言語に合わせる。知らない ID はそのまま）
            switch (category.Id)
            {
                case "performance":
                    return Loc.T("Performance (low load, priority, CPU, GPU, tracker mode)",
                        "動作最適化（軽量モード・優先度・CPU・GPU・トラッカーの動作）",
                        "동작 최적화 (저부하·우선순위·CPU·GPU·트래커 동작)", "运行优化（低负载·优先级·CPU·GPU·追踪器模式）",
                        "執行最佳化（低負載·優先順序·CPU·GPU·追蹤器模式）");
                case "display":
                    return Loc.T("Background and camera lock", "背景・カメラの固定", "배경·카메라 고정", "背景·相机锁定",
                        "背景·相機鎖定");
                case "motion":
                    return Loc.T("Idle motion and physics", "待機モーション・揺れもの", "대기 모션·흔들림", "待机动作·物理摆动",
                        "待機動作·物理擺動");
                case "face":
                    return Loc.T("Blinking and lip sync", "まばたき・口パク", "눈 깜빡임·립싱크", "眨眼·口型同步", "眨眼·口型同步");
                case "tracking":
                    return Loc.T("Tracking", "トラッキング", "트래킹", "追踪", "追蹤");
                case "output":
                    return Loc.T("Output (virtual camera, Spout2)", "出力（仮想カメラ・Spout2）", "출력 (가상 카메라·Spout2)",
                        "输出（虚拟摄像头·Spout2）", "輸出（虛擬攝影機·Spout2）");
                case "shortcuts":
                    return Loc.T("Shortcut keys and OSC / HTTP", "ショートカットキー・OSC / HTTP", "단축키·OSC / HTTP",
                        "快捷键·OSC / HTTP", "快捷鍵·OSC / HTTP");
                case "interface":
                    return Loc.T("Panel (language, size, theme, updates, logs)", "パネル（言語・大きさ・テーマ・アップデート・ログ）",
                        "패널 (언어·크기·테마·업데이트·로그)", "面板（语言·大小·主题·更新·日志）", "面板（語言·大小·主題·更新·日誌）");
                case "avatarLook":
                    return Loc.T("Light, brightness, outline, pose and direction", "ライト・明るさ・輪郭線・ポーズ・向き",
                        "조명·밝기·윤곽선·포즈·방향", "灯光·亮度·轮廓线·姿势·朝向", "燈光·亮度·輪廓線·姿勢·朝向");
                case "expressionMapping":
                    return Loc.T("Expression mapping and thresholds", "表情反映の割り当て・しきい値", "표정 반영 할당·임계값",
                        "表情映射·阈值", "表情對應·閾值");
                default:
                    return category.Id;
            }
        }
    }
}
