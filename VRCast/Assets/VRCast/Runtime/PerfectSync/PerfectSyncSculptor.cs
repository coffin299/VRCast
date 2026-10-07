using System;
using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Avatars;
using VRCast.Cameras;
using VRCast.Core;
using VRCast.Tracking;

namespace VRCast.PerfectSync
{
    /// <summary>
    /// パーフェクトシンクの作成モード本体。顔のアップにカメラを合わせ、顔のトラッキングを止めて表情を初期値に固定し、
    /// 選んだ ARKit 名の形状を頂点ブラシで作る。元に戻す・やり直し、既存の BlendShape の合成、左右反転コピー、保存を扱う。
    /// アバターに付けて使い、Close で外す（BlendShapeLimiter より先に動かして、上限の適用を上書きしないようにする）。
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder)]
    public sealed class PerfectSyncSculptor : MonoBehaviour
    {
        // 実行順（BlendShapeLimiter の 10000 より前）
        private const int ExecutionOrder = 9990;

        // ブラシの半径の範囲・初期値（m）と、[ ] キー 1 回の倍率
        public const float MinRadius = 0.002f;
        public const float MaxRadius = 0.05f;
        private const float DefaultRadius = 0.012f;
        private const float RadiusStep = 1.15f;

        // 元に戻せる回数
        private const int MaxUndo = 30;

        // 膨らませる速さ（強さ 1 で 1 秒あたり半径の何倍か）と、なめらか・元に戻すの寄せる速さ（1 秒あたり）
        private const float InflateSpeed = 0.5f;
        private const float SmoothRate = 12f;
        private const float EraseRate = 6f;

        // 顔のアップの位置（首の長さに対する倍率）と、首が見つからないときの長さ（m）
        private const float FaceDistanceRatio = 4f;
        private const float FaceUpRatio = 0.6f;
        private const float FaceForwardRatio = 0.4f;
        private const float DefaultNeckLength = 0.1f;
        private const float MinNeckLength = 0.02f;

        // 正面を向くカメラの水平角（OrbitCameraController.FrontYaw と同じ）
        private const float FrontYaw = 180f;

        // カーソルの輪のテクスチャの大きさ・線の太さ（px）と色
        private const int CursorTextureSize = 128;
        private const float CursorLineWidth = 2.5f;
        private static readonly Color ActiveCursorColor = new Color(0.4f, 0.85f, 1f, 0.9f);
        private static readonly Color InactiveCursorColor = new Color(0.6f, 0.6f, 0.6f, 0.6f);

        private static readonly int ShapeTotal = ArKitFace.BlendShapeNames.Length;

        /// <summary>
        /// 元に戻すための 1 つの形状の控え（メッシュ番号・形状・差分。null = 差分無し）。
        /// </summary>
        private sealed class Snapshot
        {
            public int Mesh;
            public int Shape;
            public Vector3[] Deltas;
        }

        private readonly List<List<Snapshot>> _undo = new List<List<Snapshot>>();
        private readonly List<List<Snapshot>> _redo = new List<List<Snapshot>>();

        // ブラシ 1 回分の変化（代表頂点 → 差分の変化）と、左右対称にしたもの
        private readonly List<KeyValuePair<int, float>> _region = new List<KeyValuePair<int, float>>();
        private readonly Dictionary<int, Vector3> _changes = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> _symmetric = new Dictionary<int, Vector3>();

        // つまむブラシ: 押したときの差分・位置・動かす平面と、頂点ごとのワールド → メッシュの行列
        private readonly Dictionary<int, Matrix4x4> _grabInverse = new Dictionary<int, Matrix4x4>();
        private Vector3[] _grabStart;
        private Vector3 _grabOrigin;
        private Plane _grabPlane;

        private CustomPerfectSync _custom;
        private FaceTrackingDriver _face;
        private OrbitCameraController _orbit;
        private Camera _camera;
        private Animator _animator;
        private Transform _head;
        private SculptMeshData[] _data;
        private CameraPose _savedPose;
        private float _radius = DefaultRadius;

        // ブラシで編集中のメッシュ（-1 = 編集中でない）
        private int _strokeMesh = -1;

        // マウスの下の顔の位置（カーソル表示・ブラシに使う）
        private bool _hovering;
        private int _hoverMesh;
        private Vector3 _hoverPoint;

        private Texture2D _cursor;

        // 形状ごとの「差分がある」の控え（UI で毎回全頂点を調べないよう、変えた形状だけ調べ直す）
        private readonly bool[] _shapeKnown = new bool[ShapeTotal];
        private readonly bool[] _shapeHas = new bool[ShapeTotal];

        /// <summary>
        /// 作成モード中なら true（視点の記録を止めるため）。
        /// </summary>
        public static bool IsOpen { get; private set; }

        /// <summary>
        /// 編集中の ARKit 名の位置（ArKitFace.BlendShapeNames 内）。
        /// </summary>
        public int Shape { get; private set; }

        /// <summary>
        /// 編集中の形状を表示する強さ（0〜1。1 のときだけブラシを使える）。
        /// </summary>
        public float PreviewWeight { get; private set; } = 1f;

        public SculptBrush Brush { get; set; } = SculptBrush.Grab;

        /// <summary>
        /// ブラシの半径（m）。
        /// </summary>
        public float Radius
        {
            get => _radius;
            set => _radius = Mathf.Clamp(value, MinRadius, MaxRadius);
        }

        /// <summary>
        /// ブラシの強さ（0〜1）。
        /// </summary>
        public float Strength { get; set; } = 0.5f;

        /// <summary>
        /// 左右対称に編集するなら true。
        /// </summary>
        public bool Symmetry { get; set; } = true;

        /// <summary>
        /// 顔のトラッキングで試している間は true（ブラシは使えない）。
        /// </summary>
        public bool TrackingPreview { get; private set; }

        /// <summary>
        /// 保存していない変更があれば true。
        /// </summary>
        public bool IsDirty { get; private set; }

        public bool CanUndo => _undo.Count > 0;

        public bool CanRedo => _redo.Count > 0;

        public CustomPerfectSync Custom => _custom;

        /// <summary>
        /// 今ブラシで編集できるなら true（試している間・弱めて表示中・元から持っている名前は不可）。
        /// </summary>
        public bool CanSculpt => !TrackingPreview && PreviewWeight >= 0.999f && !_custom.IsNative(Shape);

        /// <summary>
        /// 形状に差分があれば true（控えた結果を使う）。
        /// </summary>
        public bool HasShape(int shape)
        {
            // 変えた形状だけ調べ直す
            if (!_shapeKnown[shape])
            {
                _shapeHas[shape] = _custom.HasShape(shape);
                _shapeKnown[shape] = true;
            }

            return _shapeHas[shape];
        }

        /// <summary>
        /// 作った形状の数（ARKit 名の種類数）。
        /// </summary>
        public int ShapeCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < ShapeTotal; i++)
                {
                    count += HasShape(i) ? 1 : 0;
                }

                return count;
            }
        }

        public void Initialize(CustomPerfectSync custom, FaceTrackingDriver face, OrbitCameraController orbit, Animator animator)
        {
            _custom = custom;
            _face = face;
            _orbit = orbit;
            _camera = orbit != null ? orbit.GetComponent<Camera>() : Camera.main;
            _animator = animator;
            _head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            _data = new SculptMeshData[custom.Meshes.Count];
            IsOpen = true;

            // 顔のトラッキングを止めて、表情を初期値に固定する
            if (_face != null)
            {
                _face.Suspended = true;
            }

            // 抜けるときに戻すカメラを控えて、顔のアップにする
            if (_orbit != null)
            {
                _savedPose = _orbit.Pose;
            }

            FrameFace();

            // 最初は元から持っていない最初の ARKit 名を選ぶ
            Shape = 0;
            for (int i = 0; i < ShapeTotal; i++)
            {
                if (!custom.IsNative(i))
                {
                    Shape = i;
                    break;
                }
            }

            ShowPreview();
        }

        /// <summary>
        /// カメラを顔のアップ（正面）に戻す。
        /// </summary>
        public void FrameFace()
        {
            // Head が無い（Humanoid でない）アバターはカメラを動かさない
            if (_orbit == null || _head == null)
            {
                return;
            }

            // 首の長さを顔の大きさの目安にする
            Transform neck = _animator.GetBoneTransform(HumanBodyBones.Neck);
            float length = neck != null ? Vector3.Distance(neck.position, _head.position) : DefaultNeckLength;
            length = Mathf.Max(length, MinNeckLength);

            // 頭の少し上・前を注視点にして、アバターの正面から見る
            Transform root = _custom.transform;
            CameraPose pose = _orbit.Pose;
            pose.target = _head.position + root.up * (length * FaceUpRatio) + root.forward * (length * FaceForwardRatio);
            pose.distance = length * FaceDistanceRatio;
            pose.yaw = FrontYaw + root.eulerAngles.y;
            pose.pitch = 0f;
            _orbit.SetPose(pose);
        }

        /// <summary>
        /// 編集する形状を選ぶ。
        /// </summary>
        public void SelectShape(int shape)
        {
            // 範囲外・同じ形状なら何もしない
            if (shape < 0 || shape >= ShapeTotal || shape == Shape)
            {
                return;
            }

            EndStroke();
            Shape = shape;
            ShowPreview();
        }

        /// <summary>
        /// 編集中の形状を表示する強さを変える。
        /// </summary>
        public void SetPreviewWeight(float weight)
        {
            PreviewWeight = Mathf.Clamp01(weight);
            ShowPreview();
        }

        /// <summary>
        /// 顔のトラッキングで試す（作った形状を BlendShape にして、トラッキングを再開する）。
        /// </summary>
        public void SetTrackingPreview(bool enabled)
        {
            // 変わらなければ何もしない
            if (enabled == TrackingPreview)
            {
                return;
            }

            EndStroke();
            TrackingPreview = enabled;
            _hovering = false;
            if (enabled)
            {
                // 形状を BlendShape にしてからトラッキングを再開する
                Commit();
                if (_face != null)
                {
                    _face.Suspended = false;
                }
            }
            else
            {
                // トラッキングを止めて頂点での表示に戻す
                if (_face != null)
                {
                    _face.Suspended = true;
                }

                ShowPreview();
            }
        }

        /// <summary>
        /// 作った形状を BlendShape としてメッシュに反映する（パーフェクトシンク・上限の一覧も作り直す）。
        /// </summary>
        public void Commit()
        {
            EndStroke();
            _custom.RebuildAll();

            // 作り直すとメッシュが変わるため、ブラシ用の情報の行列等は次の Bake で取り直される
            if (!TrackingPreview)
            {
                ShowPreview();
            }
        }

        /// <summary>
        /// 既存の BlendShape（元のメッシュのもの）を、編集中の形状に weight（0〜1）の割合で足す。
        /// </summary>
        public bool MixExisting(int meshIndex, int blendShapeIndex, float weight)
        {
            // 範囲外・元から持っている名前には足さない
            if (meshIndex < 0 || meshIndex >= _custom.Meshes.Count || _custom.IsNative(Shape))
            {
                return false;
            }

            EditableFaceMesh mesh = _custom.Meshes[meshIndex];
            if (blendShapeIndex < 0 || blendShapeIndex >= mesh.OriginalShapeCount)
            {
                return false;
            }

            // 最後のフレーム（最大の形）を読み、その重みで 1.0 あたりの差分にする
            Mesh original = mesh.Original;
            int frame = original.GetBlendShapeFrameCount(blendShapeIndex) - 1;
            if (frame < 0)
            {
                return false;
            }

            var vertices = new Vector3[mesh.VertexCount];
            original.GetBlendShapeFrameVertices(blendShapeIndex, frame, vertices, null, null);
            float frameWeight = original.GetBlendShapeFrameWeight(blendShapeIndex, frame);
            float scale = frameWeight > 0f ? weight * 100f / frameWeight : weight;

            // 元に戻せるように控えてから足す
            EndStroke();
            RecordUndo(new List<Snapshot> { Capture(meshIndex, Shape) });
            Vector3[] deltas = mesh.GetOrCreateDeltas(Shape);
            for (int i = 0; i < deltas.Length; i++)
            {
                deltas[i] += vertices[i] * scale;
            }

            Invalidate(Shape);
            IsDirty = true;
            ShowPreview();
            return true;
        }

        /// <summary>
        /// 左右反対の ARKit 名（例: eyeBlinkLeft → eyeBlinkRight）へ、左右を反転した形状を写す（写し先は置き換え）。
        /// 写せなければ false（左右の無い名前・写し先が元から持っている名前）。
        /// </summary>
        public bool MirrorCopy()
        {
            // 左右の組になる名前を求める（左右の無い名前は自分自身）
            int target = OppositeShape(Shape);
            if (target < 0 || _custom.IsNative(target))
            {
                return false;
            }

            EndStroke();

            // 写し元・写し先のどちらかに差分のあるメッシュを控える
            var snapshots = new List<Snapshot>();
            for (int i = 0; i < _custom.Meshes.Count; i++)
            {
                EditableFaceMesh mesh = _custom.Meshes[i];
                if (mesh.GetDeltas(Shape) != null || mesh.GetDeltas(target) != null)
                {
                    snapshots.Add(Capture(i, target));
                }
            }

            if (snapshots.Count == 0)
            {
                return false;
            }

            RecordUndo(snapshots);
            foreach (Snapshot snapshot in snapshots)
            {
                // 写し元に差分が無ければ写し先を消す
                EditableFaceMesh mesh = _custom.Meshes[snapshot.Mesh];
                Vector3[] source = mesh.GetDeltas(Shape);
                if (source == null || !mesh.HasDeltas(Shape))
                {
                    mesh.SetDeltas(target, null);
                    continue;
                }

                // 代表頂点ごとに対称の相手へ反転した差分を写す（同じ位置の頂点にも同じ値）
                SculptMeshData data = Data(snapshot.Mesh);
                MirrorResult mirror = data.Mirror;
                var mirrored = new Vector3[mesh.VertexCount];
                for (int v = 0; v < data.Rep.Length; v++)
                {
                    int partner = data.Rep[v] == v ? mirror.Partners[v] : -1;
                    if (partner < 0)
                    {
                        continue;
                    }

                    Vector3 value = mirror.MirrorVector(source[v]);
                    foreach (int member in data.Members[partner])
                    {
                        mirrored[member] = value;
                    }
                }

                mesh.SetDeltas(target, mirrored);
            }

            Invalidate(target);
            IsDirty = true;
            ShowPreview();
            return true;
        }

        /// <summary>
        /// 左右反対の ARKit 名の位置（左右の無い名前は -1）。
        /// </summary>
        public static int OppositeShape(int shape)
        {
            // 鏡像 OFF の受信値の対応は左右の入れ替えなので、それを流用する
            int opposite = PerfectSyncBlendShapes.SourceIndex(shape, false);
            return opposite != shape ? opposite : -1;
        }

        /// <summary>
        /// 編集中の形状を消す。
        /// </summary>
        public void ClearShape()
        {
            EndStroke();

            // 差分のあるメッシュだけを控えて消す
            var snapshots = new List<Snapshot>();
            for (int i = 0; i < _custom.Meshes.Count; i++)
            {
                if (_custom.Meshes[i].GetDeltas(Shape) != null)
                {
                    snapshots.Add(Capture(i, Shape));
                }
            }

            if (snapshots.Count == 0)
            {
                return;
            }

            RecordUndo(snapshots);
            foreach (Snapshot snapshot in snapshots)
            {
                _custom.Meshes[snapshot.Mesh].SetDeltas(Shape, null);
            }

            Invalidate(Shape);
            IsDirty = true;
            ShowPreview();
        }

        /// <summary>
        /// 別の .vrcaster の形状を読み込む（同じメッシュのものだけ。含まれる形状を置き換える）。当てた形状の数を返す。
        /// </summary>
        public int Import(PerfectSyncData data)
        {
            // 空なら何もしない
            if (data == null || data.IsEmpty)
            {
                return 0;
            }

            EndStroke();

            // 置き換わる可能性のある形状を全メッシュ分控える
            var snapshots = new List<Snapshot>();
            foreach (PerfectSyncShapeData shape in data.Shapes)
            {
                int index = Array.IndexOf(ArKitFace.BlendShapeNames, shape.Name);
                for (int i = 0; index >= 0 && i < _custom.Meshes.Count; i++)
                {
                    snapshots.Add(Capture(i, index));
                }
            }

            int applied = _custom.Apply(data);
            if (applied > 0)
            {
                RecordUndo(snapshots);
                IsDirty = true;
                foreach (Snapshot snapshot in snapshots)
                {
                    Invalidate(snapshot.Shape);
                }
            }

            ShowPreview();
            return applied;
        }

        /// <summary>
        /// 作った形状を読み込んだ .vrcaster へ保存する（初回は元のファイルを .bak に控える）。控えのパスを返す（控えなければ null）。
        /// 失敗したら例外を投げる。
        /// </summary>
        public string Save()
        {
            // 形状を BlendShape にしてから書き出す
            Commit();
            var shapes = new List<PerfectSyncShape>();
            PerfectSyncSet set = _custom.Export(shapes);
            string backup = AvatarPackageWriter.ReplacePerfectSync(_custom.SourcePath, set, shapes);
            IsDirty = false;
            return backup;
        }

        public void Undo()
        {
            // 元に戻す操作の控えを今の状態と入れ替えて、やり直し側へ積む
            if (!CanUndo)
            {
                return;
            }

            EndStroke();
            List<Snapshot> action = Pop(_undo);
            _redo.Add(Swap(action));
            AfterHistory(action);
        }

        public void Redo()
        {
            // やり直しの控えを今の状態と入れ替えて、元に戻す側へ積む
            if (!CanRedo)
            {
                return;
            }

            EndStroke();
            List<Snapshot> action = Pop(_redo);
            _undo.Add(Swap(action));
            AfterHistory(action);
        }

        /// <summary>
        /// 作成モードを抜ける（作った形状を BlendShape にし、カメラを戻して自分を外す）。保存はしない。
        /// </summary>
        public void Close()
        {
            EndStroke();
            foreach (EditableFaceMesh mesh in _custom.Meshes)
            {
                mesh.EndPreview();
            }

            _custom.RebuildAll();
            if (_orbit != null)
            {
                _orbit.SetPose(_savedPose);
            }

            Destroy(this);
        }

        private void LateUpdate()
        {
            // 試している間はトラッキングに任せる
            if (_custom == null || TrackingPreview)
            {
                _hovering = false;
                return;
            }

            HoldWeights();
            HandleShortcuts();

            // UI の上ではブラシ・カーソルを使わない（編集中は続ける）
            bool overUi = _orbit != null && _orbit.InputBlocked;
            bool stroking = _strokeMesh >= 0;
            if (overUi && !stroking)
            {
                _hovering = false;
                return;
            }

            UpdateHover();

            // 押している間はブラシを当て続け、離したら終える
            if (stroking)
            {
                if (Input.GetMouseButton(0))
                {
                    ContinueStroke();
                }
                else
                {
                    EndStroke();
                }
            }
            else if (Input.GetMouseButtonDown(0) && CanSculpt)
            {
                BeginStroke();
            }
        }

        private void HoldWeights()
        {
            foreach (EditableFaceMesh mesh in _custom.Meshes)
            {
                SkinnedMeshRenderer renderer = mesh.Renderer;
                if (renderer == null || renderer.sharedMesh == null)
                {
                    continue;
                }

                // 元の BlendShape は読み込み時の値に固定する（まばたき・表情で形を変えない）
                int count = Mathf.Min(mesh.OriginalShapeCount, renderer.sharedMesh.blendShapeCount);
                for (int i = 0; i < count; i++)
                {
                    renderer.SetBlendShapeWeight(i, mesh.NeutralWeights[i]);
                }

                // 作った形状の BlendShape は 0（編集中の形状は頂点で表示している）
                for (int shape = 0; shape < ShapeTotal; shape++)
                {
                    int index = mesh.WorkingShapeIndex(shape);
                    if (index >= 0)
                    {
                        renderer.SetBlendShapeWeight(index, 0f);
                    }
                }
            }
        }

        private void HandleShortcuts()
        {
            // テキスト入力中はショートカットを使わない
            if (GUIUtility.keyboardControl != 0)
            {
                return;
            }

            // Ctrl+Z で元に戻す、Ctrl+Shift+Z / Ctrl+Y でやり直す
            bool control = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (control && Input.GetKeyDown(KeyCode.Z))
            {
                if (shift)
                {
                    Redo();
                }
                else
                {
                    Undo();
                }
            }
            else if (control && Input.GetKeyDown(KeyCode.Y))
            {
                Redo();
            }

            // [ ] でブラシの半径を変える
            if (Input.GetKeyDown(KeyCode.LeftBracket))
            {
                Radius /= RadiusStep;
            }
            else if (Input.GetKeyDown(KeyCode.RightBracket))
            {
                Radius *= RadiusStep;
            }
        }

        private void UpdateHover()
        {
            _hovering = false;
            if (_camera == null)
            {
                return;
            }

            // 表示中のメッシュの今の形を取り直し、マウスの光線と最も近い交点を探す
            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            float nearest = float.MaxValue;
            for (int i = 0; i < _data.Length; i++)
            {
                SculptMeshData data = Data(i);
                if (!data.IsVisible)
                {
                    continue;
                }

                data.Bake();
                if (data.Raycast(ray, out float distance, out Vector3 point) && distance < nearest)
                {
                    nearest = distance;
                    _hovering = true;
                    _hoverMesh = i;
                    _hoverPoint = point;
                }
            }
        }

        private void BeginStroke()
        {
            // 顔の上で押したときだけ始める
            if (!_hovering)
            {
                return;
            }

            _strokeMesh = _hoverMesh;
            SculptMeshData data = Data(_strokeMesh);
            RecordUndo(new List<Snapshot> { Capture(_strokeMesh, Shape) });
            Vector3[] deltas = data.Mesh.GetOrCreateDeltas(Shape);
            IsDirty = true;

            // つまむブラシは押したときの範囲・差分・平面を固定する
            if (Brush == SculptBrush.Grab)
            {
                _grabStart = (Vector3[])deltas.Clone();
                _grabOrigin = _hoverPoint;
                _grabPlane = new Plane(-_camera.transform.forward, _hoverPoint);
                data.CollectInRadius(_hoverPoint, Radius, _region);
                _grabInverse.Clear();
                foreach (KeyValuePair<int, float> pair in _region)
                {
                    _grabInverse[pair.Key] = data.SkinMatrix(pair.Key).inverse;
                }
            }
        }

        private void ContinueStroke()
        {
            SculptMeshData data = Data(_strokeMesh);
            EditableFaceMesh mesh = data.Mesh;
            Vector3[] deltas = mesh.GetOrCreateDeltas(Shape);
            _changes.Clear();
            if (Brush == SculptBrush.Grab)
            {
                // マウスの光線と押した位置の平面の交点までの移動量を、影響度を掛けてメッシュ空間へ
                Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
                if (!_grabPlane.Raycast(ray, out float enter))
                {
                    return;
                }

                Vector3 move = ray.GetPoint(enter) - _grabOrigin;
                foreach (KeyValuePair<int, float> pair in _region)
                {
                    _changes[pair.Key] = _grabInverse[pair.Key].MultiplyVector(move * pair.Value);
                }

                // 押したときの差分に移動量を足した値にする（毎フレーム同じ頂点を書き換える）
                Symmetrize(data.Mirror);
                foreach (KeyValuePair<int, Vector3> pair in _symmetric)
                {
                    foreach (int member in data.Members[pair.Key])
                    {
                        deltas[member] = _grabStart[member] + pair.Value;
                    }
                }
            }
            else
            {
                // 同じメッシュの上にカーソルがある間だけ当てる
                if (!_hovering || _hoverMesh != _strokeMesh)
                {
                    return;
                }

                data.CollectInRadius(_hoverPoint, Radius, _region);
                CollectContinuousChanges(data, deltas, Time.deltaTime);
                Symmetrize(data.Mirror);
                foreach (KeyValuePair<int, Vector3> pair in _symmetric)
                {
                    foreach (int member in data.Members[pair.Key])
                    {
                        deltas[member] += pair.Value;
                    }
                }
            }

            Invalidate(Shape);
            mesh.ShowPreview(Shape, PreviewWeight);
        }

        private void CollectContinuousChanges(SculptMeshData data, Vector3[] deltas, float deltaTime)
        {
            EditableFaceMesh mesh = data.Mesh;
            Vector3[] normals = mesh.BaseNormals;

            // 押し出す量（m）と、寄せる割合（フレームレートに依らないよう指数で）
            float push = Strength * Radius * InflateSpeed * deltaTime;
            float smooth = 1f - Mathf.Exp(-SmoothRate * Strength * deltaTime);
            float erase = 1f - Mathf.Exp(-EraseRate * Strength * deltaTime);
            foreach (KeyValuePair<int, float> pair in _region)
            {
                int vertex = pair.Key;
                float influence = pair.Value;
                switch (Brush)
                {
                    case SculptBrush.Inflate:
                    case SculptBrush.Deflate:
                    {
                        // 元の法線方向へ、ワールドでの長さが push になるように押し出す（拡大率を打ち消す）
                        Vector3 normal = normals[vertex];
                        float worldLength = Mathf.Max(data.SkinMatrix(vertex).MultiplyVector(normal).magnitude, 1e-6f);
                        float sign = Brush == SculptBrush.Inflate ? 1f : -1f;
                        _changes[vertex] = normal * (sign * push * influence / worldLength);
                        break;
                    }

                    case SculptBrush.Smooth:
                    {
                        // 隣の頂点の差分の平均へ寄せる（隣が無ければ何もしない）
                        int[] neighbors = data.Neighbors[vertex];
                        if (neighbors == null || neighbors.Length == 0)
                        {
                            break;
                        }

                        Vector3 sum = Vector3.zero;
                        foreach (int neighbor in neighbors)
                        {
                            sum += deltas[neighbor];
                        }

                        _changes[vertex] = (sum / neighbors.Length - deltas[vertex]) * (smooth * influence);
                        break;
                    }

                    case SculptBrush.Erase:
                        // 差分を 0 へ寄せる
                        _changes[vertex] = -deltas[vertex] * (erase * influence);
                        break;
                }
            }
        }

        private void Symmetrize(MirrorResult mirror)
        {
            _symmetric.Clear();

            // 対称にしない・相手が見つからないメッシュはそのまま
            if (!Symmetry || mirror == null || mirror.Matched == 0)
            {
                foreach (KeyValuePair<int, Vector3> pair in _changes)
                {
                    _symmetric[pair.Key] = pair.Value;
                }

                return;
            }

            foreach (KeyValuePair<int, Vector3> pair in _changes)
            {
                int vertex = pair.Key;
                int partner = mirror.Partners[vertex];
                if (partner < 0)
                {
                    // 相手が無い頂点はそのまま
                    _symmetric[vertex] = pair.Value;
                }
                else if (partner == vertex)
                {
                    // 対称面上の頂点は面に沿う成分だけにする
                    _symmetric[vertex] = (pair.Value + mirror.MirrorVector(pair.Value)) * 0.5f;
                }
                else if (_changes.TryGetValue(partner, out Vector3 other))
                {
                    // 両側とも範囲内なら、自分と相手の反転の平均にする
                    _symmetric[vertex] = (pair.Value + mirror.MirrorVector(other)) * 0.5f;
                }
                else
                {
                    // 片側だけなら相手へ反転して写す
                    _symmetric[vertex] = pair.Value;
                    _symmetric[partner] = mirror.MirrorVector(pair.Value);
                }
            }
        }

        private void EndStroke()
        {
            _strokeMesh = -1;
            _grabStart = null;
            _grabInverse.Clear();
        }

        private void ShowPreview()
        {
            // 試している間は BlendShape での表示のまま
            if (TrackingPreview)
            {
                return;
            }

            // 差分のあるメッシュだけ頂点で表示し、他は元の形へ
            foreach (EditableFaceMesh mesh in _custom.Meshes)
            {
                if (mesh.GetDeltas(Shape) != null)
                {
                    mesh.ShowPreview(Shape, PreviewWeight);
                }
                else
                {
                    mesh.EndPreview();
                }
            }
        }

        private SculptMeshData Data(int index)
        {
            // ブラシ用の情報は初めて使うときに作る（大きなメッシュは時間がかかる）
            return _data[index] ?? (_data[index] = SculptMeshData.Build(_custom.Meshes[index], _head));
        }

        private Snapshot Capture(int mesh, int shape)
        {
            // 今の差分の複製を控える
            Vector3[] deltas = _custom.Meshes[mesh].GetDeltas(shape);
            return new Snapshot { Mesh = mesh, Shape = shape, Deltas = (Vector3[])deltas?.Clone() };
        }

        private void RecordUndo(List<Snapshot> action)
        {
            // 新しい操作をしたらやり直しは消す
            _undo.Add(action);
            if (_undo.Count > MaxUndo)
            {
                _undo.RemoveAt(0);
            }

            _redo.Clear();
        }

        private static List<Snapshot> Pop(List<List<Snapshot>> stack)
        {
            List<Snapshot> action = stack[stack.Count - 1];
            stack.RemoveAt(stack.Count - 1);
            return action;
        }

        private List<Snapshot> Swap(List<Snapshot> action)
        {
            // 控えの差分を当て、今の差分を逆方向の控えにする（配列はそのまま受け渡す）
            var reverse = new List<Snapshot>(action.Count);
            foreach (Snapshot snapshot in action)
            {
                EditableFaceMesh mesh = _custom.Meshes[snapshot.Mesh];
                reverse.Add(new Snapshot { Mesh = snapshot.Mesh, Shape = snapshot.Shape, Deltas = mesh.GetDeltas(snapshot.Shape) });
                mesh.SetDeltas(snapshot.Shape, snapshot.Deltas);
                Invalidate(snapshot.Shape);
            }

            return reverse;
        }

        private void Invalidate(int shape)
        {
            // 次に HasShape を呼んだときに調べ直す
            _shapeKnown[shape] = false;
        }

        private void AfterHistory(List<Snapshot> action)
        {
            // 変わった形状を表示する
            if (action.Count > 0)
            {
                Shape = action[0].Shape;
            }

            IsDirty = true;
            ShowPreview();
        }

        private void OnGUI()
        {
            // 他の UI より手前に、ブラシの範囲の輪を描く
            GUI.depth = 1;
            if (!_hovering || _camera == null || Event.current.type != EventType.Repaint)
            {
                return;
            }

            Vector3 center = _camera.WorldToScreenPoint(_hoverPoint);
            if (center.z <= 0f)
            {
                return;
            }

            // 半径をカメラの右方向で画面上の長さへ
            Vector3 edge = _camera.WorldToScreenPoint(_hoverPoint + _camera.transform.right * Radius);
            float pixels = Mathf.Max(Vector2.Distance(center, edge), 2f);
            var rect = new Rect(center.x - pixels, Screen.height - center.y - pixels, pixels * 2f, pixels * 2f);
            Color previous = GUI.color;
            GUI.color = CanSculpt ? ActiveCursorColor : InactiveCursorColor;
            GUI.DrawTexture(rect, CursorTexture());
            GUI.color = previous;
        }

        private Texture2D CursorTexture()
        {
            // 白い輪のテクスチャを初回だけ作る（色は GUI.color で付ける）
            if (_cursor != null)
            {
                return _cursor;
            }

            _cursor = new Texture2D(CursorTextureSize, CursorTextureSize, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
            float middle = (CursorTextureSize - 1) * 0.5f;
            float ring = middle - CursorLineWidth;
            var pixels = new Color32[CursorTextureSize * CursorTextureSize];
            for (int y = 0; y < CursorTextureSize; y++)
            {
                for (int x = 0; x < CursorTextureSize; x++)
                {
                    // 輪からの距離で不透明度を決める（縁はなめらかに）
                    float distance = Mathf.Sqrt((x - middle) * (x - middle) + (y - middle) * (y - middle));
                    float alpha = Mathf.Clamp01(1f - Mathf.Abs(distance - ring) / CursorLineWidth);
                    pixels[y * CursorTextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
                }
            }

            _cursor.SetPixels32(pixels);
            _cursor.Apply();
            return _cursor;
        }

        private void OnDestroy()
        {
            // トラッキングを再開し、ブラシ用の一時データを破棄する
            IsOpen = false;
            if (_face != null)
            {
                _face.Suspended = false;
            }

            if (_data != null)
            {
                foreach (SculptMeshData data in _data)
                {
                    data?.Dispose();
                }
            }

            if (_cursor != null)
            {
                Destroy(_cursor);
            }
        }
    }
}
