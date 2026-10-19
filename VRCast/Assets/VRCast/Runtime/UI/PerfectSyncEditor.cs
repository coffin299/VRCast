using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using VRCast.Animations;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Dynamics;
using VRCast.PerfectSync;
using VRCast.Platform;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// パーフェクトシンク化の編集画面。顔を大きく映し、顔メッシュの線の上に目印を打たせて ARKit 互換の形状を作り、
    /// 既存の BlendShape の割り当てと合わせて試した後、元のファイルはそのままに別名の .vrcaster として保存する。
    /// 開いている間はメインのパネルの代わりに右側の細いパネルを出す（描画は PerfectSyncEditor.Panel.cs）。
    /// </summary>
    public sealed partial class PerfectSyncEditor
    {
        // ログのカテゴリ名
        private const string LogCategory = "PerfectSync";

        // 線を描く範囲（顔の中心からの距離。顔を写す範囲の半分の高さに対する倍率）
        private const float RegionRadiusRatio = 1.6f;

        // 顔へ寄るときの距離（顔を写す範囲が画面の高さに収まる距離に対する倍率）
        private const float FaceDistanceRatio = 1.1f;

        // 目印をつかめる距離（画面の画素）
        private const float PickRadius = 14f;

        // 元に戻せる操作の数（古いものから捨てる）
        private const int MaxUndo = 100;

        // 歯・舌のメッシュとみなす名前（小文字で含むか調べる）
        private static readonly string[] MouthPartKeywords = { "teeth", "tooth", "tongue", "歯", "舌" };

        // 編集の段階（目印を打つ → 形状を確かめる）
        private enum Step
        {
            Points,
            Shapes,
        }

        /// <summary>
        /// ARKit の形状 1 つの設定（出どころ・強さ・割り当てる既存の BlendShape）。
        /// </summary>
        private sealed class ShapeState
        {
            // 自動生成 / 既存 / なし（PerfectSyncSet.Source*）
            public int Source = PerfectSyncSet.SourceGenerated;

            // 強さ（0〜PerfectSyncSet.MaxStrength）
            public float Strength = 1f;

            // ARKit の名前の BlendShape が元からある（生成・割り当ては不要で、そのまま使う）
            public bool Native;

            // 割り当てる既存の BlendShape（_candidates の番号。-1 = 未選択）
            public int Binding = -1;
        }

        /// <summary>
        /// 元に戻すための、目印を変える直前の状態（目印・自動で決めた右側・左右対称・無い部位・選択）。
        /// </summary>
        private sealed class UndoState
        {
            public Dictionary<LandmarkId, PerfectSyncLandmark> Landmarks;
            public HashSet<LandmarkId> AutoMirrored;
            public bool Mirror;
            public FaceParts Absent;
            public LandmarkId? Selected;
        }

        /// <summary>
        /// 割り当てに使える既存の BlendShape 1 つ。
        /// </summary>
        private readonly struct ShapeCandidate
        {
            public readonly string Path;
            public readonly string Name;
            public readonly string Label;

            public ShapeCandidate(string path, string name, string label)
            {
                Path = path;
                Name = name;
                Label = label;
            }
        }

        private readonly AvatarSession _session;

        // 「実際のトラッキングで試す」で口・頬の強調を調整するための設定
        private readonly AppSettings _settings;
        private readonly OrbitCameraController _orbit;
        private readonly Camera _camera;
        private readonly PerfectSyncOverlay _overlay = new PerfectSyncOverlay();

        // 編集中のアバターと、その顔のトラッキング（編集中の形状を試すために書き込み先を作り直す）
        private LoadedAvatar _avatar;
        private FaceTrackingDriver _faceDriver;

        // 開く前の視点（閉じたら戻す）
        private CameraPose _savedPose;

        // 形状を作れるメッシュ（頂点を読めるもの）と、その表示名・選択中の顔メッシュ
        private readonly List<SkinnedMeshRenderer> _renderers = new List<SkinnedMeshRenderer>();
        private string[] _rendererLabels = Array.Empty<string>();
        private int _faceIndex;
        private SkinnedMeshRenderer _face;
        private string _facePath = string.Empty;
        private FaceMeshGeometry _geometry;

        // 今の姿勢の顔メッシュ（毎フレーム作り直す）と、そのワールド座標の頂点
        private Mesh _baked;
        private readonly List<Vector3> _bakedVertices = new List<Vector3>();
        private Vector3[] _world = Array.Empty<Vector3>();

        // 線を描く・クリックを判定する三角形と、その頂点・顔の中心
        private readonly List<int> _region = new List<int>();
        private readonly List<int> _regionVertices = new List<int>();
        private Vector3 _faceCenter;

        // 打った目印と、左右対称で自動で決めた右側の目印
        private readonly Dictionary<LandmarkId, PerfectSyncLandmark> _landmarks =
            new Dictionary<LandmarkId, PerfectSyncLandmark>();
        private readonly HashSet<LandmarkId> _autoMirrored = new HashSet<LandmarkId>();

        // 目印の細かさ・左右対称・歯と舌も動かすか
        private LandmarkDetail _detail = LandmarkDetail.Standard;
        private bool _mirror = true;
        private bool _includeMouthParts = true;

        // アバターに無い部位（その部位の目印は打たず、形状は既定で作らない）
        private FaceParts _absent;

        // 顔メッシュと一緒に動かせる歯・舌のメッシュの数（0 なら「歯・舌も動かす」は選べない）
        private int _mouthPartCount;

        // 視点を顔の正面に固定するか（開くたびに ON。拡大・移動はできる）
        private bool _lockView = true;

        // 保存前に実際のトラッキングで試している間 true（止めていた動きを設定どおりに動かす）
        private bool _trackingTest;

        // 試し始めた時点（か最後に保存した時点）の口・頬の強調の設定（保存せずにやめたらこの値へ戻す）
        private PerfectSyncBoostControls.Snapshot _boostSaved;

        // 次に打つ（選択中の）目印と、ドラッグ中か・そのドラッグで新しく打ったか
        private LandmarkId? _selected;
        private bool _dragging;

        // 元に戻す履歴（古いものが先頭）。つかんだだけで動かさなければ積まないよう、つかんだ時点の状態を控えておく
        private readonly List<UndoState> _undo = new List<UndoState>();
        private UndoState _pendingUndo;

        // 直前にクリックで打った目印（打った直後は選択が次の目印へ移るため、Delete で消す対象にする）
        private LandmarkId? _lastPlaced;

        // ARKit の並びと同じ数の形状の設定と、割り当てに使える既存の BlendShape
        private readonly ShapeState[] _shapes = new ShapeState[ArKitFace.BlendShapeNames.Length];
        private readonly List<ShapeCandidate> _candidates = new List<ShapeCandidate>();

        // 作った形状と、試しに足したメッシュ。_dirty = 設定を変えてから作り直していない
        private PerfectSyncShapeData _generated;
        private PerfectSyncMeshes _preview;
        private bool _dirty = true;

        // 自動生成しても動く部分が無かった形状（ARKit の番号。目が別のメッシュ・その部位が無いなど）
        private readonly HashSet<int> _emptyShapes = new HashSet<int>();

        // 試しに動かす形状（ARKit の番号。-1 = なし）・その値・書き込み先
        private int _previewIndex = -1;
        private float _previewWeight;
        private List<(BlendShapeOverlay Overlay, float Scale)> _previewTargets;

        // 段階・結果の表示・保存したファイル
        private Step _step = Step.Points;
        private string _message = string.Empty;
        private bool _messageIsError;
        private string _savedPath;

        // 「閉じる」の確認中なら true（保存していない変更を誤って捨てないよう 2 段階にする）
        private bool _confirmingClose;

        public PerfectSyncEditor(AvatarSession session, OrbitCameraController orbit, AppSettings settings)
        {
            _session = session;
            _settings = settings;
            _orbit = orbit;
            _camera = orbit != null ? orbit.GetComponent<Camera>() : null;
        }

        /// <summary>
        /// 編集中なら true。
        /// </summary>
        public bool IsOpen => _avatar != null;

        /// <summary>
        /// パネルの範囲（GUI の座標。マウスがこの上にある間はカメラ操作・目印の操作をしない）。
        /// </summary>
        public Rect PanelRect { get; private set; }

        /// <summary>
        /// アバターで編集を始められるか。始められなければ理由（表示用）を返す。
        /// </summary>
        public static string CanEdit(LoadedAvatar avatar)
        {
            // 読み込み済みの .vrcaster で、頂点を読める顔メッシュがあること
            if (avatar == null || avatar.Instance == null)
            {
                return Loc.T("Load an avatar first.", "先にアバターを読み込んでください。", "먼저 아바타를 불러오세요.",
                    "请先加载虚拟形象。", "請先載入虛擬形象。");
            }

            if (avatar.Vrm != null || avatar.Manifest == null)
            {
                return Loc.T("VRM avatars are not supported. Use a .vrcaster file.",
                    "VRM アバターには対応していません。.vrcaster ファイルを使ってください。",
                    "VRM 아바타는 지원하지 않습니다. .vrcaster 파일을 사용하세요.",
                    "不支持 VRM 虚拟形象。请使用 .vrcaster 文件。",
                    "不支援 VRM 虛擬形象。請使用 .vrcaster 檔案。");
            }

            if (CountReadableMeshes(avatar.Instance) == 0)
            {
                return Loc.T(
                    "The face mesh cannot be read. Export the avatar again with the latest VRCast Converter.",
                    "顔メッシュを読み取れません。最新の VRCast Converter でアバターを書き出し直してください。",
                    "얼굴 메시를 읽을 수 없습니다. 최신 VRCast Converter로 아바타를 다시 내보내세요.",
                    "无法读取面部网格。请用最新的 VRCast Converter 重新导出虚拟形象。",
                    "無法讀取臉部網格。請用最新的 VRCast Converter 重新匯出虛擬形象。");
            }

            return null;
        }

        /// <summary>
        /// 頂点を読めて BlendShape のあるメッシュの数（書き出し直し前の .vrcaster は 0）。
        /// </summary>
        public static int CountReadableMeshes(GameObject root)
        {
            int count = 0;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsEditable(renderer))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 表示中のアバターで編集を始める（始められなければ何もしない）。
        /// </summary>
        public void Open(LoadedAvatar avatar)
        {
            if (IsOpen || CanEdit(avatar) != null)
            {
                return;
            }

            _avatar = avatar;
            _faceDriver = avatar.Instance.GetComponent<FaceTrackingDriver>();

            // 顔の線と目印がずれないよう、揺れもの・待機モーション・トラッキング・まばたき・口パクを止める（閉じたら戻す）
            _trackingTest = false;
            SetMotionSuspended(true);

            // 読み込み時に足した形状を外し、元のメッシュから編集する（閉じたら足し直す）
            // （書き込みを先に戻す。差し替えた後のメッシュには足した形状の番号が無いため）
            _faceDriver?.ReleasePerfectSync();
            avatar.PerfectSyncMeshes?.Restore();
            avatar.PerfectSyncMeshes = null;
            _faceDriver?.RebuildPerfectSync(null);

            // 形状を作れるメッシュと割り当ての候補を集める
            CollectRenderers();
            CollectCandidates();

            // 前回の設定（パーフェクトシンク版のファイル）があれば引き継ぐ
            PerfectSyncSet saved = avatar.PerfectSync;
            bool resume = saved != null && !saved.IsEmpty;
            if (resume)
            {
                _detail = (LandmarkDetail)Mathf.Clamp(saved.detail, 0, PerfectSyncSet.MaxDetail);
                _mirror = saved.mirror;
                _includeMouthParts = saved.includeMouthParts;
            }

            // 無い部位は保存してあればそれ、無ければ全部ある（別のアバターの設定を持ち越さない）
            _absent = resume ? (FaceParts)saved.absentParts & FaceParts.All : FaceParts.None;

            // 顔メッシュを選び、保存してあった目印をその顔メッシュに戻す
            SelectFace(ChooseFace(resume ? saved.facePath : null));
            if (resume && saved.facePath == _facePath)
            {
                RestoreLandmarks(saved.landmarks);
            }

            // 形状の設定（保存してあればそれ、無ければ自動の割り当て）
            ResetShapes(resume ? saved : null);

            // 結果の表示を消し、最初の目印を選び、顔へ寄る
            _step = Step.Points;
            _message = string.Empty;
            _messageIsError = false;
            _savedPath = null;
            _confirmingClose = false;
            _generated = null;
            _emptyShapes.Clear();
            _dirty = true;
            _selected = NextUnplaced();
            _savedPose = _orbit.Pose;
            _lockView = true;
            FocusFace();
        }

        /// <summary>
        /// 編集をやめる（試しに足した形状を外し、アバターのパーフェクトシンクと視点を元に戻す）。
        /// </summary>
        public void Close()
        {
            Shutdown(true);
        }

        /// <summary>
        /// 毎フレームの処理。pointerFree はマウスがパネルの外にあり、目印を操作してよいとき true。
        /// </summary>
        public void Update(bool pointerFree)
        {
            if (!IsOpen)
            {
                return;
            }

            // アバターが替わった・破棄されたら、残った複製だけ片付けて閉じる
            if (_session.Current != _avatar || _avatar.Instance == null || _face == null)
            {
                Shutdown(false);
                return;
            }

            // パネルのボタンで決めた処理（閉じたらここで終わり）
            if (!RunDeferred())
            {
                return;
            }

            // 視点の向きの固定（回転だけ止める）
            _orbit.RotationLocked = _lockView;

            // 今の姿勢の顔メッシュと顔の中心
            BakeFace();
            _avatar.CalculateFaceFrame(out _faceCenter, out float _);

            // 画像認識が終わっていれば目印を置く
            PollDetection();

            // 目印の操作と、試している形状の書き込み
            HandleMouse(pointerFree);
            HandleKeys();
            WritePreview();
        }

        private void Shutdown(bool avatarAlive)
        {
            StopPreview();
            CancelDetection();
            _orbit.RotationLocked = false;
            _trackingTest = false;
            if (avatarAlive)
            {
                // 試しに足した形状を外し、アバター自身のパーフェクトシンク（読み込んだファイルの内容）を足し直す
                _faceDriver?.ReleasePerfectSync();
                _preview?.Restore();
                if (_avatar.PerfectSyncShapes.Shapes.Count > 0)
                {
                    _avatar.PerfectSyncMeshes = PerfectSyncMeshes.Apply(_avatar.Instance.transform, _avatar.PerfectSyncShapes);
                }

                _faceDriver?.RebuildPerfectSync(_avatar.PerfectSync.bindings);
                _orbit.SetPose(_savedPose);

                // 止めていた動きを設定どおりに戻す
                SetMotionSuspended(false);
            }
            else
            {
                // アバターごと破棄された後は、複製のメッシュだけ破棄する
                _preview?.Restore(false);
            }

            // 作ったものを破棄して閉じた状態にする
            _deferred = null;
            _confirmingClose = false;
            _previewIndex = -1;
            _preview = null;
            _generated = null;
            _emptyShapes.Clear();
            if (_baked != null)
            {
                UnityEngine.Object.Destroy(_baked);
                _baked = null;
            }

            _overlay.Dispose();
            _renderers.Clear();
            _candidates.Clear();
            _landmarks.Clear();
            _autoMirrored.Clear();
            ClearUndo();
            _region.Clear();
            _regionVertices.Clear();
            _face = null;
            _geometry = null;
            _faceDriver = null;
            _avatar = null;
            _dragging = false;
        }

        private void SetTrackingTest(bool on)
        {
            // 同じ状態なら何もしない
            if (_trackingTest == on)
            {
                return;
            }

            if (on)
            {
                // 試す形状を最新にし（作れなければ始めない）、スライダーで試していた形状をやめる
                if ((_dirty || _preview == null) && !ApplyChanges())
                {
                    return;
                }

                StopPreview();
                _previewIndex = -1;

                // 口・頬の調整を保存せずにやめたときに戻せるよう、今の値を控える
                if (_settings != null)
                {
                    _boostSaved = PerfectSyncBoostControls.Snapshot.Capture(_settings);
                }
            }
            // 試す間は止めていた動き（トラッキング・まばたき・口パク・揺れもの・待機モーション）を設定どおりに動かす
            _trackingTest = on;
            SetMotionSuspended(!on);
        }

        private void SaveBoostChanges()
        {
            // 設定ファイルへすぐ保存し、以後はこの値を戻し先にする（トラッキングタブも同じ設定を使う）
            AppBootstrap.Save();
            _boostSaved = PerfectSyncBoostControls.Snapshot.Capture(_settings);
        }

        private void SetMotionSuspended(bool suspended)
        {
            GameObject root = _avatar.Instance;

            // 揺れもの（無いアバターでは付いていない）
            var physics = root.GetComponent<PhysBoneSimulator>();
            if (physics != null)
            {
                physics.Suspended = suspended;
            }

            // 待機モーション（呼吸・体の揺れ・頭のゆらぎ）
            var idle = root.GetComponent<IdleMotionController>();
            if (idle != null)
            {
                idle.Suspended = suspended;
            }

            // 顔のトラッキング（頭・視線・カメラ目線・自動で当てた表情）
            if (_faceDriver != null)
            {
                _faceDriver.Suspended = suspended;
            }

            // 腕・手のトラッキング（待機ポーズへ戻す）
            var hands = root.GetComponent<HandTrackingDriver>();
            if (hands != null)
            {
                hands.Suspended = suspended;
            }

            // 外部アプリからの全身のトラッキング（待機ポーズへ戻す）
            var fullBody = root.GetComponent<FullBodyTrackingDriver>();
            if (fullBody != null)
            {
                fullBody.Suspended = suspended;
            }

            // まばたき（目を開いたまま）と口パク（口を閉じたまま）。目印を打つまぶた・唇の形を変えない
            var blink = root.GetComponent<BlinkController>();
            if (blink != null)
            {
                blink.Suspended = suspended;
            }

            var lipSync = root.GetComponent<LipSyncController>();
            if (lipSync != null)
            {
                lipSync.Suspended = suspended;
            }
        }

        private static bool IsEditable(SkinnedMeshRenderer renderer)
        {
            // 頂点を読めて、BlendShape のある（書き出し時に読める複製にしたもの）メッシュ
            Mesh mesh = renderer.sharedMesh;
            return mesh != null && mesh.isReadable && mesh.vertexCount > 0 && mesh.blendShapeCount > 0;
        }

        private void CollectRenderers()
        {
            // 頂点を読めるメッシュと、その表示名（オブジェクト名と頂点数）
            _renderers.Clear();
            foreach (SkinnedMeshRenderer renderer in _avatar.Instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsEditable(renderer))
                {
                    _renderers.Add(renderer);
                }
            }

            _rendererLabels = new string[_renderers.Count];
            for (int i = 0; i < _renderers.Count; i++)
            {
                _rendererLabels[i] = $"{_renderers[i].name} ({_renderers[i].sharedMesh.vertexCount})";
            }
        }

        private void CollectCandidates()
        {
            // 全メッシュの BlendShape（ARKit の名前のものは元から使えるので除く）
            _candidates.Clear();
            Transform root = _avatar.Instance.transform;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    continue;
                }

                string path = TransformPath.Of(renderer.transform, root);
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    if (PerfectSyncBlendShapes.Match(name) < 0)
                    {
                        _candidates.Add(new ShapeCandidate(path, name, $"{name} ({renderer.name})"));
                    }
                }
            }
        }

        private int ChooseFace(string savedPath)
        {
            // 保存してあった顔メッシュ → 口パク・まばたきのメッシュ → BlendShape が最も多いメッシュ
            Transform root = _avatar.Instance.transform;
            AvatarDescriptorData descriptor = _avatar.Descriptor;
            string[] preferred =
            {
                savedPath,
                descriptor?.lipSync?.meshPath,
                descriptor?.eyelids?.meshPath,
            };
            foreach (string path in preferred)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                int index = _renderers.FindIndex(r => TransformPath.Of(r.transform, root) == path);
                if (index >= 0)
                {
                    return index;
                }
            }

            int best = 0;
            for (int i = 1; i < _renderers.Count; i++)
            {
                if (_renderers[i].sharedMesh.blendShapeCount > _renderers[best].sharedMesh.blendShapeCount)
                {
                    best = i;
                }
            }

            return best;
        }

        private void SelectFace(int index)
        {
            // 顔メッシュを替えたら目印は打ち直し（三角形の番号はメッシュごとに違う）
            _faceIndex = Mathf.Clamp(index, 0, _renderers.Count - 1);
            _face = _renderers[_faceIndex];
            _facePath = TransformPath.Of(_face.transform, _avatar.Instance.transform);
            _geometry = FaceMeshGeometry.From(_face.sharedMesh);
            _landmarks.Clear();
            _autoMirrored.Clear();
            _selected = NextUnplaced();
            _dirty = true;

            // 別のメッシュの目印には戻せないので履歴も捨てる
            ClearUndo();

            // この顔メッシュと一緒に動かせる歯・舌のメッシュを数える
            _mouthPartCount = CountMouthParts();

            // 今の姿勢の形を作り、顔のまわりの三角形を決める
            BakeFace();
            BuildRegion();
        }

        private void RestoreLandmarks(PerfectSyncLandmark[] saved)
        {
            // 定義済みの番号で、無い部位のものではなく、三角形の番号がこのメッシュに収まるものだけ戻す
            if (saved == null || _geometry == null)
            {
                return;
            }

            foreach (PerfectSyncLandmark landmark in saved)
            {
                if (landmark != null && FaceLandmarks.IsDefined(landmark.id)
                    && (FacePartRules.Landmark((LandmarkId)landmark.id) & _absent) == 0
                    && _geometry.TryGetPoint(landmark, out _))
                {
                    var id = (LandmarkId)landmark.id;
                    _landmarks[id] = Copy(landmark);

                    // 左右対称で自動で決めた右側は、再開後も左側に合わせて動く「自動」として戻す
                    if (_mirror && landmark.mirrored && FaceLandmarks.IsRight(id))
                    {
                        _autoMirrored.Add(id);
                    }
                }
            }

            // 自動の右側を今の左側から置き直す（左側が無い自動の右側はここで消える）
            Remirror(null);
            _selected = NextUnplaced();
        }

        private void ResetShapes(PerfectSyncSet saved)
        {
            // まず全部を「自動生成・強さ 1」にし、元からある ARKit の名前の BlendShape を調べる
            bool[] native = FindNativeShapes();
            for (int i = 0; i < _shapes.Length; i++)
            {
                _shapes[i] = new ShapeState { Native = native[i] };
            }

            if (saved != null)
            {
                ApplySavedShapes(saved);
                return;
            }

            // 目の動きは目のボーンがあればボーンで動かすので、既定では作らない（無ければ顔に描いた瞳を動かす形状を作る）。
            // 舌出しは目印から自動生成できないので既定は「なし」（既存の BlendShape があれば AutoAssign で割り当てる）
            for (int i = 0; i < _shapes.Length; i++)
            {
                string name = ArKitFace.BlendShapeNames[i];
                if ((HasEyeBones && PerfectSyncLabels.IsEyeLook(name)) || name == "tongueOut")
                {
                    _shapes[i].Source = PerfectSyncSet.SourceNone;
                }
            }

            // 無い部位の形状は作らない
            SetAbsentShapeSources(_absent, true);

            // 既存の BlendShape に名前で対応が付くものは、既存を使う
            AutoAssign();
        }

        /// <summary>
        /// 視線で動かす目のボーンがあれば true（無ければ目の動きも形状で作る。視線用 BlendShape だけのときは形状側で動かす）。
        /// </summary>
        private bool HasEyeBones => _faceDriver != null && _faceDriver.HasEyeBones;

        private void SetAbsentShapeSources(FaceParts parts, bool absent)
        {
            // parts の部位の形状のうち、無くしたなら「自動生成」を「なし」に、戻したなら「なし」を「自動生成」にする
            // （既存の BlendShape を割り当てたもの・元からある形状はそのまま）
            int from = absent ? PerfectSyncSet.SourceGenerated : PerfectSyncSet.SourceNone;
            int to = absent ? PerfectSyncSet.SourceNone : PerfectSyncSet.SourceGenerated;
            for (int i = 0; parts != FaceParts.None && i < _shapes.Length; i++)
            {
                ShapeState shape = _shapes[i];
                if (!shape.Native && shape.Source == from
                    && (FacePartRules.Shape(ArKitFace.BlendShapeNames[i]) & parts) != 0)
                {
                    shape.Source = to;
                    _dirty = true;
                }
            }
        }

        private void SetPartAbsent(FaceParts part, bool absent)
        {
            // 変わらなければ何もしない
            if (((_absent & part) != 0) == absent)
            {
                return;
            }

            // 変える前の状態を履歴に積み、無い部位を更新する
            PushUndo(Snapshot());
            _absent = absent ? _absent | part : _absent & ~part;

            // 無くした部位の目印を消す（自動で決めた右側も含む）
            if (absent)
            {
                foreach (LandmarkId id in FaceLandmarks.For(LandmarkDetail.Detailed))
                {
                    if ((FacePartRules.Landmark(id) & part) != 0 && _landmarks.Remove(id))
                    {
                        _autoMirrored.Remove(id);
                    }
                }
            }

            // その部位の形状の出どころを合わせ、選択中の目印が無くなったら次の目印へ
            SetAbsentShapeSources(part, absent);
            if (_selected.HasValue && (FacePartRules.Landmark(_selected.Value) & _absent) != 0)
            {
                _selected = NextUnplaced();
            }

            _lastPlaced = null;
            _dirty = true;
        }

        private void ApplySavedShapes(PerfectSyncSet saved)
        {
            // 保存してあった出どころと強さ（名前で対応させる）
            if (saved.shapes != null)
            {
                foreach (PerfectSyncShapeSetting setting in saved.shapes)
                {
                    int index = setting != null ? Array.IndexOf(ArKitFace.BlendShapeNames, setting.name) : -1;
                    if (index >= 0)
                    {
                        _shapes[index].Source = Mathf.Clamp(setting.source, PerfectSyncSet.SourceGenerated, PerfectSyncSet.SourceNone);
                        _shapes[index].Strength = Mathf.Clamp(setting.strength, 0f, PerfectSyncSet.MaxStrength);
                    }
                }
            }

            // 保存してあった割り当て（見つからない BlendShape は外す）
            if (saved.bindings == null)
            {
                return;
            }

            foreach (PerfectSyncBinding binding in saved.bindings)
            {
                int index = binding != null ? Array.IndexOf(ArKitFace.BlendShapeNames, binding.name) : -1;
                int candidate = index >= 0 ? FindCandidate(binding.path, binding.blendShape) : -1;
                if (candidate >= 0)
                {
                    _shapes[index].Source = PerfectSyncSet.SourceExisting;
                    _shapes[index].Binding = candidate;
                    _shapes[index].Strength = Mathf.Clamp(binding.weight, 0f, PerfectSyncSet.MaxStrength);
                }
            }
        }

        private void AutoAssign()
        {
            // 名前で対応が付いた既存の BlendShape を「既存」に割り当てる（強さは対応表の値）
            var candidates = new List<PerfectSyncAutoMapper.Candidate>(_candidates.Count);
            foreach (ShapeCandidate candidate in _candidates)
            {
                candidates.Add(new PerfectSyncAutoMapper.Candidate(candidate.Path, candidate.Name));
            }

            foreach (PerfectSyncBinding binding in PerfectSyncAutoMapper.Map(candidates))
            {
                int index = Array.IndexOf(ArKitFace.BlendShapeNames, binding.name);
                int candidate = FindCandidate(binding.path, binding.blendShape);
                if (index < 0 || candidate < 0 || _shapes[index].Native)
                {
                    continue;
                }

                _shapes[index].Source = PerfectSyncSet.SourceExisting;
                _shapes[index].Binding = candidate;
                _shapes[index].Strength = Mathf.Clamp(binding.weight, 0f, PerfectSyncSet.MaxStrength);
            }

            _dirty = true;
        }

        private bool[] FindNativeShapes()
        {
            // どれかのメッシュに ARKit の名前（表記ゆれを含む）の BlendShape があるか
            var native = new bool[_shapes.Length];
            foreach (SkinnedMeshRenderer renderer in _avatar.Instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                for (int i = 0; mesh != null && i < mesh.blendShapeCount; i++)
                {
                    int index = PerfectSyncBlendShapes.Match(mesh.GetBlendShapeName(i));
                    if (index >= 0)
                    {
                        native[index] = true;
                    }
                }
            }

            return native;
        }

        private int FindCandidate(string path, string blendShape)
        {
            // パスと名前が一致する候補の番号（無ければ -1）
            return _candidates.FindIndex(c => c.Path == (path ?? string.Empty) && c.Name == blendShape);
        }

        private void FocusFace()
        {
            // 顔の中心を正面から、顔が画面の高さに収まる距離で映す（アバターは +Z 向きなので 180 度回す）
            _avatar.CalculateFaceFrame(out Vector3 center, out float halfHeight);
            float fieldOfView = _orbit.FieldOfView;
            float distance = halfHeight * FaceDistanceRatio / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);
            _orbit.SetPose(new CameraPose
            {
                target = center,
                distance = distance,
                yaw = _avatar.Instance.transform.eulerAngles.y + 180f,
                pitch = 0f,
                fieldOfView = fieldOfView,
                zoom = 1f,
            });
        }

        private void BakeFace()
        {
            // 今の姿勢の顔メッシュのワールド座標（配列は使い回す）
            _world = BakeWorld(_world);
        }

        private Vector3[] BakeWorld(Vector3[] reuse)
        {
            // 今の姿勢・BlendShape の顔メッシュを作り、ワールド座標にする（拡大縮小なしで作り、位置と向きだけ掛ける）
            if (_baked == null)
            {
                _baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            }

            _face.BakeMesh(_baked, false);
            _baked.GetVertices(_bakedVertices);
            Vector3[] world = reuse != null && reuse.Length == _bakedVertices.Count
                ? reuse
                : new Vector3[_bakedVertices.Count];

            Matrix4x4 matrix = Matrix4x4.TRS(_face.transform.position, _face.transform.rotation, Vector3.one);
            for (int i = 0; i < world.Length; i++)
            {
                world[i] = matrix.MultiplyPoint3x4(_bakedVertices[i]);
            }

            return world;
        }

        private FaceAxes CurrentAxes()
        {
            // 顔の中心（両目の間）とアバターの左・上・前
            _avatar.CalculateFaceFrame(out Vector3 center, out float _);
            Transform root = _avatar.Instance.transform;
            return new FaceAxes(center, -root.right, root.up, root.forward);
        }

        private void BuildRegion()
        {
            // 顔の中心から一定の距離にある三角形を、線を描く・クリックを判定する範囲にする
            _region.Clear();
            _regionVertices.Clear();
            if (_geometry == null || _world.Length != _geometry.Vertices.Length)
            {
                return;
            }

            _avatar.CalculateFaceFrame(out _faceCenter, out float halfHeight);
            float radius = halfHeight * RegionRadiusRatio;
            float radiusSquared = radius * radius;
            int[] triangles = _geometry.Triangles;
            for (int t = 0; t < _geometry.TriangleCount; t++)
            {
                Vector3 centroid = (_world[triangles[t * 3]] + _world[triangles[t * 3 + 1]] + _world[triangles[t * 3 + 2]]) / 3f;
                if ((centroid - _faceCenter).sqrMagnitude <= radiusSquared)
                {
                    _region.Add(t);
                }
            }

            // 顔の位置が分からない（骨格の無いアバター等）ときは全部の三角形
            if (_region.Count == 0)
            {
                for (int t = 0; t < _geometry.TriangleCount; t++)
                {
                    _region.Add(t);
                }
            }

            // 範囲の三角形が使う頂点（重複なし）
            var used = new HashSet<int>();
            foreach (int t in _region)
            {
                for (int k = 0; k < 3; k++)
                {
                    if (used.Add(triangles[t * 3 + k]))
                    {
                        _regionVertices.Add(triangles[t * 3 + k]);
                    }
                }
            }
        }

        private void HandleMouse(bool pointerFree)
        {
            // トラッキングで試している間は顔が動くので、目印を打たない
            if (_geometry == null || _camera == null || _trackingTest)
            {
                return;
            }

            // 押したとき: 近くの目印をつかむか、表面をクリックしたら選択中の目印を打つ
            if (pointerFree && Input.GetMouseButtonDown(0))
            {
                LandmarkId? picked = PickLandmark(Input.mousePosition);
                if (picked.HasValue)
                {
                    // つかんだ時点の状態を控え、実際に動かしたときだけ履歴に積む
                    _pendingUndo = Snapshot();
                    _selected = picked;
                    _dragging = true;
                }
                else if (RaycastFace(out PerfectSyncLandmark hit))
                {
                    _selected ??= NextUnplaced();
                    if (_selected.HasValue)
                    {
                        // 打つ前の状態を履歴に積み、打った目印を Delete の対象として覚える
                        PushUndo(Snapshot());
                        PlaceLandmark(_selected.Value, hit);
                        _lastPlaced = _selected;
                        _dragging = true;
                    }
                }
            }

            // ドラッグ中: 表面に沿って動かす（反対側は動かした目印の相手だけ決め直し、ほかの目印は動かさない）
            if (_dragging && Input.GetMouseButton(0) && _selected.HasValue && RaycastFace(out PerfectSyncLandmark moved))
            {
                // つかんだ目印を初めて動かしたときに、つかむ前の状態を履歴に積む
                if (_pendingUndo != null)
                {
                    PushUndo(_pendingUndo);
                    _pendingUndo = null;
                }

                PlaceLandmark(_selected.Value, moved);
            }

            // 離したとき: まだ打っていない次の目印を選ぶ
            if (_dragging && !Input.GetMouseButton(0))
            {
                _dragging = false;
                _pendingUndo = null;
                _selected = NextUnplaced() ?? _selected;
            }
        }

        private void HandleKeys()
        {
            // 文字の入力中（検索欄など）は Delete で目印を消さない
            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            // 目印の段階以外・ドラッグ中は履歴・目印を変えない（見えていない目印・つかんでいる目印を消さないように）
            if (_step != Step.Points || _dragging)
            {
                return;
            }

            // Ctrl+Z で 1 つ前に戻す
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (control && Input.GetKeyDown(KeyCode.Z))
            {
                Undo();
                return;
            }

            // Delete / Backspace で、選択中の目印（打っていなければ直前に打った目印）を消す
            if (Input.GetKeyDown(KeyCode.Delete) || Input.GetKeyDown(KeyCode.Backspace))
            {
                RemoveDeleteTarget();
            }
        }

        /// <summary>
        /// Delete・「目印を消す」で消す目印。選択中の目印を打ってあればそれ、無ければ直前にクリックで打った目印
        /// （どちらも左右対称で自動で決めた右側は除く。左側を消せば一緒に消える）。
        /// </summary>
        private LandmarkId? DeleteTarget()
        {
            if (_selected.HasValue && CanRemove(_selected.Value))
            {
                return _selected;
            }

            return _lastPlaced.HasValue && CanRemove(_lastPlaced.Value) ? _lastPlaced : null;
        }

        private bool CanRemove(LandmarkId id)
        {
            // 打ってあり、自動で決めた右側でない（自動の右側は消しても左側からすぐ決め直される）
            return _landmarks.ContainsKey(id) && !_autoMirrored.Contains(id);
        }

        private void RemoveDeleteTarget()
        {
            LandmarkId? target = DeleteTarget();
            if (!target.HasValue)
            {
                return;
            }

            // 消す前の状態を履歴に積んで消し、消した目印を次に打つ目印として選ぶ
            PushUndo(Snapshot());
            RemoveLandmark(target.Value);
            _selected = target;
            if (_lastPlaced == target)
            {
                _lastPlaced = null;
            }
        }

        private void ClearAllLandmarks()
        {
            // 消す前の状態を履歴に積む（全部消しても元に戻せる）
            PushUndo(Snapshot());
            _landmarks.Clear();
            _autoMirrored.Clear();
            _lastPlaced = null;
            _dirty = true;
            _selected = NextUnplaced();
        }

        private UndoState Snapshot()
        {
            // 目印は複製して控える（後の操作で中身が変わっても影響しない）
            var landmarks = new Dictionary<LandmarkId, PerfectSyncLandmark>();
            foreach (KeyValuePair<LandmarkId, PerfectSyncLandmark> pair in _landmarks)
            {
                landmarks[pair.Key] = Copy(pair.Value);
            }

            return new UndoState
            {
                Landmarks = landmarks,
                AutoMirrored = new HashSet<LandmarkId>(_autoMirrored),
                Mirror = _mirror,
                Absent = _absent,
                Selected = _selected,
            };
        }

        private void PushUndo(UndoState state)
        {
            // 上限を超えたら最も古い操作から捨てる
            _undo.Add(state);
            if (_undo.Count > MaxUndo)
            {
                _undo.RemoveAt(0);
            }
        }

        private void Undo()
        {
            // 戻せる操作が無ければ何もしない
            if (_undo.Count == 0)
            {
                return;
            }

            // 最後に積んだ状態へ戻す（目印・自動の右側・左右対称・選択）
            UndoState state = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _landmarks.Clear();
            foreach (KeyValuePair<LandmarkId, PerfectSyncLandmark> pair in state.Landmarks)
            {
                _landmarks[pair.Key] = pair.Value;
            }

            _autoMirrored.Clear();
            _autoMirrored.UnionWith(state.AutoMirrored);
            _mirror = state.Mirror;

            // 無い部位が変わっていたら、変わった部位の形状の出どころも戻す
            FaceParts becomeAbsent = state.Absent & ~_absent;
            FaceParts becomePresent = _absent & ~state.Absent;
            _absent = state.Absent;
            SetAbsentShapeSources(becomeAbsent, true);
            SetAbsentShapeSources(becomePresent, false);
            _selected = state.Selected ?? NextUnplaced();
            _lastPlaced = null;
            _pendingUndo = null;
            _dirty = true;
        }

        private void ClearUndo()
        {
            // 履歴と、つかんだ時点の控え・直前に打った目印を捨てる
            _undo.Clear();
            _pendingUndo = null;
            _lastPlaced = null;
        }

        private LandmarkId? PickLandmark(Vector3 mouse)
        {
            // 画面上でマウスに最も近い（つかめる距離内の）目印
            LandmarkId? best = null;
            float bestDistance = PickRadius * PickRadius;
            foreach (KeyValuePair<LandmarkId, PerfectSyncLandmark> pair in _landmarks)
            {
                if (!_geometry.TryGetPoint(pair.Value, out Vector3 world, _world))
                {
                    continue;
                }

                Vector3 screen = PerfectSyncOverlay.ToScreen(_camera, world, out bool visible);
                float distance = ((Vector2)screen - (Vector2)mouse).sqrMagnitude;
                if (visible && distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pair.Key;
                }
            }

            return best;
        }

        private bool RaycastFace(out PerfectSyncLandmark hit)
        {
            // マウスの位置からの光線を、今の姿勢の顔のまわりの三角形に当てる
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            return _geometry.Raycast(_world, ray, _region, out hit, out float _);
        }

        private void PlaceLandmark(LandmarkId id, PerfectSyncLandmark hit)
        {
            // 手で打った目印にする（自動で決めた右側を動かしたら、手で打ったものとして扱う）
            hit.id = (int)id;
            _landmarks[id] = hit;
            _autoMirrored.Remove(id);
            _dirty = true;

            // 反対側はこの目印の相手だけ決め直す（中央の目印など相手の無い目印では、ほかの目印を動かさない）
            if (FaceLandmarks.TryGetMirror(id, out LandmarkId partner))
            {
                Remirror(partner);
            }
        }

        private void RemoveLandmark(LandmarkId id)
        {
            // 消した目印が左側なら、自動で決めた右側も消える（Remirror が片付ける）
            if (_landmarks.Remove(id))
            {
                _autoMirrored.Remove(id);
                _dirty = true;
                Remirror(null);
            }
        }

        private void Remirror(LandmarkId? only)
        {
            if (!_mirror || _geometry == null)
            {
                return;
            }

            // 今の姿勢（画面に見えている形）で求める。元のメッシュには BlendShape で隠した部品が重なっていることがあり、
            // そこへ吸い付かないようにする（今の姿勢がまだ無ければ取り込み時の形）
            bool posed = _world.Length == _geometry.Vertices.Length && _region.Count > 0;
            Vector3[] positions = posed ? _world : _geometry.Vertices;

            // 手で打った目印から左右対称の面を推定する。左右の組が無ければアバターの左右の向き、
            // 組も中央の目印も無ければ顔の中心（両目の間）を通る面にする
            var manual = new Dictionary<LandmarkId, Vector3>();
            foreach (KeyValuePair<LandmarkId, PerfectSyncLandmark> pair in _landmarks)
            {
                if (!_autoMirrored.Contains(pair.Key) && _geometry.TryGetPoint(pair.Value, out Vector3 point, positions))
                {
                    manual[pair.Key] = point;
                }
            }

            Vector3 fallbackPoint = Vector3.zero;
            Vector3 fallbackNormal = Vector3.right;
            if (posed)
            {
                FaceAxes axes = CurrentAxes();
                fallbackPoint = axes.Center;
                fallbackNormal = axes.Left;
            }

            // 手で打った左右の組が無ければ、中央の目印（唇・あご・鼻）ではなく顔の中心で決める
            // （中央の目印を少しずらしただけで、右側の目印が全部ずれないように）
            IReadOnlyDictionary<LandmarkId, Vector3> planeSource = posed && !HasManualPair(manual)
                ? new Dictionary<LandmarkId, Vector3>()
                : manual;
            FaceRig.EstimateSymmetryPlane(planeSource, fallbackPoint, fallbackNormal, out Vector3 planePoint,
                out Vector3 planeNormal);

            // 右側の目印を、左側を面で反転した位置の最も近い表面に置く（手で打った右側はそのまま）
            foreach (LandmarkId id in FaceLandmarks.For(LandmarkDetail.Detailed))
            {
                if (!FaceLandmarks.IsRight(id) || (only.HasValue && only.Value != id)
                    || !FaceLandmarks.TryGetMirror(id, out LandmarkId left))
                {
                    continue;
                }

                // 左側が無ければ、自動で決めた右側も消す
                if (!manual.TryGetValue(left, out Vector3 leftPoint))
                {
                    if (_autoMirrored.Remove(id))
                    {
                        _landmarks.Remove(id);
                    }

                    continue;
                }

                if (_landmarks.ContainsKey(id) && !_autoMirrored.Contains(id))
                {
                    continue;
                }

                _landmarks[id] = MirrorOf(_landmarks[left], leftPoint, planePoint, planeNormal, positions, posed, id);
                _autoMirrored.Add(id);
            }
        }

        private static bool HasManualPair(Dictionary<LandmarkId, Vector3> manual)
        {
            // 左右の両方を手で打った組があれば true
            foreach (LandmarkId id in manual.Keys)
            {
                if (FaceLandmarks.IsLeft(id) && FaceLandmarks.TryGetMirror(id, out LandmarkId right) && manual.ContainsKey(right))
                {
                    return true;
                }
            }

            return false;
        }

        private void MarkRightsAutomatic()
        {
            // 左側を打ってある右側の目印を、左側から自動で決めるものにする
            foreach (LandmarkId id in _landmarks.Keys)
            {
                if (FaceLandmarks.IsRight(id) && FaceLandmarks.TryGetMirror(id, out LandmarkId left)
                    && _landmarks.ContainsKey(left))
                {
                    _autoMirrored.Add(id);
                }
            }
        }

        private PerfectSyncLandmark MirrorOf(PerfectSyncLandmark left, Vector3 leftPoint, Vector3 planePoint,
            Vector3 planeNormal, Vector3[] positions, bool posed, LandmarkId id)
        {
            // 左側の位置を面で反転した点
            Vector3 target = FaceMeshGeometry.Reflect(leftPoint, planePoint, planeNormal);
            if (posed)
            {
                // 顔のまわりの三角形のうち、左側の面の向きを反転した向きを向く（裏側でない）面の最も近い点
                Vector3 facing = FaceMeshGeometry.ReflectDirection(_geometry.TriangleNormal(positions, left.triangle),
                    planeNormal);
                PerfectSyncLandmark mirrored = _geometry.Nearest(positions, _region, target, facing, (int)id)
                    ?? _geometry.Nearest(positions, _region, target, Vector3.zero, (int)id);
                return mirrored;
            }

            // 今の姿勢が無ければ、取り込み時の形の全体から探す
            return _geometry.Nearest(target, (int)id);
        }

        private LandmarkId? NextUnplaced()
        {
            // 今の細かさで、まだ打っていない最初の目印（左右対称なら右側は自動で決まるので飛ばす）
            foreach (LandmarkId id in ActiveLandmarks())
            {
                if (_landmarks.ContainsKey(id) || (_mirror && FaceLandmarks.IsRight(id)))
                {
                    continue;
                }

                return id;
            }

            return null;
        }

        private List<LandmarkId> ActiveLandmarks()
        {
            // 今の細かさで打つ目印（無い部位のものを除く）
            return FaceLandmarks.For(_detail, _absent);
        }

        private List<LandmarkId> MissingRequired()
        {
            // 生成に最低限必要な目印のうち、まだ打っていないもの
            List<LandmarkId> missing = FaceLandmarks.RequiredFor(_absent);
            missing.RemoveAll(_landmarks.ContainsKey);
            return missing;
        }

        private int PlacedCount(out int total)
        {
            // 今の細かさで打つ目印のうち、打った（自動で決めたものを含む）数
            List<LandmarkId> ids = ActiveLandmarks();
            total = ids.Count;
            int placed = 0;
            foreach (LandmarkId id in ids)
            {
                if (_landmarks.ContainsKey(id))
                {
                    placed++;
                }
            }

            return placed;
        }

        private bool ApplyChanges()
        {
            // 形状を作り直してメッシュに足し、顔のトラッキングの書き込み先を作り直す
            if (!Generate())
            {
                return false;
            }

            StopPreview();
            _faceDriver?.ReleasePerfectSync();
            _preview?.Restore();
            _preview = PerfectSyncMeshes.Apply(_avatar.Instance.transform, _generated);
            _faceDriver?.RebuildPerfectSync(BuildBindings());
            _dirty = false;
            return true;
        }

        private bool Generate()
        {
            // 自動生成する形状の強さ（作らない形状は 0。元からある形状は作らない）
            var strengths = new float[_shapes.Length];
            bool any = false;
            for (int i = 0; i < _shapes.Length; i++)
            {
                ShapeState shape = _shapes[i];
                strengths[i] = shape.Source == PerfectSyncSet.SourceGenerated && !shape.Native ? shape.Strength : 0f;
                any |= strengths[i] > 0f;
            }

            // 自動生成する形状が無ければ、割り当てだけ使う
            _generated = new PerfectSyncShapeData();
            _emptyShapes.Clear();
            if (!any)
            {
                return true;
            }

            // 目印の取り込み時の位置と法線から顔の座標系を作る
            var points = new Dictionary<LandmarkId, Vector3>();
            var normals = new Dictionary<LandmarkId, Vector3>();
            foreach (KeyValuePair<LandmarkId, PerfectSyncLandmark> pair in _landmarks)
            {
                if (_geometry.TryGetPoint(pair.Value, out Vector3 point))
                {
                    points[pair.Key] = point;
                }

                if (_geometry.TryGetNormal(pair.Value, out Vector3 normal))
                {
                    normals[pair.Key] = normal;
                }
            }

            FaceRig rig = FaceRig.Build(points, normals, out List<LandmarkId> missing, _absent);
            if (rig == null)
            {
                // 足りない目印を知らせ、最初の 1 つを選ぶ
                SetMessage(Loc.T("Place these points first: ", "先に次の目印を打ってください: ", "먼저 다음 표시를 찍으세요: ",
                    "请先放置以下标记: ", "請先放置以下標記: ") + string.Join(", ", missing.ConvertAll(PerfectSyncLabels.Landmark)), true);
                _selected = missing[0];
                _step = Step.Points;
                return false;
            }

            // 顔メッシュと、歯・舌のメッシュ（顔メッシュの座標系に直したもの）から作る
            var inputs = new List<PerfectSyncGenerator.MeshInput>
            {
                new PerfectSyncGenerator.MeshInput
                {
                    Path = _facePath,
                    Vertices = _geometry.Vertices,
                    Normals = _geometry.Normals,
                    Triangles = _geometry.Triangles,
                },
            };
            if (_includeMouthParts)
            {
                AddMouthParts(inputs);
            }

            _generated.Shapes.AddRange(PerfectSyncGenerator.Generate(rig, inputs, strengths));

            // 作ろうとしたのに、どのメッシュでも動く頂点が無かった形状を控える
            var made = new HashSet<string>();
            foreach (PerfectSyncShape shape in _generated.Shapes)
            {
                made.Add(shape.Name);
            }

            for (int i = 0; i < strengths.Length; i++)
            {
                if (strengths[i] > 0f && !made.Contains(ArKitFace.BlendShapeNames[i]))
                {
                    _emptyShapes.Add(i);
                }
            }

            return true;
        }

        private int CountMouthParts()
        {
            // 顔メッシュと一緒に動かせる歯・舌のメッシュの数
            Transform head = HeadBone();
            int count = 0;
            foreach (SkinnedMeshRenderer renderer in _renderers)
            {
                if (IsUsableMouthPart(renderer, head, out _))
                {
                    count++;
                }
            }

            return count;
        }

        private Transform HeadBone()
        {
            // 頭のボーン（歯・舌と顔メッシュの位置関係を、取り込み時の姿勢で求めるのに使う）
            return _avatar.IsHumanoid ? _avatar.Animator.GetBoneTransform(HumanBodyBones.Head) : null;
        }

        private bool IsUsableMouthPart(SkinnedMeshRenderer renderer, Transform head, out Matrix4x4 meshToFace)
        {
            // 顔メッシュ以外の、歯・舌らしい名前で、顔メッシュと座標系をつなげるメッシュ
            meshToFace = Matrix4x4.identity;
            return renderer != _face && IsMouthPart(renderer.name) && TryMeshToFace(renderer, head, out meshToFace);
        }

        private void AddMouthParts(List<PerfectSyncGenerator.MeshInput> inputs)
        {
            Transform head = HeadBone();
            Transform root = _avatar.Instance.transform;
            foreach (SkinnedMeshRenderer renderer in _renderers)
            {
                if (!IsUsableMouthPart(renderer, head, out Matrix4x4 meshToFace))
                {
                    continue;
                }

                // 頂点と法線を顔メッシュの座標系へ（移動量はこのメッシュの座標系へ戻す）
                Mesh mesh = renderer.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                bool hasNormals = normals.Length == vertices.Length;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] = meshToFace.MultiplyPoint3x4(vertices[i]);
                    if (hasNormals)
                    {
                        normals[i] = meshToFace.MultiplyVector(normals[i]).normalized;
                    }
                }

                inputs.Add(new PerfectSyncGenerator.MeshInput
                {
                    Path = TransformPath.Of(renderer.transform, root),
                    Vertices = vertices,
                    Normals = hasNormals ? normals : null,
                    FaceToMesh = meshToFace.inverse,
                    IsMouthPart = true,
                });
            }
        }

        private static bool IsMouthPart(string name)
        {
            // 歯・舌らしい名前か（大文字小文字は区別しない）
            string lower = name.ToLowerInvariant();
            foreach (string keyword in MouthPartKeywords)
            {
                if (lower.Contains(keyword))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryMeshToFace(SkinnedMeshRenderer part, Transform head, out Matrix4x4 meshToFace)
        {
            // 両方のメッシュが頭のボーンを使っていれば、取り込み時の姿勢（bindpose）で座標系をつなぐ
            // （このメッシュ → 頭のボーン → 顔メッシュ。今の姿勢には左右されない）
            meshToFace = Matrix4x4.identity;
            int faceBone = head != null ? Array.IndexOf(_face.bones, head) : -1;
            int partBone = head != null ? Array.IndexOf(part.bones, head) : -1;
            Matrix4x4[] faceBindposes = _face.sharedMesh.bindposes;
            Matrix4x4[] partBindposes = part.sharedMesh.bindposes;
            if (faceBone < 0 || partBone < 0 || faceBone >= faceBindposes.Length || partBone >= partBindposes.Length)
            {
                return false;
            }

            meshToFace = faceBindposes[faceBone].inverse * partBindposes[partBone];
            return true;
        }

        private List<PerfectSyncBinding> BuildBindings()
        {
            // 「既存」で BlendShape を選んだ形状の割り当て
            var bindings = new List<PerfectSyncBinding>();
            for (int i = 0; i < _shapes.Length; i++)
            {
                ShapeState shape = _shapes[i];
                if (shape.Native || shape.Source != PerfectSyncSet.SourceExisting || shape.Binding < 0
                    || shape.Binding >= _candidates.Count || shape.Strength <= 0f)
                {
                    continue;
                }

                ShapeCandidate candidate = _candidates[shape.Binding];
                bindings.Add(new PerfectSyncBinding
                {
                    name = ArKitFace.BlendShapeNames[i],
                    path = candidate.Path,
                    blendShape = candidate.Name,
                    weight = shape.Strength,
                });
            }

            return bindings;
        }

        private PerfectSyncSet BuildSet()
        {
            // 目印（番号順）
            var landmarks = new List<PerfectSyncLandmark>();
            foreach (LandmarkId id in FaceLandmarks.For(LandmarkDetail.Detailed))
            {
                if (_landmarks.TryGetValue(id, out PerfectSyncLandmark landmark))
                {
                    // 自動で決めた右側かどうかも記録する（再開後も左側に合わせて動かすため）
                    PerfectSyncLandmark copy = Copy(landmark);
                    copy.mirrored = _autoMirrored.Contains(id);
                    landmarks.Add(copy);
                }
            }

            landmarks.Sort((a, b) => a.id.CompareTo(b.id));

            // 形状の設定（元からある形状は「既存」として記録する）
            var shapes = new PerfectSyncShapeSetting[_shapes.Length];
            for (int i = 0; i < _shapes.Length; i++)
            {
                shapes[i] = new PerfectSyncShapeSetting
                {
                    name = ArKitFace.BlendShapeNames[i],
                    source = _shapes[i].Native ? PerfectSyncSet.SourceExisting : _shapes[i].Source,
                    strength = _shapes[i].Strength,
                };
            }

            return new PerfectSyncSet
            {
                sourceName = Path.GetFileName(_avatar.SourcePath),
                sourceBundleSha256 = _avatar.Manifest?.bundleSha256 ?? string.Empty,
                detail = (int)_detail,
                mirror = _mirror,
                includeMouthParts = _includeMouthParts,
                absentParts = (int)_absent,
                facePath = _facePath,
                landmarks = landmarks.ToArray(),
                shapes = shapes,
                bindings = BuildBindings().ToArray(),
            };
        }

        private void Save()
        {
            // 設定を変えていれば作り直してから保存する
            if (_dirty && !ApplyChanges())
            {
                return;
            }

            // 保存先を選ばせる（既定は元のファイルと同じフォルダの「元の名前_PerfectSync.vrcaster」）
            if (!FileDialog.IsSupported)
            {
                SetMessage(Loc.T("The save dialog is not available on this system.", "この環境では保存の画面を開けません。",
                    "이 환경에서는 저장 화면을 열 수 없습니다.", "此环境无法打开保存窗口。", "此環境無法開啟儲存視窗。"), true);
                return;
            }

            string suggested = PerfectSyncPackageWriter.DefaultOutputPath(_avatar.SourcePath);
            string path = FileDialog.SaveFile(
                Loc.T("Save the perfect sync version", "パーフェクトシンク版を保存", "퍼펙트 싱크 버전 저장",
                    "保存完美同步版本", "儲存完美同步版本"),
                "VRCast avatar", AvatarPackageLayout.Extension, suggested, Path.GetFileName(suggested));
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                // 元のファイルはそのままに、別名で保存する
                PerfectSyncPackageWriter.Save(_avatar.SourcePath, path, BuildSet(), _generated ?? new PerfectSyncShapeData());
                _savedPath = path;
                SetMessage(Loc.T("Saved: ", "保存しました: ", "저장했습니다: ", "已保存: ", "已儲存: ") + Path.GetFileName(path), false);
                VRCastLog.Info(LogCategory, $"Saved perfect sync avatar: {path}");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException
                                      || e is InvalidOperationException || e is InvalidDataException)
            {
                // 同じファイルを選んだ・書き込めない・内容が不正
                SetMessage(Loc.T("Could not save: ", "保存できませんでした: ", "저장하지 못했습니다: ", "无法保存: ", "無法儲存: ")
                    + e.Message, true);
                VRCastLog.Warning(LogCategory, $"Failed to save perfect sync avatar: {e.Message}");
            }
        }

        private void LoadSaved()
        {
            // 編集を閉じてから、保存したファイルを読み込む
            string path = _savedPath;
            Close();
            if (!string.IsNullOrEmpty(path))
            {
                _session.Load(path);
            }
        }

        private void SelectPreview(int index)
        {
            // 前の形状を元に戻してから、試す形状を替える（スライダーで試すならトラッキングで試すのはやめる）
            StopPreview();
            if (index >= 0)
            {
                SetTrackingTest(false);
            }

            _previewIndex = index;
        }

        private void WritePreview()
        {
            if (_previewIndex < 0)
            {
                return;
            }

            // 書き込み先は形状を足した後に探す（足す前に探すと新しい形状が見つからない）
            _previewTargets ??= FindPreviewTargets(_previewIndex);
            foreach ((BlendShapeOverlay overlay, float scale) in _previewTargets)
            {
                overlay.Write(_previewWeight * scale);
            }
        }

        private void StopPreview()
        {
            // 試していた形状の上乗せをやめる（値は書き込み先を探し直すまで 0 に戻す）
            if (_previewTargets != null)
            {
                foreach ((BlendShapeOverlay overlay, float _) in _previewTargets)
                {
                    overlay.Write(0f);
                }
            }

            _previewTargets = null;
        }

        private List<(BlendShapeOverlay, float)> FindPreviewTargets(int index)
        {
            // ARKit の名前（表記ゆれを含む）の BlendShape と、「既存」で割り当てた BlendShape
            var targets = new List<(BlendShapeOverlay, float)>();
            Transform root = _avatar.Instance.transform;
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = renderer.sharedMesh;
                for (int i = 0; mesh != null && i < mesh.blendShapeCount; i++)
                {
                    if (PerfectSyncBlendShapes.Match(mesh.GetBlendShapeName(i)) == index)
                    {
                        targets.Add((BlendShapeOverlay.Create(renderer, i), 1f));
                    }
                }
            }

            ShapeState shape = _shapes[index];
            if (!shape.Native && shape.Source == PerfectSyncSet.SourceExisting && shape.Binding >= 0
                && shape.Binding < _candidates.Count)
            {
                ShapeCandidate candidate = _candidates[shape.Binding];
                BlendShapeOverlay overlay = BlendShapeOverlay.Create(root, candidate.Path, candidate.Name);
                if (overlay != null)
                {
                    targets.Add((overlay, shape.Strength));
                }
            }

            return targets;
        }

        private void SetMessage(string message, bool error)
        {
            _message = message;
            _messageIsError = error;
        }

        private static PerfectSyncLandmark Copy(PerfectSyncLandmark landmark)
        {
            return new PerfectSyncLandmark
            {
                id = landmark.id,
                triangle = landmark.triangle,
                u = landmark.u,
                v = landmark.v,
                mirrored = landmark.mirrored,
            };
        }
    }
}
