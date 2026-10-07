using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.PerfectSync;
using VRCast.Platform;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンクの作成モードの画面（開いている間はタブの代わりにパネル全体へ表示する）。
    /// ARKit 名の一覧・ブラシ・既存の BlendShape の合成・左右反転コピー・確認・保存・読み込み・終了を扱う。
    /// パーフェクトシンク設定タブ（DrawTab）とトラッキングタブの入口（DrawEntry）から開く。
    /// </summary>
    public class PerfectSyncEditorSection
    {
        // ログのカテゴリ名
        private const string LogCategory = "PerfectSync";

        // ARKit 名の一覧の列数・欄の高さ
        private const int GridColumns = 3;
        private const float GridHeight = 260f;

        // 合成の候補の欄の高さと、表示する最大件数
        private const float MixListHeight = 160f;
        private const int MaxMixResults = 200;

        // ブラシの半径の表示単位（m → mm）
        private const float MillimetersPerMeter = 1000f;

        private static readonly string[] ShapeNames = ArKitFace.BlendShapeNames;

        private readonly AvatarSession _session;
        private readonly OrbitCameraController _orbit;
        private readonly AvatarComponentCache _avatar;

        private PerfectSyncSculptor _sculptor;

        // 直近の操作の結果（保存・読み込み等）
        private string _status;
        private bool _statusOk;

        // 未保存のまま終わろうとして確認中なら true
        private bool _confirmExit;

        // 一覧・合成の候補のスクロール位置
        private Vector2 _gridScroll;
        private Vector2 _mixScroll;

        // 合成: 対象のメッシュ・検索文字列・足す割合（0〜1）と、メッシュの BlendShape 名の控え
        private int _mixMesh;
        private string _mixSearch = string.Empty;
        private float _mixWeight = 1f;
        private string[] _mixNames;
        private int _mixNamesMesh = -1;

        public PerfectSyncEditorSection(AvatarSession session, OrbitCameraController orbit)
        {
            _session = session;
            _orbit = orbit;
            _avatar = new AvatarComponentCache(session);
        }

        /// <summary>
        /// 作成モード中なら true（アバターを替えると自動で閉じる）。
        /// </summary>
        public bool IsOpen => _sculptor != null;

        /// <summary>
        /// パーフェクトシンク設定タブ（説明と入口）を描く。
        /// </summary>
        public void DrawTab()
        {
            GuiControls.BeginCard(Loc.T("Perfect sync shapes", "パーフェクトシンクの形状", "퍼펙트 싱크 셰이프", "完美同步形状",
                "完美同步形狀"));
            GuiControls.Hint(Loc.T(
                "BETA: Make the ARKit blend shapes used by perfect sync inside VRCast, for avatars that do not have them.",
                "ベータ版: パーフェクトシンクで使う ARKit 名の BlendShape を、持っていないアバター向けに VRCast の中で作れます。",
                "베타: 퍼펙트 싱크에 쓰는 ARKit 이름의 BlendShape를, 갖고 있지 않은 아바타용으로 VRCast 안에서 만들 수 있습니다.",
                "测试版：可以在 VRCast 中为没有 ARKit 名称 BlendShape 的虚拟形象制作完美同步所用的形状。",
                "測試版：可以在 VRCast 中為沒有 ARKit 名稱 BlendShape 的虛擬形象製作完美同步所用的形狀。"));

            // アバター未表示なら読み込みを案内する
            _avatar.Refresh();
            if (!_avatar.HasAvatar)
            {
                GuiControls.Hint(Loc.T("Load an avatar first.", "先にアバターを読み込んでください。", "먼저 아바타를 불러오세요.",
                    "请先加载虚拟形象。", "請先載入虛擬形象。"));
            }
            else
            {
                DrawEntry();
            }

            GuiControls.EndCard();
        }

        /// <summary>
        /// 入口（作った形状の数と、作成モードを開くボタン）を描く。形状を作れないアバターなら理由を出す。
        /// </summary>
        public void DrawEntry()
        {
            // アバター未表示なら何も出さない
            _avatar.Refresh();
            var custom = _avatar.Get<CustomPerfectSync>();
            if (custom == null)
            {
                return;
            }

            // 頂点を読めるメッシュが無ければ、書き出しツールのオプションを案内する
            if (custom.Meshes.Count == 0)
            {
                GuiControls.Hint(Loc.T(
                    "To make perfect sync shapes in VRCast, re-export the avatar with \"Editable face meshes (Perfect Sync)\" turned on in the converter.",
                    "VRCast でパーフェクトシンクの形状を作るには、書き出しツールで「顔メッシュを編集可能にする（パーフェクトシンク）」を ON にして書き出し直してください。",
                    "VRCast에서 퍼펙트 싱크 셰이프를 만들려면 변환 도구에서 \"얼굴 메시 편집 가능 (퍼펙트 싱크)\"를 켜고 다시 내보내세요.",
                    "要在 VRCast 中制作完美同步形状，请在转换工具中开启“面部网格可编辑（完美同步）”后重新导出。",
                    "要在 VRCast 中製作完美同步形狀，請在轉換工具中開啟「臉部網格可編輯（完美同步）」後重新匯出。"));
                return;
            }

            int count = custom.ShapeCount;
            GuiControls.Hint(Loc.T(
                $"Shapes made in VRCast: {count}/{ShapeNames.Length} (saved in the .vrcaster)",
                $"VRCast で作った形状: {count}/{ShapeNames.Length} 個（.vrcaster に保存）",
                $"VRCast에서 만든 셰이프: {count}/{ShapeNames.Length}개 (.vrcaster에 저장)",
                $"在 VRCast 中制作的形状：{count}/{ShapeNames.Length} 个（保存在 .vrcaster 中）",
                $"在 VRCast 中製作的形狀：{count}/{ShapeNames.Length} 個（儲存在 .vrcaster 中）"));
            if (GUILayout.Button(Loc.T("Make perfect sync shapes...", "パーフェクトシンクの形状を作る...",
                    "퍼펙트 싱크 셰이프 만들기...", "制作完美同步形状...", "製作完美同步形狀..."), GuiControls.Shrinkable))
            {
                Open(custom);
            }
        }

        /// <summary>
        /// 作成モードの画面を描く（開いていなければ何もしない）。
        /// </summary>
        public void Draw()
        {
            // アバターを替えた・閉じた後は何もしない
            if (_sculptor == null)
            {
                return;
            }

            DrawOverview();
            DrawShapes();
            DrawBrush();
            DrawTools();
            DrawPreview();
            DrawFile();
        }

        private void Open(CustomPerfectSync custom)
        {
            // 開いていれば何もしない
            LoadedAvatar current = _session.Current;
            if (_sculptor != null || current == null)
            {
                return;
            }

            // アバターに付けて、顔のトラッキング・カメラを作成モード用にする
            _sculptor = current.Instance.AddComponent<PerfectSyncSculptor>();
            _sculptor.Initialize(custom, current.Instance.GetComponent<FaceTrackingDriver>(), _orbit, current.Animator);
            _status = null;
            _confirmExit = false;
            _mixMesh = 0;
            _mixNamesMesh = -1;
        }

        private void Close()
        {
            // 作った形状を反映して自分を外す（保存はしない）
            if (_sculptor != null)
            {
                _sculptor.Close();
            }

            _sculptor = null;
            _confirmExit = false;
        }

        private void DrawOverview()
        {
            GuiControls.BeginCard(Loc.T("Perfect sync shape editor", "パーフェクトシンクの形状作成", "퍼펙트 싱크 셰이프 만들기",
                "完美同步形状制作", "完美同步形狀製作"));
            GuiControls.Hint(Loc.T(
                "Pick an ARKit shape, then drag on the face to shape it. Right drag: rotate / Middle drag: pan / Wheel: zoom / [ ]: brush size / Ctrl+Z, Ctrl+Y: undo, redo. Face tracking is paused while editing.",
                "ARKit 名を選び、顔をドラッグして形を作ります。右ドラッグ: 回転 / 中ドラッグ: 移動 / ホイール: ズーム / [ ]: ブラシの大きさ / Ctrl+Z・Ctrl+Y: 元に戻す・やり直し。編集中は顔のトラッキングを止めます。",
                "ARKit 이름을 고르고 얼굴을 드래그해 모양을 만듭니다. 오른쪽 드래그: 회전 / 가운데 드래그: 이동 / 휠: 줌 / [ ]: 브러시 크기 / Ctrl+Z·Ctrl+Y: 실행 취소·다시 실행. 편집 중에는 얼굴 트래킹을 멈춥니다.",
                "选择 ARKit 名称，然后在脸上拖动来塑形。右键拖动：旋转 / 中键拖动：平移 / 滚轮：缩放 / [ ]：笔刷大小 / Ctrl+Z、Ctrl+Y：撤销、重做。编辑时会暂停面部追踪。",
                "選擇 ARKit 名稱，然後在臉上拖曳來塑形。右鍵拖曳：旋轉 / 中鍵拖曳：平移 / 滾輪：縮放 / [ ]：筆刷大小 / Ctrl+Z、Ctrl+Y：復原、重做。編輯時會暫停臉部追蹤。"));

            // 直近の操作の結果
            if (!string.IsNullOrEmpty(_status))
            {
                GuiControls.Status(_status, _statusOk);
            }

            DrawExit();
            GuiControls.EndCard();
        }

        private void DrawExit()
        {
            // 未保存なら 1 回目は確認を出す
            if (!_confirmExit)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(Loc.T("Face close-up", "顔のアップに戻す", "얼굴 클로즈업", "面部特写", "臉部特寫"),
                        GuiControls.Shrinkable))
                {
                    _sculptor.FrameFace();
                }

                if (GUILayout.Button(Loc.T("Exit", "終了", "종료", "退出", "結束"), GuiControls.Shrinkable))
                {
                    if (_sculptor.IsDirty)
                    {
                        _confirmExit = true;
                    }
                    else
                    {
                        Close();
                    }
                }

                GUILayout.EndHorizontal();
                return;
            }

            GuiControls.Status(Loc.T(
                "There are unsaved changes. Unsaved shapes stay only until the avatar is reloaded.",
                "保存していない変更があります。保存しない形状はアバターを読み込み直すまでの間だけ使えます。",
                "저장하지 않은 변경이 있습니다. 저장하지 않은 셰이프는 아바타를 다시 불러올 때까지만 사용할 수 있습니다.",
                "有未保存的更改。未保存的形状只在重新加载虚拟形象前有效。",
                "有未儲存的變更。未儲存的形狀只在重新載入虛擬形象前有效。"), false);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Save and exit", "保存して終了", "저장 후 종료", "保存并退出", "儲存並結束"),
                    GuiControls.Shrinkable))
            {
                // 保存できたときだけ閉じる
                if (Save())
                {
                    Close();
                }
                else
                {
                    _confirmExit = false;
                }
            }

            if (GuiControls.DangerButton(Loc.T("Exit without saving", "保存せずに終了", "저장하지 않고 종료", "不保存退出",
                    "不儲存結束")))
            {
                Close();
            }

            if (GUILayout.Button(Loc.T("Cancel", "キャンセル", "취소", "取消", "取消"), GuiControls.Shrinkable))
            {
                _confirmExit = false;
            }

            GUILayout.EndHorizontal();
        }

        private void DrawShapes()
        {
            GuiControls.BeginCard(Loc.T("ARKit shapes", "ARKit 名", "ARKit 이름", "ARKit 名称", "ARKit 名稱"));
            CustomPerfectSync custom = _sculptor.Custom;
            GuiControls.Hint(Loc.T(
                $"* = made ({_sculptor.ShapeCount}). Greyed out = the avatar already has it (used as is).",
                $"* = 作成済み（{_sculptor.ShapeCount} 個）。灰色 = アバターが元から持っている（そのまま使います）。",
                $"* = 만듦 ({_sculptor.ShapeCount}개). 회색 = 아바타가 원래 가지고 있음 (그대로 사용).",
                $"* = 已制作（{_sculptor.ShapeCount} 个）。灰色 = 虚拟形象原本就有（直接使用）。",
                $"* = 已製作（{_sculptor.ShapeCount} 個）。灰色 = 虛擬形象原本就有（直接使用）。"));

            // ARKit 名を列に並べ、押したものを編集対象にする（元から持っている名前は押せない）
            GUIStyle style = UiTheme.Current != null ? UiTheme.Current.OverflowButton : GUI.skin.button;
            _gridScroll = GUILayout.BeginScrollView(_gridScroll, GUILayout.Height(GridHeight));
            for (int row = 0; row * GridColumns < ShapeNames.Length; row++)
            {
                GUILayout.BeginHorizontal();
                for (int column = 0; column < GridColumns; column++)
                {
                    int shape = row * GridColumns + column;
                    if (shape >= ShapeNames.Length)
                    {
                        // 最後の行の空きを埋めて幅をそろえる
                        GUILayout.Label(string.Empty, GuiControls.Shrinkable, GUILayout.ExpandWidth(true));
                        continue;
                    }

                    bool previous = GUI.enabled;
                    GUI.enabled = previous && !custom.IsNative(shape);
                    string label = (_sculptor.HasShape(shape) ? "* " : string.Empty) + ShapeNames[shape];
                    bool selected = _sculptor.Shape == shape;
                    if (GUILayout.Toggle(selected, label, style, GuiControls.Shrinkable, GUILayout.ExpandWidth(true)) && !selected)
                    {
                        _sculptor.SelectShape(shape);
                    }

                    GUI.enabled = previous;
                }

                GUILayout.EndHorizontal();
            }

            GUILayout.EndScrollView();
            GuiControls.EndCard();
        }

        private void DrawBrush()
        {
            GuiControls.BeginCard(Loc.T("Brush", "ブラシ", "브러시", "笔刷", "筆刷"));

            // 元から持っている名前・弱めて表示中・試している間はブラシを使えない理由を出す
            if (_sculptor.Custom.IsNative(_sculptor.Shape))
            {
                GuiControls.Status(Loc.T("The avatar already has this shape.", "アバターが元から持っている形状です。",
                    "아바타가 원래 가지고 있는 셰이프입니다.", "虚拟形象原本就有此形状。", "虛擬形象原本就有此形狀。"), false);
            }
            else if (!_sculptor.CanSculpt)
            {
                GuiControls.Status(Loc.T("Brushes work when the preview strength is 1.00 and tracking test is off.",
                    "ブラシは確認の強さが 1.00 で、トラッキングで試すが OFF のときに使えます。",
                    "브러시는 미리보기 강도가 1.00이고 트래킹 테스트가 꺼져 있을 때 사용할 수 있습니다.",
                    "预览强度为 1.00 且关闭追踪测试时才能使用笔刷。",
                    "預覽強度為 1.00 且關閉追蹤測試時才能使用筆刷。"), false);
            }

            string[] brushes =
            {
                Loc.T("Grab", "つまむ", "잡기", "抓取", "抓取"),
                Loc.T("Inflate", "膨らませる", "부풀리기", "膨胀", "膨脹"),
                Loc.T("Deflate", "へこませる", "오목하게", "收缩", "收縮"),
                Loc.T("Smooth", "なめらか", "매끄럽게", "平滑", "平滑"),
                Loc.T("Erase (back to original)", "元に戻す（消す）", "원래대로 (지우기)", "还原（擦除）", "還原（擦除）"),
            };
            _sculptor.Brush = (SculptBrush)GuiControls.EnumSelector(Loc.T("Brush", "ブラシ", "브러시", "笔刷", "筆刷"),
                brushes, (int)_sculptor.Brush);

            // 半径は mm で表示する
            float radius = GuiControls.Slider(Loc.T("Size (mm)", "大きさ（mm）", "크기 (mm)", "大小（mm）", "大小（mm）"),
                _sculptor.Radius * MillimetersPerMeter, PerfectSyncSculptor.MinRadius * MillimetersPerMeter,
                PerfectSyncSculptor.MaxRadius * MillimetersPerMeter, "F0");
            _sculptor.Radius = radius / MillimetersPerMeter;

            // つまむ以外の強さ
            if (_sculptor.Brush != SculptBrush.Grab)
            {
                _sculptor.Strength = GuiControls.Slider(Loc.T("Strength", "強さ", "강도", "强度", "強度"),
                    _sculptor.Strength, 0.05f, 1f);
            }

            _sculptor.Symmetry = GUILayout.Toggle(_sculptor.Symmetry, Loc.T("Symmetry (left and right together)",
                "左右対称（左右を一緒に編集）", "좌우 대칭 (좌우 함께 편집)", "左右对称（同时编辑左右）", "左右對稱（同時編輯左右）"));

            // 元に戻す・やり直し
            GUILayout.BeginHorizontal();
            bool previous = GUI.enabled;
            GUI.enabled = previous && _sculptor.CanUndo;
            if (GUILayout.Button(Loc.T("Undo", "元に戻す", "실행 취소", "撤销", "復原"), GuiControls.Shrinkable))
            {
                _sculptor.Undo();
            }

            GUI.enabled = previous && _sculptor.CanRedo;
            if (GUILayout.Button(Loc.T("Redo", "やり直し", "다시 실행", "重做", "重做"), GuiControls.Shrinkable))
            {
                _sculptor.Redo();
            }

            GUI.enabled = previous;
            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        private void DrawTools()
        {
            GuiControls.BeginCard(Loc.T("Shape tools", "形状の操作", "셰이프 도구", "形状工具", "形狀工具"));
            bool previous = GUI.enabled;
            GUI.enabled = previous && !_sculptor.TrackingPreview && !_sculptor.Custom.IsNative(_sculptor.Shape);

            DrawMirrorCopy();
            DrawMix();

            // 編集中の形状を消す
            if (GuiControls.DangerButton(Loc.T($"Clear {ShapeNames[_sculptor.Shape]}", $"{ShapeNames[_sculptor.Shape]} を消す",
                    $"{ShapeNames[_sculptor.Shape]} 지우기", $"清除 {ShapeNames[_sculptor.Shape]}",
                    $"清除 {ShapeNames[_sculptor.Shape]}")))
            {
                _sculptor.ClearShape();
            }

            GUI.enabled = previous;
            GuiControls.EndCard();
        }

        private void DrawMirrorCopy()
        {
            // 左右の組になる名前があるときだけ出す
            int opposite = PerfectSyncSculptor.OppositeShape(_sculptor.Shape);
            if (opposite < 0)
            {
                return;
            }

            // 写し先が元から持っている名前なら押せない
            bool previous = GUI.enabled;
            GUI.enabled = previous && !_sculptor.Custom.IsNative(opposite);
            string target = ShapeNames[opposite];
            if (GUILayout.Button(Loc.T($"Copy mirrored to {target}", $"左右反転して {target} へ写す",
                    $"좌우 반전해 {target}에 복사", $"左右翻转复制到 {target}", $"左右翻轉複製到 {target}"),
                    GuiControls.Shrinkable))
            {
                bool copied = _sculptor.MirrorCopy();
                SetStatus(copied
                    ? Loc.T($"Copied to {target}.", $"{target} へ写しました。", $"{target}에 복사했습니다.",
                        $"已复制到 {target}。", $"已複製到 {target}。")
                    : Loc.T("Nothing to copy.", "写す形状がありません。", "복사할 셰이프가 없습니다.", "没有可复制的形状。",
                        "沒有可複製的形狀。"), copied);
            }

            GUI.enabled = previous;
        }

        private void DrawMix()
        {
            GuiControls.SubHeading(Loc.T("Add an existing blend shape", "既存の BlendShape を足す", "기존 BlendShape 더하기",
                "叠加现有 BlendShape", "疊加現有 BlendShape"));
            GuiControls.Hint(Loc.T(
                "Adds a blend shape the avatar already has (e.g. a mouth or eye shape) to the selected shape as a starting point.",
                "アバターが元から持っている BlendShape（口・目の形など）を、選んでいる形状に足して下地にします。",
                "아바타가 원래 가진 BlendShape (입·눈 모양 등)를 선택한 셰이프에 더해 바탕으로 씁니다.",
                "将虚拟形象原有的 BlendShape（嘴型、眼型等）叠加到所选形状上作为基础。",
                "將虛擬形象原有的 BlendShape（嘴型、眼型等）疊加到所選形狀上作為基礎。"));

            // 対象のメッシュ（複数あるときだけ選ばせる）
            IReadOnlyList<EditableFaceMesh> meshes = _sculptor.Custom.Meshes;
            if (meshes.Count > 1)
            {
                var paths = new List<string>(meshes.Count);
                foreach (EditableFaceMesh mesh in meshes)
                {
                    paths.Add(mesh.Path);
                }

                _mixMesh = GuiControls.EnumSelector(Loc.T("Mesh", "メッシュ", "메시", "网格", "網格"), paths.ToArray(),
                    Mathf.Clamp(_mixMesh, 0, meshes.Count - 1));
            }

            _mixMesh = Mathf.Clamp(_mixMesh, 0, meshes.Count - 1);
            string[] names = MixNames(meshes[_mixMesh]);
            _mixSearch = GuiControls.TextField(Loc.T("Search", "検索", "검색", "搜索", "搜尋"), _mixSearch, 64);
            _mixWeight = GuiControls.Slider(Loc.T("Amount (%)", "割合（%）", "비율 (%)", "比例（%）", "比例（%）"),
                _mixWeight * 100f, 0f, 100f, "F0") / 100f;

            // 名前で絞り込んだ候補を並べ、押したものを足す
            _mixScroll = GUILayout.BeginScrollView(_mixScroll, GUILayout.Height(MixListHeight));
            int shown = 0;
            for (int i = 0; i < names.Length && shown < MaxMixResults; i++)
            {
                if (_mixSearch.Length > 0 && names[i].IndexOf(_mixSearch, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                shown++;
                if (GUILayout.Button(names[i], GuiControls.Shrinkable))
                {
                    bool added = _sculptor.MixExisting(_mixMesh, i, _mixWeight);
                    SetStatus(added
                        ? Loc.T($"Added {names[i]}.", $"{names[i]} を足しました。", $"{names[i]}을(를) 더했습니다.",
                            $"已叠加 {names[i]}。", $"已疊加 {names[i]}。")
                        : Loc.T("Could not add it.", "足せませんでした。", "더할 수 없습니다.", "无法叠加。", "無法疊加。"), added);
                }
            }

            GUILayout.EndScrollView();
        }

        private string[] MixNames(EditableFaceMesh mesh)
        {
            // メッシュを替えたときだけ BlendShape 名を読み直す
            if (_mixNames == null || _mixNamesMesh != _mixMesh)
            {
                _mixNames = new string[mesh.OriginalShapeCount];
                for (int i = 0; i < _mixNames.Length; i++)
                {
                    _mixNames[i] = mesh.Original.GetBlendShapeName(i);
                }

                _mixNamesMesh = _mixMesh;
            }

            return _mixNames;
        }

        private void DrawPreview()
        {
            GuiControls.BeginCard(Loc.T("Check", "確認", "확인", "确认", "確認"));

            // 選んでいる形状を弱めて表示する（ブラシは 1.00 のときだけ）
            bool previous = GUI.enabled;
            GUI.enabled = previous && !_sculptor.TrackingPreview;
            float weight = GuiControls.Slider(Loc.T("Preview strength", "確認の強さ", "미리보기 강도", "预览强度", "預覽強度"),
                _sculptor.PreviewWeight, 0f, 1f);
            if (!Mathf.Approximately(weight, _sculptor.PreviewWeight))
            {
                _sculptor.SetPreviewWeight(weight);
            }

            GUI.enabled = previous;

            // 顔のトラッキングで試す（パーフェクトシンクが有効なときだけ動く）
            bool tracking = GUILayout.Toggle(_sculptor.TrackingPreview, Loc.T("Test with face tracking",
                "顔のトラッキングで試す", "얼굴 트래킹으로 테스트", "用面部追踪测试", "用臉部追蹤測試"));
            if (tracking != _sculptor.TrackingPreview)
            {
                _sculptor.SetTrackingPreview(tracking);
            }

            if (_sculptor.TrackingPreview)
            {
                GuiControls.Hint(Loc.T(
                    $"Needs Perfect sync ON in the Tracking tab. With fewer than {PerfectSyncBlendShapes.MinMatchedShapes} shapes, set \"Enable when\" to \"Any shape\".",
                    $"トラッキングタブのパーフェクトシンクを ON にしてください。形状が {PerfectSyncBlendShapes.MinMatchedShapes} 種類未満なら「有効にする条件」を「1 種類でも」にします。",
                    $"트래킹 탭의 퍼펙트 싱크를 켜 주세요. 셰이프가 {PerfectSyncBlendShapes.MinMatchedShapes}종 미만이면 \"활성화 조건\"을 \"1종이라도\"로 설정합니다.",
                    $"请在追踪标签中开启完美同步。形状少于 {PerfectSyncBlendShapes.MinMatchedShapes} 种时，将“启用条件”设为“有 1 种即可”。",
                    $"請在追蹤分頁中開啟完美同步。形狀少於 {PerfectSyncBlendShapes.MinMatchedShapes} 種時，將「啟用條件」設為「有 1 種即可」。"));
            }

            GuiControls.EndCard();
        }

        private void DrawFile()
        {
            GuiControls.BeginCard(Loc.T("Save", "保存", "저장", "保存", "儲存"));
            GuiControls.Hint(Loc.T(
                "Saves the shapes into the loaded .vrcaster. The first save keeps the original file as .bak. Older VRCast versions open the file as before (without the shapes).",
                "作った形状を読み込んだ .vrcaster に保存します。初回は元のファイルを .bak として残します。古い VRCast でも今までどおり開けます（形状は使われません）。",
                "만든 셰이프를 불러온 .vrcaster에 저장합니다. 처음 저장할 때 원래 파일을 .bak으로 남깁니다. 이전 VRCast에서도 그대로 열립니다 (셰이프는 사용되지 않음).",
                "将制作的形状保存到已加载的 .vrcaster 中。首次保存时会把原文件保留为 .bak。旧版 VRCast 也能照常打开（不使用这些形状）。",
                "將製作的形狀儲存到已載入的 .vrcaster 中。首次儲存時會把原檔案保留為 .bak。舊版 VRCast 也能照常開啟（不使用這些形狀）。"));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("Save to .vrcaster", ".vrcaster に保存", ".vrcaster에 저장", "保存到 .vrcaster",
                    "儲存到 .vrcaster"), GuiControls.Shrinkable))
            {
                Save();
            }

            if (GUILayout.Button(Loc.T("Import from .vrcaster...", ".vrcaster から読み込む...", ".vrcaster에서 가져오기...",
                    "从 .vrcaster 导入...", "從 .vrcaster 匯入..."), GuiControls.Shrinkable))
            {
                Import();
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();
        }

        private bool Save()
        {
            // 失敗しても作成モードは続ける（理由を出す）
            try
            {
                string backup = _sculptor.Save();
                SetStatus(backup != null
                    ? Loc.T($"Saved. The original was kept as {Path.GetFileName(backup)}.",
                        $"保存しました。元のファイルは {Path.GetFileName(backup)} として残しました。",
                        $"저장했습니다. 원래 파일은 {Path.GetFileName(backup)}(으)로 남겼습니다.",
                        $"已保存。原文件保留为 {Path.GetFileName(backup)}。",
                        $"已儲存。原檔案保留為 {Path.GetFileName(backup)}。")
                    : Loc.T("Saved.", "保存しました。", "저장했습니다.", "已保存。", "已儲存。"), true);
                return true;
            }
            catch (Exception exception)
            {
                VRCastLog.Error(LogCategory, $"Failed to save perfect sync shapes: {exception.Message}");
                SetStatus(Loc.T("Could not save: ", "保存できませんでした: ", "저장하지 못했습니다: ", "无法保存：", "無法儲存：")
                    + exception.Message, false);
                return false;
            }
        }

        private void Import()
        {
            // 読み込む .vrcaster を選ぶ（キャンセルなら何もしない）
            string initial = _sculptor.Custom.SourcePath;
            string path = FileDialog.OpenFile(
                Loc.T("Import shapes", "形状を読み込む", "셰이프 가져오기", "导入形状", "匯入形狀"),
                Loc.T("VRCast avatar", "VRCast アバター", "VRCast 아바타", "VRCast 虚拟形象", "VRCast 虛擬形象"),
                AvatarPackageLayout.Extension, initial);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // 同じメッシュの形状だけを当てる
            try
            {
                int applied = _sculptor.Import(AvatarPackageReader.ReadPerfectSyncOnly(path));
                SetStatus(applied > 0
                    ? Loc.T($"Imported {applied} shapes.", $"{applied} 個の形状を読み込みました。", $"셰이프 {applied}개를 가져왔습니다.",
                        $"已导入 {applied} 个形状。", $"已匯入 {applied} 個形狀。")
                    : Loc.T("No shapes matched this avatar's meshes.", "このアバターのメッシュに合う形状がありませんでした。",
                        "이 아바타의 메시에 맞는 셰이프가 없습니다.", "没有与此虚拟形象网格匹配的形状。",
                        "沒有與此虛擬形象網格相符的形狀。"), applied > 0);
            }
            catch (Exception exception)
            {
                VRCastLog.Error(LogCategory, $"Failed to import perfect sync shapes: {exception.Message}");
                SetStatus(Loc.T("Could not import: ", "読み込めませんでした: ", "가져오지 못했습니다: ", "无法导入：", "無法匯入：")
                    + exception.Message, false);
            }
        }

        private void SetStatus(string text, bool ok)
        {
            _status = text;
            _statusOk = ok;
        }
    }
}
