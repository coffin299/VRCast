using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using VRCast.PerfectSync;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンク化の編集画面の描画（右側のパネルと、顔に重ねる線・目印）。
    /// 重い処理（形状の生成・保存・閉じる）はボタンを押したフレームの Update で行う（描画の途中で状態を変えないため）。
    /// </summary>
    public sealed partial class PerfectSyncEditor
    {
        // IMGUI ウィンドウ ID・パネルの大きさ・画面端からの余白
        private const int WindowId = 0x5650;
        private const float PanelWidth = 440f;
        private const float PanelMargin = 10f;
        private const float MinPanelHeight = 320f;

        // 「実際のトラッキングで試す」間に左側へ出す、口・頬の強調の調整パネル（ID・幅）
        private const int TuneWindowId = 0x5651;
        private const float TunePanelWidth = 380f;

        // 案内図の大きさと点の大きさ
        private const float DiagramSize = 150f;
        private const float DiagramDot = 6f;
        private const float DiagramSelectedDot = 10f;

        // 顔に重ねる目印の大きさ（画面の画素）
        private const float PointSize = 8f;
        private const float SelectedPointSize = 12f;

        // 目印の名前の札の大きさ（GUI の座標）
        private const float TagWidth = 240f;
        private const float TagHeight = 24f;

        // 形状の一覧の列幅と、割り当ての候補を一度に出す数
        private const float ShapeNameWidth = 150f;
        private const float StateWidth = 80f;
        private const float StrengthValueWidth = 44f;
        private const int MaxSearchResults = 30;

        // 形状の強さと、試している形状の値の入力途中
        private string[] _strengthInputs = Array.Empty<string>();
        private string _previewInput = string.Empty;

        // 線と目印の色（選択中は黄色、自動で決めた右側は水色）
        private static readonly Color WireColor = new Color(0.3f, 0.8f, 1f, 0.35f);
        private static readonly Color PlacedColor = new Color(0.3f, 1f, 0.4f, 1f);
        private static readonly Color MirroredColor = new Color(0.4f, 0.8f, 1f, 1f);
        private static readonly Color SelectedColor = new Color(1f, 0.9f, 0.2f, 1f);
        private static readonly Color PendingColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        // 次の Update で行う処理（ボタンで決めたもの）
        private Action _deferred;

        // 調整パネルの範囲（出していなければ空。内容に合わせて高さが決まる）
        private Rect _tuneRect;

        // パネルのスクロール位置・割り当ての候補の検索文字列・重ねて描く目印（使い回す）
        private Vector2 _scroll;
        private string _search = string.Empty;
        private readonly List<(Vector3 World, Color Color, float Size)> _points =
            new List<(Vector3, Color, float)>();

        /// <summary>
        /// 顔に重ねる線・目印と、右側のパネルを描く（MainPanel の OnGUI から、スキンと倍率を適用した状態で呼ぶ）。
        /// </summary>
        public void OnGUI(float uiScale)
        {
            if (!IsOpen)
            {
                return;
            }

            DrawOverlay(uiScale);

            // 画面の右端に、画面の高さいっぱいのパネルを置く
            float width = Screen.width / uiScale;
            float height = Screen.height / uiScale;
            PanelRect = new Rect(width - PanelWidth - PanelMargin, PanelMargin, PanelWidth,
                Mathf.Max(MinPanelHeight, height - PanelMargin * 2f));
            GUILayout.Window(WindowId, PanelRect, DrawPanel, GUIContent.none, GUI.skin.window,
                GUILayout.Width(PanelRect.width), GUILayout.Height(PanelRect.height));

            // トラッキングで試している間だけ、左上に口・頬の強調の調整パネルを出す（高さは内容に合わせる）
            if (!_trackingTest || _settings == null)
            {
                _tuneRect = Rect.zero;
                return;
            }

            _tuneRect = GUILayout.Window(TuneWindowId, new Rect(PanelMargin, PanelMargin, TunePanelWidth, 0f),
                DrawTunePanel, GUIContent.none, GUI.skin.window, GUILayout.Width(TunePanelWidth));
        }

        /// <summary>
        /// GUI の座標 position が編集画面のパネル（右側・調整パネル）の上なら true（その間はカメラ操作・目印の操作をしない）。
        /// </summary>
        public bool IsOverPanel(Vector2 position)
        {
            return PanelRect.Contains(position) || _tuneRect.Contains(position);
        }

        private void DrawTunePanel(int id)
        {
            UiTheme theme = UiTheme.Current;

            // 見出しと、口・頬の強調の設定（トラッキングタブと同じもの。変えるとすぐ動きに反映される）
            GUILayout.Label(Loc.T("Mouth and cheek tuning", "口・頬の調整", "입·볼 조정", "嘴部和脸颊调整", "嘴部和臉頰調整"),
                theme != null ? theme.SectionTitle : GUI.skin.label);
            PerfectSyncBoostControls.Draw(_settings, _faceDriver);

            // 保存していない変更があるか（あれば保存・元に戻すを押せる。横に並べると見切れるので縦に積む）
            bool changed = !_boostSaved.Matches(_settings);
            GUI.enabled = changed;
            if (GUILayout.Button(Loc.T("Save settings", "設定を保存", "설정 저장", "保存设置", "儲存設定"),
                    theme != null ? theme.AccentButton : GUI.skin.button))
            {
                SaveBoostChanges();
            }

            if (GUILayout.Button(Loc.T("Revert", "元に戻す", "되돌리기", "还原", "還原")))
            {
                _boostSaved.Restore(_settings);
            }

            GUI.enabled = true;

            // 保存の状態（メモリ上はトラッキングタブと常に同じ。ファイルへ残すときだけ保存）
            if (changed)
            {
                GuiControls.Warning(Loc.T(
                    "Not saved to file yet. The Tracking tab already uses these values. Revert restores the values from when you started testing.",
                    "ファイルにはまだ保存していません。トラッキングタブにはすでに反映されています。「元に戻す」で試し始めたときの値に戻します。",
                    "파일에는 아직 저장하지 않았습니다. 트래킹 탭에는 이미 반영되어 있습니다. '되돌리기'로 확인을 시작했을 때 값으로 돌아갑니다.",
                    "尚未写入文件。追踪分页已经在用这些值。点“还原”会回到开始测试时的值。",
                    "尚未寫入檔案。追蹤分頁已經在用這些值。點「還原」會回到開始測試時的值。"));
            }
            else
            {
                GuiControls.Status(Loc.T("Saved (same settings as the Tracking tab)",
                    "保存済み（トラッキングタブと同じ設定です）", "저장됨 (트래킹 탭과 같은 설정)",
                    "已保存（与追踪分页的设置相同）", "已儲存（與追蹤分頁的設定相同）"), true);
            }
        }

        /// <summary>
        /// ボタンで決めた処理を行う（Update の最初に呼ぶ）。閉じたら false。
        /// </summary>
        private bool RunDeferred()
        {
            Action deferred = _deferred;
            _deferred = null;
            deferred?.Invoke();
            return IsOpen;
        }

        private void DrawOverlay(float uiScale)
        {
            // 描くのは Repaint のときだけ（トラッキングで試している間は顔を見やすいよう描かない）
            if (Event.current.type != EventType.Repaint || _trackingTest || _geometry == null || _camera == null
                || _world.Length != _geometry.Vertices.Length)
            {
                return;
            }

            // 顔メッシュの線
            _overlay.DrawWireframe(_camera, _world, _geometry.Triangles, _region, _regionVertices, _faceCenter, WireColor);

            // 打った目印（選択中は大きく黄色、自動で決めた右側は水色）
            _points.Clear();
            foreach (KeyValuePair<LandmarkId, PerfectSyncLandmark> pair in _landmarks)
            {
                if (!_geometry.TryGetPoint(pair.Value, out Vector3 world, _world))
                {
                    continue;
                }

                bool selected = _selected == pair.Key;
                Color color = selected ? SelectedColor : _autoMirrored.Contains(pair.Key) ? MirroredColor : PlacedColor;
                _points.Add((world, color, selected ? SelectedPointSize : PointSize));
            }

            _overlay.DrawPoints(_camera, _points);

            // 選択中の目印の横に名前の札を出す
            if (_selected.HasValue && _landmarks.TryGetValue(_selected.Value, out PerfectSyncLandmark current)
                && _geometry.TryGetPoint(current, out Vector3 selectedWorld, _world)
                && PerfectSyncOverlay.ToGui(_camera, selectedWorld, uiScale, out Vector2 gui))
            {
                GUI.Box(new Rect(gui.x + SelectedPointSize, gui.y - TagHeight * 0.5f, TagWidth, TagHeight),
                    PerfectSyncLabels.Landmark(_selected.Value));
            }
        }

        private void DrawPanel(int id)
        {
            UiTheme theme = UiTheme.Current;

            // 見出しと、編集中の注意
            GUILayout.Label(Loc.T("Perfect sync setup", "パーフェクトシンク化", "퍼펙트 싱크화", "完美同步化", "完美同步化"),
                theme != null ? theme.SectionTitle : GUI.skin.label);
            GUILayout.Label(_avatar.Name, theme != null ? theme.Hint : GUI.skin.label);
            GuiControls.Warning(Loc.T(
                "While editing, the output shows this close-up, and physics, idle motion, tracking, blinking and lip sync " +
                "are paused. The original file is not changed.",
                "編集中は出力にもこのアップが映り、揺れもの・待機モーション・トラッキング・まばたき・口パクは止まります。" +
                "元のファイルは変更しません。",
                "편집 중에는 출력에도 이 클로즈업이 나오고, 흔들림·대기 모션·트래킹·눈 깜빡임·립싱크는 멈춥니다. " +
                "원본 파일은 변경하지 않습니다.",
                "编辑期间输出画面也会显示此特写，物理摆动、待机动作、追踪、眨眼和口型同步会暂停。不会修改原文件。",
                "編輯期間輸出畫面也會顯示此特寫，物理擺動、待機動作、追蹤、眨眼和口型同步會暫停。不會修改原檔案。"));

            // 段階の切替（目印 / 形状）
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_step == Step.Points,
                    Loc.T("1. Points", "1. 目印", "1. 표시", "1. 标记", "1. 標記"), GUI.skin.button) && _step != Step.Points)
            {
                // 形状の段階の検索欄から入力の焦点を外し、Delete・Ctrl+Z を目印の操作に使えるようにする
                // （目印を打てるよう、トラッキングで試すのはやめて顔を止める）
                _step = Step.Points;
                GUIUtility.keyboardControl = 0;
                SetTrackingTest(false);
            }

            if (GUILayout.Toggle(_step == Step.Shapes,
                    Loc.T("2. Shapes", "2. 形状", "2. 형상", "2. 形状", "2. 形狀"), GUI.skin.button) && _step != Step.Shapes)
            {
                _step = Step.Shapes;
            }

            GUILayout.EndHorizontal();

            // 段階ごとの内容（縦にスクロール）
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            if (_step == Step.Points)
            {
                DrawPointsStep(theme);
            }
            else
            {
                DrawShapesStep(theme);
            }

            GUILayout.EndScrollView();

            DrawFooter(theme);
        }

        private void DrawPointsStep(UiTheme theme)
        {
            GuiControls.BeginCard(Loc.T("Place points on the face", "顔に目印を打つ", "얼굴에 표시 찍기", "在脸上放置标记", "在臉上放置標記"));
            GuiControls.Hint(Loc.T(
                "Left-click the face to place the highlighted point, and drag a point to move it. " +
                "The wheel zooms and middle-drag pans (right-drag rotates when the view is not kept facing the face). " +
                "Ctrl+Z undoes, and Delete removes the selected point " +
                "(or the one you just placed). " +
                "Left and right are the avatar's own (its left is on the right of the screen from the front).",
                "顔を左クリックすると、選択中の目印を打てます。目印はドラッグで動かせます。" +
                "ホイールで拡大、中ドラッグで移動（正面の固定を外すと右ドラッグで回転）。Ctrl+Z で元に戻し、Delete キーで選択中の目印" +
                "（打っていなければ直前に打った目印）を消せます。" +
                "左右はアバターから見た左右です（正面から見ると画面の右がアバターの左）。",
                "얼굴을 왼쪽 클릭하면 선택한 표시를 찍습니다. 표시는 드래그로 옮길 수 있습니다. " +
                "휠로 확대, 가운데 드래그로 이동(정면 고정을 끄면 오른쪽 드래그로 회전). Ctrl+Z로 되돌리고, Delete 키로 선택한 표시" +
                "(찍지 않았다면 방금 찍은 표시)를 지웁니다. " +
                "좌우는 아바타 기준입니다(정면에서 보면 화면 오른쪽이 아바타의 왼쪽).",
                "左键点击脸部即可放置当前选中的标记，拖动可移动标记。" +
                "滚轮缩放，中键拖动平移（取消正面固定后可右键拖动旋转）。Ctrl+Z 撤销，Delete 键删除选中的标记（未放置时删除刚放置的标记）。" +
                "左右以虚拟形象自身为准（从正面看，屏幕右侧是形象的左侧）。",
                "左鍵點擊臉部即可放置目前選取的標記，拖曳可移動標記。" +
                "滾輪縮放，中鍵拖曳平移（取消正面固定後可右鍵拖曳旋轉）。Ctrl+Z 復原，Delete 鍵刪除選取的標記（未放置時刪除剛放置的標記）。" +
                "左右以虛擬形象自身為準（從正面看，螢幕右側是形象的左側）。"));

            // 視点を顔の正面に固定（ON にしたら正面へ戻す。OFF なら右ドラッグで回せる）
            bool lockView = GUILayout.Toggle(_lockView, Loc.T(
                "Keep the view facing the face (zoom and pan still work)", "視点を顔の正面に固定（拡大・移動はできます）",
                "시점을 얼굴 정면에 고정 (확대·이동은 가능)", "将视角固定在脸部正面（仍可缩放·平移）",
                "將視角固定在臉部正面（仍可縮放·平移）"));
            if (lockView != _lockView)
            {
                _lockView = lockView;
                if (lockView)
                {
                    FocusFace();
                }
            }

            // 顔メッシュ（替えたら目印は打ち直し）
            int face = GuiControls.Selector(Loc.T("Face mesh", "顔メッシュ", "얼굴 메시", "面部网格", "臉部網格"),
                _rendererLabels, _faceIndex, null);
            if (face != _faceIndex && face >= 0)
            {
                SelectFace(face);
            }

            // 細かさ（少ない段階で打たない目印は推定する）
            var detail = (LandmarkDetail)GuiControls.EnumSelector(
                Loc.T("Detail", "細かさ", "세밀도", "精细度", "精細度"), PerfectSyncLabels.Details(), (int)_detail);
            if (detail != _detail)
            {
                _detail = detail;
                _dirty = true;
                _selected = NextUnplaced() ?? _selected;
            }

            // 左右対称（右側は左側から自動で決める）
            bool mirror = GUILayout.Toggle(_mirror, Loc.T(
                "Mirror the left points to the right", "左側の目印を右側にも反映（左右対称）",
                "왼쪽 표시를 오른쪽에도 반영 (좌우 대칭)", "将左侧标记镜像到右侧（左右对称）", "將左側標記鏡像到右側（左右對稱）"));
            if (mirror != _mirror)
            {
                // 切り替え前を履歴に積む（目印が増減するため元に戻せるようにする）
                PushUndo(Snapshot());

                // ON にしたら、左側を打ってある右側を自動に戻して決め直す（元に戻すで戻せる）。
                // OFF にしたら自動で決めた右側は手で打ったものとして残す
                _mirror = mirror;
                _dirty = true;
                if (mirror)
                {
                    MarkRightsAutomatic();
                    Remirror(null);
                }
                else
                {
                    _autoMirrored.Clear();
                }
            }

            // 歯・舌のメッシュも顎と一緒に動かす（別のメッシュが見つからなければ選べない）
            GUIStyle hint = theme != null ? theme.Hint : GUI.skin.label;
            GUI.enabled = _mouthPartCount > 0;
            bool mouth = GUILayout.Toggle(_includeMouthParts && _mouthPartCount > 0, Loc.T(
                "Also move teeth and tongue meshes with the jaw", "歯・舌のメッシュも顎と一緒に動かす",
                "치아·혀 메시도 턱과 함께 움직이기", "牙齿·舌头网格也随下巴一起移动", "牙齒·舌頭網格也隨下巴一起移動"));
            GUI.enabled = true;
            if (_mouthPartCount == 0)
            {
                GUILayout.Label(Loc.T(
                    "Separate teeth/tongue meshes not found (if they are part of the face mesh, they move with it).",
                    "別の歯・舌のメッシュは見つかりません（顔メッシュに含まれていれば、顔と一緒に動きます）。",
                    "별도의 치아·혀 메시를 찾을 수 없습니다(얼굴 메시에 포함되어 있으면 함께 움직입니다).",
                    "未找到单独的牙齿·舌头网格（若包含在面部网格中，会随面部一起移动）。",
                    "找不到單獨的牙齒·舌頭網格（若包含在臉部網格中，會隨臉部一起移動）。"), hint);
            }
            else if (mouth != _includeMouthParts)
            {
                _includeMouthParts = mouth;
                _dirty = true;
            }

            // アバターに無い部位（その部位の目印は打たず、形状は既定で「なし」にする）
            GUILayout.Label(Loc.T("Parts this avatar doesn't have", "このアバターに無い部位", "이 아바타에 없는 부위",
                "此形象没有的部位", "此形象沒有的部位"), hint);
            GUIStyle wrap = WrapButton(theme);
            GUILayout.BeginHorizontal();
            foreach (FaceParts part in FacePartRules.Selectable)
            {
                bool absent = (_absent & part) != 0;
                if (GUILayout.Toggle(absent, PerfectSyncLabels.FacePart(part), wrap, GuiControls.Shrinkable) != absent)
                {
                    SetPartAbsent(part, !absent);
                }
            }

            GUILayout.EndHorizontal();

            // 無い部位を選んでいる間は、思いどおりに動かないことがあると知らせる
            if (_absent != FaceParts.None)
            {
                GuiControls.Warning(PerfectSyncLabels.MissingPartsCaution());
            }

            // 自動で置く（打っていない目印だけ。画像認識の結果を待つ間は押せない）
            DrawAutoPlace(hint, wrap);

            // 進み具合と案内図
            int placed = PlacedCount(out int total);
            GuiControls.Status(Loc.T($"Placed {placed}/{total}", $"打った目印 {placed}/{total}", $"찍은 표시 {placed}/{total}",
                $"已放置 {placed}/{total}", $"已放置 {placed}/{total}"), placed == total);
            DrawDiagram();

            // 元に戻す・目印を消す（間違えて打ったときにすぐ押せるよう、長い一覧より上に置く）
            // 消すボタンには目印の名前が入って長くなるので、1 行ずつ並べて折り返す
            GUI.enabled = _undo.Count > 0;
            if (GUILayout.Button(Loc.T("Undo (Ctrl+Z)", "元に戻す（Ctrl+Z）", "실행 취소 (Ctrl+Z)", "撤销（Ctrl+Z）", "復原（Ctrl+Z）"),
                    wrap))
            {
                Undo();
            }

            LandmarkId? target = DeleteTarget();
            GUI.enabled = target.HasValue;
            string removeLabel = Loc.T("Remove (Delete)", "消す（Delete）", "지우기 (Delete)", "删除（Delete）", "刪除（Delete）");
            if (GUILayout.Button(target.HasValue ? $"{removeLabel}: {PerfectSyncLabels.Landmark(target.Value)}" : removeLabel,
                    wrap))
            {
                RemoveDeleteTarget();
            }

            GUI.enabled = true;

            // 目印の一覧（押すと選択）
            foreach (LandmarkId landmark in ActiveLandmarks())
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(_selected == landmark, PerfectSyncLabels.Landmark(landmark), wrap,
                        GuiControls.Shrinkable) && _selected != landmark)
                {
                    _selected = landmark;
                }

                GUILayout.Label(LandmarkState(landmark), hint, GUILayout.Width(StateWidth));
                GUILayout.EndHorizontal();
            }

            // 全部消す（元に戻せる）・顔へ寄り直す
            GUILayout.BeginHorizontal();
            GUI.enabled = _landmarks.Count > 0;
            if (GuiControls.DangerButton(Loc.T("Clear all", "全部消す", "모두 지우기", "全部清除", "全部清除"), GuiControls.Shrinkable))
            {
                ClearAllLandmarks();
            }

            GUI.enabled = true;
            if (GUILayout.Button(Loc.T("Face the camera", "顔を正面に", "얼굴 정면으로", "正对脸部", "正對臉部"), GuiControls.Shrinkable))
            {
                FocusFace();
            }

            GUILayout.EndHorizontal();
            GuiControls.EndCard();

            // 作り方（これまでの式 / 形状の転写）を選んでから生成する
            DrawMethodSelector();

            // 形状を作って試す（最小の目印が揃っていなければ押せない）
            GUI.enabled = HasRequiredLandmarks();
            if (GUILayout.Button(Loc.T("Generate and preview", "生成して試す", "생성해서 확인", "生成并预览", "生成並預覽"),
                    theme != null ? theme.AccentButton : GUI.skin.button))
            {
                _deferred = GenerateAndShow;
            }

            GUI.enabled = true;
            List<LandmarkId> missing = MissingRequired();
            if (missing.Count > 0)
            {
                GuiControls.Hint(Loc.T("To generate, also place: ", "生成には次の目印も必要です: ", "생성하려면 다음 표시도 필요합니다: ",
                    "生成还需要以下标记: ", "生成還需要以下標記: ") + string.Join(", ", missing.ConvertAll(PerfectSyncLabels.Landmark)));
            }
        }

        private void DrawAutoPlace(GUIStyle hint, GUIStyle wrap)
        {
            GUILayout.Label(Loc.T(
                "Place automatically (only points not placed yet; check and drag any that are off):",
                "自動で置く（まだ打っていない目印だけ。ずれた目印はドラッグで直してください）:",
                "자동으로 찍기(아직 찍지 않은 표시만. 어긋난 표시는 드래그로 고치세요):",
                "自动放置（仅限尚未放置的标记，偏移的请拖动修正）:",
                "自動放置（僅限尚未放置的標記，偏移的請拖曳修正）:"), hint);
            GUI.enabled = !IsDetecting;

            // 併用（おすすめ。BlendShape から打った点に合わせて、足りない点を MediaPipe で補う。正面に向けてから押す）
            UiTheme theme = UiTheme.Current;
            if (GUILayout.Button(Loc.T("Blend shapes + image recognition (fills the rest with MediaPipe, face the camera first)",
                    "BlendShape + 画像認識（足りない点を MediaPipe で補う。顔を正面に向けてから）",
                    "BlendShape + 이미지 인식 (부족한 점을 MediaPipe로 보완, 얼굴을 정면으로 한 뒤)",
                    "BlendShape + 图像识别（用 MediaPipe 补足缺少的点，请先正对脸部）",
                    "BlendShape + 影像辨識（用 MediaPipe 補足缺少的點，請先正對臉部）"),
                    theme != null ? theme.RecommendedWrapButton : wrap))
            {
                _deferred = StartHybridDetection;
            }

            // BlendShape から（まばたき・口・眉で動く頂点から求める。重いので次のフレームで実行）
            if (GUILayout.Button(Loc.T("From blend shapes (blink, mouth, brows)", "BlendShape から（まばたき・口・眉）",
                    "BlendShape에서 (눈 깜빡임·입·눈썹)", "从 BlendShape（眨眼·嘴·眉毛）", "從 BlendShape（眨眼·嘴·眉毛）"), wrap))
            {
                _deferred = AutoPlaceFromShapes;
            }

            // 画像認識（今の視点の顔を MediaPipe で認識する。正面に向けてから押す）
            if (GUILayout.Button(Loc.T("Image recognition (MediaPipe, face the camera first)",
                    "画像認識（MediaPipe。顔を正面に向けてから）", "이미지 인식 (MediaPipe, 얼굴을 정면으로 한 뒤)",
                    "图像识别（MediaPipe，请先正对脸部）", "影像辨識（MediaPipe，請先正對臉部）"), wrap))
            {
                _deferred = StartFaceDetection;
            }

            GUI.enabled = true;
        }

        private void DrawTrackingTest(UiTheme theme)
        {
            // ON にすると形状を最新にしてから（重いので次のフレームで）トラッキングを動かす
            bool test = GUILayout.Toggle(_trackingTest, Loc.T(
                    "Test with live tracking (before saving)", "実際のトラッキングで試す（保存する前に）",
                    "실제 트래킹으로 확인 (저장하기 전에)", "用实际追踪测试（保存前）", "用實際追蹤測試（儲存前）"),
                theme != null ? theme.TryButton : GUI.skin.button, GUILayout.ExpandWidth(true));
            if (test != _trackingTest)
            {
                if (test)
                {
                    _deferred = () => SetTrackingTest(true);
                }
                else
                {
                    SetTrackingTest(false);
                }
            }

            if (!_trackingTest)
            {
                return;
            }

            // 動いていない理由（顔が映っていない・パーフェクトシンクが OFF）か、動作中であること
            if (_faceDriver == null || !_faceDriver.IsTracking)
            {
                GuiControls.Warning(Loc.T(
                    "No face detected. Turn on tracking in the Tracking tab and face the camera.",
                    "顔を検出していません。トラッキングタブでトラッキングを ON にして、カメラに顔を映してください。",
                    "얼굴이 감지되지 않습니다. 트래킹 탭에서 트래킹을 켜고 카메라에 얼굴을 비추세요.",
                    "未检测到脸部。请在追踪分页开启追踪，并让摄像头拍到脸。",
                    "未偵測到臉部。請在追蹤分頁開啟追蹤，並讓攝影機拍到臉。"));
            }
            else if (!_faceDriver.IsPerfectSyncActive)
            {
                GuiControls.Warning(Loc.T(
                    "Perfect sync is not running. Turn on Perfect sync in the Tracking tab (MediaPipe, iPhone, VMC or iFacialMocap only).",
                    "パーフェクトシンクが動いていません。トラッキングタブの「パーフェクトシンク」を ON にしてください（MediaPipe・iPhone・VMC・iFacialMocap のみ）。",
                    "퍼펙트 싱크가 작동하지 않습니다. 트래킹 탭의 '퍼펙트 싱크'를 켜세요(MediaPipe·iPhone·VMC·iFacialMocap만).",
                    "完美同步未运行。请在追踪分页开启“完美同步”（仅限 MediaPipe、iPhone、VMC、iFacialMocap）。",
                    "完美同步未運作。請在追蹤分頁開啟「完美同步」（僅限 MediaPipe、iPhone、VMC、iFacialMocap）。"));
            }
            else
            {
                GuiControls.Status(Loc.T(
                    "Moving with live tracking. After changing shapes, press Apply to update.\nTune the mouth and cheeks in the panel on the left.",
                    "トラッキングで動かしています。形状を変えたら「反映」で更新されます。\n口・頬の動きは左のパネルで調整できます。",
                    "트래킹으로 움직이는 중입니다. 형상을 바꾼 뒤 '반영'으로 갱신됩니다.\n입·볼 움직임은 왼쪽 패널에서 조정할 수 있습니다.",
                    "正在用追踪驱动。更改形状后点击“应用”即可更新。\n可在左侧面板调整嘴部和脸颊动作。",
                    "正在用追蹤驅動。變更形狀後點擊「套用」即可更新。\n可在左側面板調整嘴部和臉頰動作。"), true);
            }
        }

        private static GUIStyle WrapButton(UiTheme theme)
        {
            // 幅に収まらない文字を折り返すボタン（テーマが無ければ通常のボタン）
            return theme != null ? theme.WrapButton : GUI.skin.button;
        }

        private void DrawDiagram()
        {
            // 案内図（中央寄せ。選択中の目印を大きく黄色で示し、押すとその目印を選ぶ）
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            Rect rect = GUILayoutUtility.GetRect(DiagramSize, DiagramSize, GUILayout.Width(DiagramSize),
                GUILayout.Height(DiagramSize));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            List<LandmarkId> ids = ActiveLandmarks();
            float radius = DiagramSize * 0.45f;

            // 押した位置に最も近い目印を選ぶ
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                float best = float.MaxValue;
                foreach (LandmarkId landmark in ids)
                {
                    float distance = (DiagramPoint(rect, radius, landmark) - current.mousePosition).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        _selected = landmark;
                    }
                }

                current.Use();
            }

            if (current.type != EventType.Repaint)
            {
                return;
            }

            // 背景と、目印の点（選択中 → 打った → まだ、の順に目立たせる）
            GUI.Box(rect, GUIContent.none);
            Color previous = GUI.color;
            foreach (LandmarkId landmark in ids)
            {
                bool selected = _selected == landmark;
                float size = selected ? DiagramSelectedDot : DiagramDot;
                GUI.color = selected ? SelectedColor : _landmarks.ContainsKey(landmark) ? PlacedColor : PendingColor;
                Vector2 point = DiagramPoint(rect, radius, landmark);
                GUI.DrawTexture(new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size), Texture2D.whiteTexture);
            }

            GUI.color = previous;
        }

        private static Vector2 DiagramPoint(Rect rect, float radius, LandmarkId landmark)
        {
            // 案内図の座標（上が +y）を GUI の座標（下が +y）へ
            Vector2 position = PerfectSyncLabels.DiagramPosition(landmark);
            return rect.center + new Vector2(position.x, -position.y) * radius;
        }

        private string LandmarkState(LandmarkId landmark)
        {
            // 打った / 自動（左右対称） / まだ
            if (!_landmarks.ContainsKey(landmark))
            {
                return "-";
            }

            return _autoMirrored.Contains(landmark)
                ? Loc.T("Auto", "自動", "자동", "自动", "自動")
                : Loc.T("Placed", "打った", "찍음", "已放置", "已放置");
        }

        private void DrawMethodSelector()
        {
            // これまでの式と、手本の顔から写す方式を切り替える（変えたら作り直し）
            int method = GuiControls.EnumSelector(
                Loc.T("How to build", "作り方", "만드는 방법", "生成方式", "生成方式"),
                PerfectSyncLabels.Methods(), _method);
            if (method != _method)
            {
                _method = method;
                _dirty = true;
            }

            GuiControls.Hint(_method == PerfectSyncSet.MethodTransfer
                ? Loc.T(
                    "Shape transfer fits a reference face to your points, then carries its eyelid, lip and jaw motion across. Saved files reopen with the method they were saved with.",
                    "形状の転写は、手本の顔を目印に合わせ、まぶた・唇・顎の動きを写します。保存したファイルは、保存したときの作り方で開きます。",
                    "형상 옮기기는 본보기 얼굴을 표시에 맞춘 뒤, 눈꺼풀·입술·턱의 움직임을 옮깁니다. 저장한 파일은 저장했을 때의 방식으로 엽니다.",
                    "形状转移会把参考脸对齐到标记，再把眼睑、嘴唇和下巴的动作搬过来。已保存的文件会用保存时的方式打开。",
                    "形狀轉移會把參考臉對齊到標記，再把眼瞼、嘴唇和下巴的動作搬過來。已儲存的檔案會用儲存時的方式開啟。")
                : Loc.T(
                    "Current formulas move each part by a fixed rule. Switch to Shape transfer to carry motion from a reference face.",
                    "これまでの式は、部位ごとに決まった動きです。手本の顔の動きを写すときは「形状の転写」に切り替えます。",
                    "기존 수식은 부위마다 정해진 움직임입니다. 본보기 얼굴의 움직임을 옮기려면 '형상 옮기기'로 바꿉니다.",
                    "原有公式按每个部位的固定规则移动。要搬入参考脸的动作时，请切换到“形状转移”。",
                    "原有公式按每個部位的固定規則移動。要搬入參考臉的動作時，請切換到「形狀轉移」。"));
        }

        private bool HasRequiredLandmarks()
        {
            // 最小の段階の目印（無い部位の分を除く）が全部あるか
            return MissingRequired().Count == 0;
        }

        private void DrawShapesStep(UiTheme theme)
        {
            GuiControls.BeginCard(Loc.T("Check the shapes", "形状を確かめる", "형상 확인", "检查形状", "檢查形狀"));

            // 使える ARKit の形状の数（パーフェクトシンクとして動くか）
            int matched = _faceDriver != null ? _faceDriver.PerfectSyncShapeCount : 0;
            int total = ArKitFace.BlendShapeNames.Length;
            GuiControls.Status(Loc.T($"ARKit shapes in use: {matched}/{total}", $"使える ARKit の形状: {matched}/{total}",
                $"사용 가능한 ARKit 형상: {matched}/{total}", $"可用的 ARKit 形状: {matched}/{total}",
                $"可用的 ARKit 形狀: {matched}/{total}"), matched >= PerfectSyncBlendShapes.MinMatchedShapes);
            GuiControls.Hint(Loc.T(
                "For each shape choose Generate (made from the points), Existing shape (use one of the avatar's blend shapes) " +
                "or None. Select a shape name to test it with the slider and to choose the existing blend shape.",
                "形状ごとに「自動生成」（目印から作る）・「既存の BlendShape」（アバターの BlendShape を使う）・「なし」を選べます。" +
                "形状の名前を押すと、スライダーで試したり、使う既存の BlendShape を選んだりできます。",
                "형상마다 '자동 생성'(표시로 만듦)·'기존 BlendShape'(아바타의 BlendShape 사용)·'없음'을 고를 수 있습니다. " +
                "형상 이름을 누르면 슬라이더로 확인하거나 사용할 기존 BlendShape를 고를 수 있습니다.",
                "每个形状可选择“自动生成”（由标记生成）·“现有 BlendShape”（使用形象的 BlendShape）·“无”。" +
                "点击形状名称可用滑块测试，或选择要使用的现有 BlendShape。",
                "每個形狀可選擇「自動生成」（由標記生成）·「現有 BlendShape」（使用形象的 BlendShape）·「無」。" +
                "點擊形狀名稱可用滑桿測試，或選擇要使用的現有 BlendShape。"));

            // 作り方を変えたら、反映で作り直す
            DrawMethodSelector();

            // 目の動きの既定（目のボーンの有無で変わる）
            GuiControls.Hint(HasEyeBones
                ? Loc.T("Eye movement is off by default because the eye bones already follow your gaze.",
                    "目の動きは目のボーンで視線を動かすため、既定では作りません。",
                    "눈 움직임은 눈 본으로 시선을 움직이므로 기본으로 만들지 않습니다.",
                    "眼球运动由眼睛骨骼控制视线，因此默认不生成。",
                    "眼球運動由眼睛骨骼控制視線，因此預設不生成。")
                : Loc.T("This avatar has no eye bones, so eye movement is generated (only pupils painted on the face mesh move).",
                    "このアバターには目のボーンが無いため、目の動きも自動生成します（顔メッシュに描かれた瞳だけが動きます）。",
                    "이 아바타에는 눈 본이 없으므로 눈 움직임도 자동 생성합니다(얼굴 메시에 그려진 눈동자만 움직입니다).",
                    "此形象没有眼睛骨骼，因此也自动生成眼球运动（只移动画在面部网格上的瞳孔）。",
                    "此形象沒有眼睛骨骼，因此也自動生成眼球運動（只移動畫在臉部網格上的瞳孔）。"));

            // 自動生成しても動く部分が無かった形状
            int empty = CountEmptyShapes();
            if (empty > 0)
            {
                GuiControls.Warning(Loc.T(
                    $"{empty} shape(s) marked \"Nothing to move\" had no part to move (e.g. the eyes are a separate mesh or the part is missing). Choose Existing shape or None for them.",
                    $"「動く部分なし」の {empty} 個の形状は、自動生成しても動く部分がありませんでした（目が別のメッシュ・その部位が無いなど）。「既存の BlendShape」か「なし」を選んでください。",
                    $"'움직일 부분 없음'인 형상 {empty}개는 자동 생성해도 움직일 부분이 없었습니다(눈이 별도 메시이거나 해당 부위가 없는 등). '기존 BlendShape' 또는 '없음'을 고르세요.",
                    $"标为“无可移动部分”的 {empty} 个形状自动生成后没有可移动的部分（眼睛是单独网格、没有该部位等）。请选择“现有 BlendShape”或“无”。",
                    $"標為「無可移動部分」的 {empty} 個形狀自動生成後沒有可移動的部分（眼睛是單獨網格、沒有該部位等）。請選擇「現有 BlendShape」或「無」。"));
            }

            // 設定を変えたら作り直しが必要
            if (_dirty)
            {
                GuiControls.Warning(Loc.T("Settings changed. Press Apply to update the preview.",
                    "設定を変えました。「反映」で試す形状を更新します。", "설정을 바꿨습니다. '반영'으로 미리보기를 갱신합니다.",
                    "设置已更改。点击“应用”更新预览。", "設定已變更。點擊「套用」更新預覽。"));
            }

            // 長い名前のボタンは横に並べると右が見切れるので、1 行ずつ折り返して置く
            GUIStyle wrap = WrapButton(theme);
            if (GUILayout.Button(Loc.T("Apply", "反映", "반영", "应用", "套用"),
                    theme != null ? theme.AccentButton : GUI.skin.button))
            {
                _deferred = GenerateAndShow;
            }

            if (GUILayout.Button(Loc.T("Auto-assign existing shapes", "既存の BlendShape を自動で割り当て",
                    "기존 BlendShape 자동 할당", "自动分配现有 BlendShape", "自動分配現有 BlendShape"), wrap))
            {
                StopPreview();
                AutoAssign();
            }

            // 保存する前に、実際のトラッキングで動きを確かめる
            DrawTrackingTest(theme);

            // 試す値（形状を選んでいるときだけ）
            if (_previewIndex >= 0)
            {
                _previewWeight = GuiControls.SliderOrField(ArKitFace.BlendShapeNames[_previewIndex], _previewWeight,
                    0f, 100f, ref _previewInput, "ps-preview", "0", ShapeNameWidth, true, 0f);
            }

            // 形状の一覧
            string[] sources = PerfectSyncLabels.Sources();
            for (int i = 0; i < _shapes.Length; i++)
            {
                DrawShapeRow(theme, i, sources);
            }

            GuiControls.EndCard();
        }

        private void DrawShapeRow(UiTheme theme, int index, string[] sources)
        {
            ShapeState shape = _shapes[index];
            GUIStyle hint = theme != null ? theme.Hint : GUI.skin.label;

            // 名前（押すと試す形状にする）と出どころ（元からある形状は選べない）
            GUILayout.BeginHorizontal();
            bool selected = GUILayout.Toggle(_previewIndex == index, ArKitFace.BlendShapeNames[index], GUI.skin.button,
                GUILayout.Width(ShapeNameWidth));
            if (selected != (_previewIndex == index))
            {
                SelectPreview(selected ? index : -1);
                _search = string.Empty;
            }

            if (shape.Native)
            {
                GUILayout.Label(Loc.T("Built in", "元からある", "원래 있음", "原本就有", "原本就有"),
                    theme != null ? theme.Success : GUI.skin.label, GuiControls.Shrinkable);
            }
            else
            {
                int source = GuiControls.Selector(null, sources, shape.Source, null);
                if (source != shape.Source && source >= 0)
                {
                    shape.Source = source;
                    _dirty = true;
                    StopPreview();
                }
            }

            GUILayout.EndHorizontal();

            // 自動生成しても動く部分が無かった形状に印を付ける
            if (IsEmptyShape(index))
            {
                GUILayout.Label(Loc.T("Nothing to move", "動く部分なし", "움직일 부분 없음", "无可移动部分", "無可移動部分"),
                    theme != null ? theme.WarningText : GUI.skin.label);
            }
            if (shape.Native || shape.Source == PerfectSyncSet.SourceNone)
            {
                return;
            }

            // 強さ（欄へ直接入力できる。🔄 で 1 に戻す）
            if (_strengthInputs.Length != _shapes.Length)
            {
                _strengthInputs = new string[_shapes.Length];
            }

            float strength = GuiControls.SliderOrField(
                Loc.T("Strength", "強さ", "강도", "强度", "強度"),
                shape.Strength, 0f, PerfectSyncSet.MaxStrength, ref _strengthInputs[index],
                "ps-shape-" + index, "0.##", ShapeNameWidth, true, 1f);
            if (!Mathf.Approximately(strength, shape.Strength))
            {
                shape.Strength = strength;
                _dirty = true;
                StopPreview();
            }

            if (shape.Source != PerfectSyncSet.SourceExisting)
            {
                return;
            }

            // 割り当てた既存の BlendShape（選択中の形状なら候補を検索して選べる）
            string bound = shape.Binding >= 0 && shape.Binding < _candidates.Count
                ? _candidates[shape.Binding].Label
                : Loc.T("(not chosen)", "（未選択）", "(선택 안 함)", "（未选择）", "（未選擇）");
            GUILayout.Label(Loc.T("Uses: ", "使う BlendShape: ", "사용할 BlendShape: ", "使用的 BlendShape: ", "使用的 BlendShape: ")
                + bound, hint);
            if (_previewIndex == index)
            {
                DrawCandidateSearch(theme, shape);
            }
        }

        private bool IsEmptyShape(int index)
        {
            // 前回の生成で動く部分が無く、今も「自動生成」のままの形状
            return _emptyShapes.Contains(index) && !_shapes[index].Native
                && _shapes[index].Source == PerfectSyncSet.SourceGenerated;
        }

        private int CountEmptyShapes()
        {
            // 「動く部分なし」の印が付く形状の数
            int count = 0;
            foreach (int index in _emptyShapes)
            {
                if (IsEmptyShape(index))
                {
                    count++;
                }
            }

            return count;
        }

        private void DrawCandidateSearch(UiTheme theme, ShapeState shape)
        {
            // 名前で絞り込んだ候補を、一度に出せる数まで並べる
            _search = GuiControls.TextField(Loc.T("Search", "検索", "검색", "搜索", "搜尋"), _search, 64);
            int shown = 0;
            for (int c = 0; c < _candidates.Count && shown < MaxSearchResults; c++)
            {
                ShapeCandidate candidate = _candidates[c];
                if (_search.Length > 0 && candidate.Label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                shown++;
                if (GUILayout.Toggle(shape.Binding == c, candidate.Label, WrapButton(theme)) && shape.Binding != c)
                {
                    shape.Binding = c;
                    _dirty = true;
                    StopPreview();
                }
            }

            if (shown == 0)
            {
                GUILayout.Label(Loc.T("No matching blend shapes.", "一致する BlendShape はありません。", "일치하는 BlendShape가 없습니다.",
                    "没有匹配的 BlendShape。", "沒有符合的 BlendShape。"), theme != null ? theme.Hint : GUI.skin.label);
            }
        }

        private void DrawFooter(UiTheme theme)
        {
            // 直前の結果（失敗は赤、成功は緑。長い文がパネルの幅を広げないよう折り返す）
            if (!string.IsNullOrEmpty(_message))
            {
                GUIStyle style = theme == null ? GUI.skin.label : _messageIsError ? theme.ErrorWrapped : theme.SuccessWrapped;
                GUILayout.Label(_message, style);
            }

            // 閉じるの確認中は、確認の行だけを出す
            if (_confirmingClose)
            {
                DrawCloseConfirmation();
                return;
            }

            // 別名で保存・閉じる（閉じるは 1 段階目。押すと確認を出す。横に並べると右が見切れるので縦に積む）
            GUIStyle wrap = WrapButton(theme);
            if (GUILayout.Button(Loc.T("Save as new file...", "別名で保存...", "다른 이름으로 저장...", "另存为...", "另存新檔..."),
                    theme != null ? theme.AccentButton : GUI.skin.button))
            {
                _deferred = Save;
            }

            if (GUILayout.Button(Loc.T("Close", "閉じる", "닫기", "关闭", "關閉"), wrap))
            {
                _confirmingClose = true;
            }

            // 保存したファイルをすぐ読み込む
            if (!string.IsNullOrEmpty(_savedPath)
                && GUILayout.Button(Loc.T("Load the saved avatar", "保存したアバターを読み込む", "저장한 아바타 불러오기",
                    "加载已保存的虚拟形象", "載入已儲存的虛擬形象"), wrap))
            {
                _deferred = LoadSaved;
            }

            GuiControls.Hint(Loc.T("Closing discards unsaved changes.", "閉じると保存していない変更は破棄されます。",
                "닫으면 저장하지 않은 변경은 버려집니다.", "关闭后未保存的更改将被丢弃。", "關閉後未儲存的變更將被捨棄。"));
        }

        private void DrawCloseConfirmation()
        {
            // 2 段階目: 保存していない変更を捨てて閉じるか、編集に戻るか
            GuiControls.Warning(Loc.T("Close the editor? Unsaved changes will be lost.",
                "編集をやめますか？保存していない変更は破棄されます。",
                "편집을 끝내시겠습니까? 저장하지 않은 변경은 버려집니다.",
                "要结束编辑吗？未保存的更改将被丢弃。", "要結束編輯嗎？未儲存的變更將被捨棄。"));
            GUIStyle wrap = WrapButton(UiTheme.Current);
            if (GuiControls.DangerButton(Loc.T("Close without saving", "保存せずに閉じる", "저장하지 않고 닫기",
                    "不保存并关闭", "不儲存並關閉")))
            {
                _deferred = Close;
            }

            if (GUILayout.Button(Loc.T("Keep editing", "編集を続ける", "계속 편집", "继续编辑", "繼續編輯"), wrap))
            {
                _confirmingClose = false;
            }
        }

        private void GenerateAndShow()
        {
            // 形状を作って試し、形状の段階へ進む
            if (!ApplyChanges())
            {
                return;
            }

            _step = Step.Shapes;
            int count = _generated != null ? _generated.Shapes.Count : 0;
            SetMessage(Loc.T($"Generated {count} shapes.", $"{count} 個の形状を作りました。", $"형상 {count}개를 만들었습니다.",
                $"已生成 {count} 个形状。", $"已生成 {count} 個形狀。"), false);
        }
    }
}
