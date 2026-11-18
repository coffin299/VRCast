using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;
using VRCast.PerfectSync;
using VRCast.Platform;
using VRCast.Tracking;

namespace VRCast.UI
{
    /// <summary>
    /// 目印の自動配置（パーフェクトシンク化の編集画面）。
    /// 「BlendShape から」はまばたき・口・眉の BlendShape で動く頂点から（LandmarkEstimator）、
    /// 「画像認識」は今の視点の顔を、トゥーンを外した認識用の見た目で画像にして同梱の MediaPipe トラッカー（--face-image）で点を求め、顔の肌へ光線を当てる。
    /// 「BlendShape + 画像認識」は BlendShape から点を打ち、その点に合わせて補正した画像認識の点（ImageCorrection）で
    /// BlendShape では求められない目印（頬・小鼻・エラ・比率で推定しただけの目印など）を補う。
    /// どれも打っていない目印だけを置き（手で打った目印は変えない）、置く前の状態を履歴に積む。
    /// </summary>
    public sealed partial class PerfectSyncEditor
    {
        // 画像認識を待つ最長時間（秒。トラッカーの起動とモデルの読み込みを含む）
        private const float DetectionTimeout = 60f;

        // 画像認識に渡す画像の長辺の上限（画素）と、出力を読み終えるのを待つ時間（ミリ秒）
        private const int DetectionMaxSize = 1280;
        private const int DetectionOutputWait = 1000;

        // 顔を切り出して描くときの最大の拡大率と、描画先の長辺の上限（画素）
        private const float DetectionMaxUpscale = 3f;
        private const int DetectionMaxTarget = 4096;

        // 画像認識に渡す範囲: 顔の高さの半分に対する切り出す正方形の半分の大きさ（画像認識だけのとき）/
        // BlendShape から置いた目印の広がりに対する切り出す大きさ（併用のとき）/ これより小さければ画面全体（画面に対する比）
        private const float FaceCropRatio = 1.5f;
        private const float HybridCropRatio = 2f;
        private const float MinCropSize = 0.05f;

        // 画像認識に渡す画像のファイル名（一時フォルダ。終わったら消す）
        private const string DetectionImageName = "perfect_sync_face.png";

        // 眉として使う ARKit 名（PerfectSyncAutoMapper の対応表で既存の BlendShape を探す。動く範囲を合わせて使う）
        private static readonly string[] BrowShapes =
            { "browDownLeft", "browDownRight", "browOuterUpLeft", "browOuterUpRight", "browInnerUp" };

        /// <summary>
        /// 「BlendShape + 画像認識」で、画像認識の結果を待つ間に控えておく BlendShape からの結果。
        /// </summary>
        private sealed class HybridState
        {
            // BlendShape から置いた目印（比率で推定しただけのものは除く）と、その表面の点（ワールド座標）
            public readonly Dictionary<LandmarkId, PerfectSyncLandmark> Landmarks =
                new Dictionary<LandmarkId, PerfectSyncLandmark>();
            public readonly Dictionary<LandmarkId, Vector3> Points = new Dictionary<LandmarkId, Vector3>();

            // 比率で推定しただけの目印（画像認識で置けなければこれを使う）
            public readonly Dictionary<LandmarkId, PerfectSyncLandmark> Guessed =
                new Dictionary<LandmarkId, PerfectSyncLandmark>();
        }

        // 画像認識中のトラッカーと、その出力（別スレッドから足す）・開始時刻・画像
        private NativeProcess _detection;
        private readonly List<string> _detectionOutput = new List<string>();
        private float _detectionStarted;
        private string _detectionImage;

        // 撮ったときのカメラ（ワールド → 正規化デバイス座標とその逆）・切り出した範囲（画面に対する比、左下原点）・光線を当てる面
        private Matrix4x4 _detectionMatrix;
        private Matrix4x4 _detectionInverse;
        private Rect _detectionCrop = new Rect(0f, 0f, 1f, 1f);
        private List<int> _detectionSurface;

        // 併用中なら BlendShape からの結果（画像認識だけなら null）
        private HybridState _hybrid;

        /// <summary>
        /// 画像認識の結果を待っていれば true（自動配置のボタンを押せなくする）。
        /// </summary>
        private bool IsDetecting => _detection != null;

        private void AutoPlaceFromShapes()
        {
            // BlendShape から推定し、肌の表面（前を向いた面）の最も近い点にする
            if (!TryEstimateFromShapes(out LandmarkEstimate estimate))
            {
                return;
            }

            FaceAxes axes = CurrentAxes();
            var landmarks = new Dictionary<LandmarkId, PerfectSyncLandmark>();
            foreach (KeyValuePair<LandmarkId, Vector3> pair in estimate.Points)
            {
                landmarks[pair.Key] = SnapToSurface(estimate.Surface, pair.Value, axes.Forward, pair.Key);
            }

            ReportAutoPlaced(ApplyAutoLandmarks(landmarks), estimate.Guessed);
        }

        private void StartHybridDetection()
        {
            // まず BlendShape から点を打つ（画像認識の結果が来るまで置かずに控える）
            if (IsDetecting || !TryEstimateFromShapes(out LandmarkEstimate estimate))
            {
                return;
            }

            FaceAxes axes = CurrentAxes();
            var hybrid = new HybridState();
            foreach (KeyValuePair<LandmarkId, Vector3> pair in estimate.Points)
            {
                PerfectSyncLandmark landmark = SnapToSurface(estimate.Surface, pair.Value, axes.Forward, pair.Key);
                if (landmark == null)
                {
                    continue;
                }

                if (estimate.Guessed.Contains(pair.Key))
                {
                    hybrid.Guessed[pair.Key] = landmark;
                    continue;
                }

                hybrid.Landmarks[pair.Key] = landmark;
                hybrid.Points[pair.Key] = _geometry.TryGetPoint(landmark, out Vector3 point, _world) ? point : pair.Value;
            }

            // 打った点のまわりを切り出して画像認識する（顔が画像の大部分を占めるほうが見つかりやすい）
            Rect crop = hybrid.Points.Count >= 2 ? CropAround(hybrid.Points.Values, HybridCropRatio) : FaceCrop();
            _hybrid = hybrid;
            if (!StartImageDetection(crop, estimate.Surface))
            {
                _hybrid = null;
            }
        }

        private void StartFaceDetection()
        {
            // 顔のまわりを切り出して画像認識する（光線は肌だけに当てる）
            if (IsDetecting)
            {
                return;
            }

            StartImageDetection(FaceCrop(), LandmarkEstimator.SkinTriangles(_world, _geometry.Triangles, _region, CurrentAxes()));
        }

        private bool TryEstimateFromShapes(out LandmarkEstimate estimate)
        {
            // 顔メッシュのまばたき・口・眉の BlendShape を 1 つずつ当てた形を作る
            estimate = null;
            LandmarkShapeSamples samples = BakeSamples(out int found);
            if (found == 0)
            {
                SetMessage(Loc.T(
                    "No blink or mouth blend shapes were found on the face mesh. Try image recognition or place the points by hand.",
                    "顔メッシュにまばたき・口の BlendShape が見つかりません。画像認識を試すか、手で打ってください。",
                    "얼굴 메시에서 눈 깜빡임·입 BlendShape를 찾을 수 없습니다. 이미지 인식을 시도하거나 직접 찍으세요.",
                    "在面部网格中找不到眨眼·嘴部 BlendShape。请尝试图像识别或手动放置。",
                    "在臉部網格中找不到眨眼·嘴部 BlendShape。請嘗試影像辨識或手動放置。"), true);
                return false;
            }

            // 動く頂点から目印の位置を推定する（顔のまわりの三角形の肌だけを見る）
            estimate = LandmarkEstimator.Estimate(_world, _geometry.Triangles, _region, samples, CurrentAxes());
            return true;
        }

        private PerfectSyncLandmark SnapToSurface(List<int> surface, Vector3 point, Vector3 forward, LandmarkId id)
        {
            // 肌（分からなければ顔のまわり）の前を向いた面の最も近い点。前を向いた面が無ければ向きを問わない
            IReadOnlyList<int> triangles = surface != null && surface.Count > 0 ? surface : _region;
            return _geometry.Nearest(_world, triangles, point, forward, (int)id)
                ?? _geometry.Nearest(_world, triangles, point, Vector3.zero, (int)id);
        }

        private LandmarkShapeSamples BakeSamples(out int found)
        {
            // 顔メッシュの BlendShape から、名前の対応表で使えるものを探す
            Mesh mesh = _face.sharedMesh;
            var candidates = new List<PerfectSyncAutoMapper.Candidate>(mesh.blendShapeCount);
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                candidates.Add(new PerfectSyncAutoMapper.Candidate(_facePath, mesh.GetBlendShapeName(i)));
            }

            var byArKit = new Dictionary<string, string>();
            foreach (PerfectSyncBinding binding in PerfectSyncAutoMapper.Map(candidates))
            {
                byArKit[binding.name] = binding.blendShape;
            }

            // 同じ BlendShape は 1 回だけ形を作る（両目のまばたきが左右で同じなど）
            var cache = new Dictionary<int, Vector3[]>();
            var samples = new LandmarkShapeSamples
            {
                BlinkLeft = BakeShape(FindShape(mesh, byArKit, "eyeBlinkLeft"), cache),
                BlinkRight = BakeShape(FindShape(mesh, byArKit, "eyeBlinkRight"), cache),
                MouthOpen = BakeShape(FindShape(mesh, byArKit, "jawOpen"), cache),
                Pucker = BakeShape(FindShape(mesh, byArKit, "mouthPucker"), cache),
            };

            // 眉は使える形状をすべて（同じ BlendShape は 1 回だけ）
            var browIndices = new HashSet<int>();
            foreach (string name in BrowShapes)
            {
                int index = FindShape(mesh, byArKit, name);
                if (index >= 0 && browIndices.Add(index))
                {
                    samples.Brows.Add(BakeShape(index, cache));
                }
            }

            // 目か口のどちらかが無ければ推定できない
            found = samples.BlinkLeft != null || samples.BlinkRight != null || samples.MouthOpen != null ? cache.Count : 0;
            return samples;
        }

        private int FindShape(Mesh mesh, Dictionary<string, string> byArKit, string arKitName)
        {
            // 元からある ARKit 名の BlendShape（試しに足した形状ではないもの）を優先し、無ければ対応表の BlendShape
            int arKit = Array.IndexOf(ArKitFace.BlendShapeNames, arKitName);
            if (arKit >= 0 && _shapes[arKit] != null && _shapes[arKit].Native)
            {
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    if (PerfectSyncBlendShapes.Match(mesh.GetBlendShapeName(i)) == arKit)
                    {
                        return i;
                    }
                }
            }

            return byArKit.TryGetValue(arKitName, out string name) ? mesh.GetBlendShapeIndex(name) : -1;
        }

        private Vector3[] BakeShape(int index, Dictionary<int, Vector3[]> cache)
        {
            // 無い BlendShape は null。作ったことがあればそれを使う
            if (index < 0)
            {
                return null;
            }

            if (cache.TryGetValue(index, out Vector3[] baked))
            {
                return baked;
            }

            // その BlendShape だけを 100 にした今の姿勢の形（すぐ元の値に戻す）
            float previous = _face.GetBlendShapeWeight(index);
            _face.SetBlendShapeWeight(index, 100f);
            baked = BakeWorld(null);
            _face.SetBlendShapeWeight(index, previous);
            cache[index] = baked;
            return baked;
        }

        private bool StartImageDetection(Rect crop, List<int> surface)
        {
            // 同梱の MediaPipe トラッカーが必要
            string tracker = TrackerProcess.FindBundled(TrackerProcess.BundledRoot, TrackingSource.MediaPipe);
            if (tracker == null)
            {
                SetMessage(Loc.T("The bundled MediaPipe tracker was not found.", "同梱の MediaPipe トラッカーが見つかりません。",
                    "동봉된 MediaPipe 트래커를 찾을 수 없습니다.", "找不到附带的 MediaPipe 追踪器。", "找不到附帶的 MediaPipe 追蹤器。"), true);
                return false;
            }

            // 今の視点の顔を画像にして、トラッカーに渡す
            _detectionImage = Path.Combine(Application.temporaryCachePath, DetectionImageName);
            _detectionSurface = surface;
            try
            {
                CaptureFace(_detectionImage, crop);
                tracker = Path.GetFullPath(tracker);
                lock (_detectionOutput)
                {
                    _detectionOutput.Clear();
                }

                _detection = NativeProcess.Start(tracker, $"--face-image \"{_detectionImage}\"",
                    Path.GetDirectoryName(tracker), Encoding.UTF8, OnDetectionLine);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is Win32Exception)
            {
                // 画像を書けない・トラッカーを起動できない
                DeleteDetectionImage();
                SetMessage(Loc.T("Could not start image recognition: ", "画像認識を始められませんでした: ",
                    "이미지 인식을 시작하지 못했습니다: ", "无法开始图像识别: ", "無法開始影像辨識: ") + e.Message, true);
                VRCastLog.Warning(LogCategory, $"Failed to start face image recognition: {e.Message}");
                return false;
            }

            _detectionStarted = Time.realtimeSinceStartup;
            SetMessage(Loc.T("Recognizing the face...", "顔を画像認識しています...", "얼굴을 이미지 인식하는 중...",
                "正在识别面部...", "正在辨識臉部..."), false);
            return true;
        }

        private void OnDetectionLine(string line)
        {
            // トラッカーの出力（別スレッドから呼ばれる）
            lock (_detectionOutput)
            {
                _detectionOutput.Add(line);
            }
        }

        private Rect FaceCrop()
        {
            // 顔の中心のまわりの、画面に平行な正方形（顔の高さの半分 × FaceCropRatio）
            _avatar.CalculateFaceFrame(out Vector3 center, out float halfHeight);
            Transform view = _camera.transform;
            Vector3 right = view.right * (halfHeight * FaceCropRatio);
            Vector3 up = view.up * (halfHeight * FaceCropRatio);
            return CropAround(new[] { center + right + up, center + right - up, center - right + up, center - right - up }, 1f);
        }

        private Rect CropAround(IEnumerable<Vector3> points, float ratio)
        {
            // 点を画面へ映した範囲を、中心から ratio 倍に広げた正方形（画素で正方形。画面の外は切る）。
            // カメラの後ろの点がある・小さすぎるときは画面全体
            var full = new Rect(0f, 0f, 1f, 1f);
            float width = _camera.pixelWidth;
            float height = _camera.pixelHeight;
            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;
            float maxY = float.MinValue;
            foreach (Vector3 point in points)
            {
                Vector3 viewport = _camera.WorldToViewportPoint(point);
                if (viewport.z <= 0f)
                {
                    return full;
                }

                minX = Mathf.Min(minX, viewport.x * width);
                maxX = Mathf.Max(maxX, viewport.x * width);
                minY = Mathf.Min(minY, viewport.y * height);
                maxY = Mathf.Max(maxY, viewport.y * height);
            }

            if (minX > maxX || width <= 0f || height <= 0f)
            {
                return full;
            }

            float half = Mathf.Max(maxX - minX, maxY - minY) * ratio * 0.5f;
            float centerX = (minX + maxX) * 0.5f;
            float centerY = (minY + maxY) * 0.5f;
            Rect crop = Rect.MinMaxRect(Mathf.Max(0f, centerX - half) / width, Mathf.Max(0f, centerY - half) / height,
                Mathf.Min(width, centerX + half) / width, Mathf.Min(height, centerY + half) / height);
            return crop.width < MinCropSize || crop.height < MinCropSize ? full : crop;
        }

        private void CaptureFace(string path, Rect crop)
        {
            // 切り出す範囲の長辺が上限になる倍率で画面全体を描く（小さく映った顔は拡大して細部まで描く）
            float screenWidth = _camera.pixelWidth;
            float screenHeight = _camera.pixelHeight;
            float cropLong = Mathf.Max(crop.width * screenWidth, crop.height * screenHeight, 1f);
            float scale = Mathf.Min(DetectionMaxSize / cropLong, DetectionMaxUpscale,
                DetectionMaxTarget / Mathf.Max(screenWidth, screenHeight, 1f));
            int width = Mathf.Max(1, Mathf.RoundToInt(screenWidth * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(screenHeight * scale));

            // 描画先の画素で切り出す範囲（左下原点）
            int x = Mathf.Clamp(Mathf.FloorToInt(crop.x * width), 0, width - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(crop.y * height), 0, height - 1);
            int cropWidth = Mathf.Clamp(Mathf.RoundToInt(crop.width * width), 1, width - x);
            int cropHeight = Mathf.Clamp(Mathf.RoundToInt(crop.height * height), 1, height - y);

            // カメラを一時的に別の描画先へ向けて 1 回描く（顔の線・目印は GUI で描くので写らない）。
            // この 1 枚だけトゥーンを外す。画面のアバターは描き終わったら戻す
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24);
            RenderTexture previousTarget = _camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            var texture = new Texture2D(cropWidth, cropHeight, TextureFormat.RGB24, false);
            using (FaceRecognitionLook.Apply(_camera, _avatar != null ? _avatar.Instance : null))
            {
                try
                {
                    _camera.targetTexture = target;
                    _camera.Render();
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(x, y, cropWidth, cropHeight), 0, 0);
                    texture.Apply();
                    File.WriteAllBytes(path, texture.EncodeToPNG());
                }
                finally
                {
                    _camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                    RenderTexture.ReleaseTemporary(target);
                    UnityEngine.Object.Destroy(texture);
                }
            }

            // 画像の点と光線を行き来するため、撮ったときのカメラと切り出した範囲を控える（結果を待つ間に視点を動かしてもよい）
            _detectionMatrix = _camera.projectionMatrix * _camera.worldToCameraMatrix;
            _detectionInverse = _detectionMatrix.inverse;
            _detectionCrop = new Rect(x / (float)width, y / (float)height, cropWidth / (float)width,
                cropHeight / (float)height);
        }

        private void PollDetection()
        {
            // 終わるか時間切れになるまで待つ
            if (!IsDetecting)
            {
                return;
            }

            bool exited = _detection.HasExited;
            if (!exited && Time.realtimeSinceStartup - _detectionStarted < DetectionTimeout)
            {
                return;
            }

            // 時間切れなら止め、出力を読み終えてから片付ける
            if (!exited)
            {
                _detection.Kill();
            }

            _detection.WaitForOutput(DetectionOutputWait);
            int exitCode = _detection.ExitCode;
            _detection.Dispose();
            _detection = null;
            DeleteDetectionImage();

            List<string> lines;
            lock (_detectionOutput)
            {
                lines = new List<string>(_detectionOutput);
                _detectionOutput.Clear();
            }

            HybridState hybrid = _hybrid;
            _hybrid = null;
            FinishDetection(exited, exitCode, lines, hybrid);
        }

        private void FinishDetection(bool exited, int exitCode, List<string> lines, HybridState hybrid)
        {
            // 時間切れ
            if (!exited)
            {
                FinishWithoutImage(hybrid, Loc.T("Image recognition timed out.", "画像認識が時間内に終わりませんでした。",
                    "이미지 인식이 시간 내에 끝나지 않았습니다.", "图像识别超时。", "影像辨識逾時。"));
                return;
            }

            // 結果の行が無い（古いトラッカー・モデル欠けなど）
            if (!MediaPipeFaceMap.TryParse(lines, out bool face, out float[] points))
            {
                bool outdated = lines.Exists(line => line != null && line.Contains("unrecognized arguments"));
                FinishWithoutImage(hybrid, outdated
                    ? Loc.T("The bundled tracker is too old for image recognition. Update VRCast.",
                        "同梱のトラッカーが古く、画像認識に対応していません。VRCast を更新してください。",
                        "동봉된 트래커가 오래되어 이미지 인식을 지원하지 않습니다. VRCast를 업데이트하세요.",
                        "附带的追踪器版本过旧，不支持图像识别。请更新 VRCast。", "附帶的追蹤器版本過舊，不支援影像辨識。請更新 VRCast。")
                    : Loc.T($"Image recognition failed (exit code {exitCode}).", $"画像認識に失敗しました（終了コード {exitCode}）。",
                        $"이미지 인식에 실패했습니다 (종료 코드 {exitCode}).", $"图像识别失败（退出代码 {exitCode}）。",
                        $"影像辨識失敗（結束代碼 {exitCode}）。"));
                VRCastLog.Warning(LogCategory, $"Face image recognition failed (exit code {exitCode}): {string.Join(" | ", lines)}");
                return;
            }

            // 顔が見つからない（アニメ調の顔は見つかりにくい）
            if (!face)
            {
                FinishWithoutImage(hybrid, hybrid != null
                    ? Loc.T(
                        "No face was found in the image. Press \"Face the camera\", or change the view slightly (zoom in or out, move it a little) and try again.",
                        "画像から顔を見つけられませんでした。「顔を正面に」を押すか、視点を少し変えて（拡大・縮小したり、位置を少しずらしたり）再試行してください。",
                        "이미지에서 얼굴을 찾지 못했습니다. '얼굴 정면으로'를 누르거나 시점을 조금 바꿔서(확대·축소하거나 위치를 조금 옮겨서) 다시 시도하세요.",
                        "未能在图像中找到面部。请按“正对脸部”，或稍微改变视角（放大·缩小或稍微移动位置）后重试。",
                        "未能在影像中找到臉部。請按「正對臉部」，或稍微改變視角（放大·縮小或稍微移動位置）後重試。")
                    : Loc.T(
                        "No face was found in the image. Cartoon faces are often hard to recognize. Press \"Face the camera\", or change the view slightly (zoom in or out, move it a little) and try again, or try \"From blend shapes\".",
                        "画像から顔を見つけられませんでした。アニメ調の顔は認識しにくいことがあります。「顔を正面に」を押すか、視点を少し変えて（拡大・縮小したり、位置を少しずらしたり）再試行するか、「BlendShape から」を試してください。",
                        "이미지에서 얼굴을 찾지 못했습니다. 애니메이션풍 얼굴은 인식하기 어려울 수 있습니다. '얼굴 정면으로'를 누르거나 시점을 조금 바꿔서(확대·축소하거나 위치를 조금 옮겨서) 다시 시도하거나, 'BlendShape에서'를 시도하세요.",
                        "未能在图像中找到面部。动漫风格的脸可能难以识别。请按“正对脸部”，或稍微改变视角（放大·缩小或稍微移动位置）后重试，或尝试“从 BlendShape”。",
                        "未能在影像中找到臉部。動漫風格的臉可能難以辨識。請按「正對臉部」，或稍微改變視角（放大·縮小或稍微移動位置）後重試，或嘗試「從 BlendShape」。"));
                return;
            }

            if (hybrid != null)
            {
                FinishHybrid(hybrid, points);
                return;
            }

            // 目印ごとの画像上の点から、撮ったときのカメラの光線を顔の肌に当てる
            var landmarks = new Dictionary<LandmarkId, PerfectSyncLandmark>();
            foreach (LandmarkId id in FaceLandmarks.For(LandmarkDetail.Detailed))
            {
                if (MediaPipeFaceMap.TryGetPoint(points, id, out Vector2 point) && PlaceFromImage(point, out PerfectSyncLandmark hit))
                {
                    landmarks[id] = hit;
                }
            }

            ReportAutoPlaced(ApplyAutoLandmarks(landmarks), null);
        }

        private void FinishHybrid(HybridState hybrid, float[] points)
        {
            // BlendShape から置いた目印の、画像認識の点と画像上の本当の位置の組から補正を作る
            var detected = new List<Vector2>();
            var known = new List<Vector2>();
            foreach (KeyValuePair<LandmarkId, Vector3> pair in hybrid.Points)
            {
                if (MediaPipeFaceMap.TryGetPoint(points, pair.Key, out Vector2 point) && ToImage(pair.Value, out Vector2 image))
                {
                    detected.Add(point);
                    known.Add(image);
                }
            }

            ImageCorrection correction = ImageCorrection.Fit(detected, known);

            // BlendShape で置けなかった目印を補正した画像認識の点で置き、置けなければ比率で推定した位置にする
            var landmarks = new Dictionary<LandmarkId, PerfectSyncLandmark>(hybrid.Landmarks);
            var guessed = new HashSet<LandmarkId>();
            foreach (LandmarkId id in FaceLandmarks.For(LandmarkDetail.Detailed))
            {
                if (landmarks.ContainsKey(id))
                {
                    continue;
                }

                if (MediaPipeFaceMap.TryGetPoint(points, id, out Vector2 point)
                    && PlaceFromImage(correction.Apply(point), out PerfectSyncLandmark hit))
                {
                    landmarks[id] = hit;
                }
                else if (hybrid.Guessed.TryGetValue(id, out PerfectSyncLandmark guess))
                {
                    landmarks[id] = guess;
                    guessed.Add(id);
                }
            }

            ReportAutoPlaced(ApplyAutoLandmarks(landmarks), guessed);
        }

        private void FinishWithoutImage(HybridState hybrid, string reason)
        {
            // 画像認識だけなら理由を出して終わり。併用なら BlendShape から求めた目印だけを置き、理由を添える
            if (hybrid == null)
            {
                SetMessage(reason, true);
                return;
            }

            var landmarks = new Dictionary<LandmarkId, PerfectSyncLandmark>(hybrid.Landmarks);
            foreach (KeyValuePair<LandmarkId, PerfectSyncLandmark> pair in hybrid.Guessed)
            {
                landmarks[pair.Key] = pair.Value;
            }

            ReportAutoPlaced(ApplyAutoLandmarks(landmarks), new HashSet<LandmarkId>(hybrid.Guessed.Keys),
                reason + Loc.T(" Placed the points from blend shapes only.", " BlendShape から求めた目印だけを置きました。",
                    " BlendShape에서 구한 표시만 찍었습니다.", " 仅放置了从 BlendShape 求得的标记。", " 僅放置了從 BlendShape 求得的標記。"));
        }

        private bool PlaceFromImage(Vector2 imagePoint, out PerfectSyncLandmark hit)
        {
            // 撮ったときのカメラの光線を肌に当てる。目・口の穴を通り抜けたら、光線に最も近い肌の点
            Ray ray = DetectionRay(imagePoint);
            IReadOnlyList<int> surface = _detectionSurface != null && _detectionSurface.Count > 0 ? _detectionSurface : _region;
            if (_geometry.Raycast(_world, ray, surface, out hit, out float _))
            {
                return true;
            }

            float best = float.MaxValue;
            Vector3 nearest = Vector3.zero;
            int[] triangles = _geometry.Triangles;
            foreach (int triangle in surface)
            {
                for (int k = 0; k < 3; k++)
                {
                    Vector3 vertex = _world[triangles[triangle * 3 + k]];
                    float along = Vector3.Dot(vertex - ray.origin, ray.direction);
                    Vector3 onRay = ray.origin + ray.direction * along;
                    float distance = (vertex - onRay).sqrMagnitude;
                    if (along > 0f && distance < best)
                    {
                        best = distance;
                        nearest = onRay;
                    }
                }
            }

            hit = best < float.MaxValue
                ? _geometry.Nearest(_world, surface, nearest, -ray.direction, 0) ?? _geometry.Nearest(_world, surface, nearest, Vector3.zero, 0)
                : null;
            return hit != null;
        }

        private Ray DetectionRay(Vector2 imagePoint)
        {
            // 画像の座標（左上原点・0〜1）を、切り出した範囲を通して撮ったときのカメラの正規化デバイス座標（左下原点・-1〜1）の
            // 近くと遠くへ戻す
            float x = (_detectionCrop.x + imagePoint.x * _detectionCrop.width) * 2f - 1f;
            float y = (_detectionCrop.y + (1f - imagePoint.y) * _detectionCrop.height) * 2f - 1f;
            Vector3 near = _detectionInverse.MultiplyPoint(new Vector3(x, y, -1f));
            Vector3 far = _detectionInverse.MultiplyPoint(new Vector3(x, y, 1f));
            return new Ray(near, (far - near).normalized);
        }

        private bool ToImage(Vector3 world, out Vector2 image)
        {
            // ワールド座標の点を、撮ったときのカメラで切り出した画像の座標（左上原点・0〜1）へ（カメラの後ろなら false）
            image = default;
            Vector4 clip = _detectionMatrix * new Vector4(world.x, world.y, world.z, 1f);
            if (clip.w <= 1e-6f || _detectionCrop.width <= 0f || _detectionCrop.height <= 0f)
            {
                return false;
            }

            float x = (clip.x / clip.w + 1f) * 0.5f;
            float y = (clip.y / clip.w + 1f) * 0.5f;
            image = new Vector2((x - _detectionCrop.x) / _detectionCrop.width,
                1f - (y - _detectionCrop.y) / _detectionCrop.height);
            return true;
        }

        private int ApplyAutoLandmarks(Dictionary<LandmarkId, PerfectSyncLandmark> found)
        {
            // 置く前の状態（何か置いたときだけ履歴に積む）
            UndoState before = Snapshot();
            int count = 0;
            foreach (LandmarkId id in ActiveLandmarks())
            {
                // 手で打った目印は変えない。左右対称なら右側は左側から決める
                if ((_landmarks.ContainsKey(id) && !_autoMirrored.Contains(id)) || (_mirror && FaceLandmarks.IsRight(id))
                    || !found.TryGetValue(id, out PerfectSyncLandmark landmark) || landmark == null)
                {
                    continue;
                }

                landmark.id = (int)id;
                _landmarks[id] = landmark;
                _autoMirrored.Remove(id);
                count++;
            }

            if (count == 0)
            {
                return 0;
            }

            // 履歴に積み、右側を置き直し、次に打つ目印を選ぶ
            PushUndo(before);
            Remirror(null);
            _lastPlaced = null;
            _selected = NextUnplaced() ?? _selected;
            _dirty = true;
            return count;
        }

        private void ReportAutoPlaced(int count, HashSet<LandmarkId> guessed, string note = null)
        {
            // 置けなかった
            string prefix = string.IsNullOrEmpty(note) ? string.Empty : note + " ";
            if (count == 0)
            {
                SetMessage(prefix + Loc.T("No points could be placed automatically (points you placed are kept).",
                    "自動で置ける目印がありませんでした（打った目印はそのままです）。",
                    "자동으로 찍을 수 있는 표시가 없었습니다(찍은 표시는 그대로입니다).",
                    "没有可自动放置的标记（已放置的标记保持不变）。", "沒有可自動放置的標記（已放置的標記保持不變）。"), true);
                return;
            }

            // 置いた数と、比率で推定しただけの目印（ずれやすいので確かめてもらう）
            string text = prefix + Loc.T($"Placed {count} points. Drag any that are off. Undo (Ctrl+Z) removes them.",
                $"{count} 個の目印を置きました。ずれている目印はドラッグで直してください（元に戻す（Ctrl+Z）で取り消せます）。",
                $"표시 {count}개를 찍었습니다. 어긋난 표시는 드래그로 고치세요(실행 취소(Ctrl+Z)로 취소할 수 있습니다).",
                $"已放置 {count} 个标记。偏移的标记请拖动修正（可用撤销（Ctrl+Z）取消）。",
                $"已放置 {count} 個標記。偏移的標記請拖曳修正（可用復原（Ctrl+Z）取消）。");
            var rough = new List<string>();
            foreach (LandmarkId id in guessed ?? new HashSet<LandmarkId>())
            {
                if (_landmarks.ContainsKey(id) && !_autoMirrored.Contains(id))
                {
                    rough.Add(PerfectSyncLabels.Landmark(id));
                }
            }

            if (rough.Count > 0)
            {
                text += Loc.T(" Rough guesses: ", " 目安の位置: ", " 대략적인 위치: ", " 大致位置: ", " 大致位置: ") + string.Join(", ", rough);
            }

            SetMessage(text, !string.IsNullOrEmpty(note));
        }

        private void CancelDetection()
        {
            // 画像認識中なら止めて片付ける（閉じたとき）
            if (_detection != null)
            {
                _detection.Kill();
                _detection.Dispose();
                _detection = null;
            }

            _hybrid = null;
            DeleteDetectionImage();
        }

        private void DeleteDetectionImage()
        {
            // 一時ファイルを消す（消せなくても次回上書きする）
            if (string.IsNullOrEmpty(_detectionImage))
            {
                return;
            }

            try
            {
                File.Delete(_detectionImage);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                VRCastLog.Warning(LogCategory, $"Failed to delete {_detectionImage}: {e.Message}");
            }

            _detectionImage = null;
        }
    }
}
