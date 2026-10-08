using UnityEngine;
using VRCast.Core;

namespace VRCast.Tracking
{
    /// <summary>
    /// 受信したトラッキング値を平滑化・補正せずに線で描く確認用の表示（腕・手の点、背骨と上半身の向き、頭の向き、視線、目と口の開き）。
    /// 表示中はカメラの描画対象からアバターを外し、アバターの腰の位置にアバターと同じ向き・鏡像設定で、
    /// アバターの大きさ（腰から頭までの長さ）に合わせて拡大・縮小して描く（表示を切り替えてもカメラを動かさずに済むように）。
    /// </summary>
    public class TrackingSkeletonView : MonoBehaviour
    {
        // 線の描画に使う組み込みシェーダー（ビルドにも常に含まれる）
        private const string ShaderName = "Hidden/Internal-Colored";

        // 線の太さと関節の印の大きさ（m）
        private const float ArmWidth = 0.012f;
        private const float HandWidth = 0.005f;
        private const float JointSize = 0.012f;

        // 頭の箱の半分の大きさ、肩の中点から頭の中心までの長さ、鼻の線の長さ（m）
        private static readonly Vector3 HeadHalfSize = new Vector3(0.08f, 0.1f, 0.09f);
        private const float NeckLength = 0.22f;
        private const float NoseLength = 0.08f;

        // 目の位置（頭の中心基準、x は本人の右目側）、視線の線の最大長、口の位置と開き切ったときの長さ（m）
        private static readonly Vector3 EyeOffset = new Vector3(0.035f, 0.02f, 0.09f);
        private const float GazeLength = 0.08f;
        private static readonly Vector3 MouthOffset = new Vector3(0f, -0.05f, 0.09f);
        private const float MouthLength = 0.05f;

        // 肩が映っていないときの首の位置（腰基準）と、アバターが無いときの腰の位置（ワールド）
        private static readonly Vector3 DefaultNeck = new Vector3(0f, 0.45f, 0f);
        private static readonly Vector3 DefaultHips = new Vector3(0f, 1f, 0f);

        // 上半身の箱の半分の大きさと、背骨上の位置（腰 0 〜 首 1）（m）
        private static readonly Vector3 ChestHalfSize = new Vector3(0.13f, 0.1f, 0.07f);
        private const float ChestHeight = 0.65f;

        // 人の腰から頭のボーンまでの標準の長さ（m）と、アバターに合わせる倍率の範囲
        private const float ReferenceHipsToHead = 0.6f;
        private const float MinScale = 0.1f;
        private const float MaxScale = 10f;

        // 色（本人の左・右、可視度不足の腕・使わない上半身、胴・頭、アバターに使う上半身の向き）
        private static readonly Color LeftColor = new Color(0.3f, 0.7f, 1f);
        private static readonly Color RightColor = new Color(1f, 0.55f, 0.2f);
        private static readonly Color LowVisibilityColor = new Color(0.5f, 0.5f, 0.5f);
        private static readonly Color CenterColor = new Color(0.9f, 0.9f, 0.9f);
        private static readonly Color TorsoColor = new Color(0.45f, 0.9f, 0.45f);

        // 手の線（MediaPipe Hand Landmarker の番号の組。各指と手のひら）
        private static readonly int[] HandLines =
        {
            0, 1, 1, 2, 2, 3, 3, 4,
            0, 5, 5, 6, 6, 7, 7, 8,
            9, 10, 10, 11, 11, 12,
            13, 14, 14, 15, 15, 16,
            0, 17, 17, 18, 18, 19, 19, 20,
            5, 9, 9, 13, 13, 17,
        };

        private UnityEngine.Camera _camera;
        private IFaceTrackingProvider _face;
        private IBodyTrackingProvider _body;
        private AppSettings _settings;
        private Material _material;
        private Transform _avatarRoot;
        private Transform _hips;

        // アバターの大きさに合わせる倍率（1 = 実寸。アバターの読込時に決める）
        private float _scale = 1f;

        // 頭の箱の 8 隅（毎フレームの確保を避けて使い回す）
        private readonly Vector3[] _corners = new Vector3[8];

        // アバターを外す前のカメラの描画対象（戻す用）と、外しているかどうか
        private int _savedCullingMask;
        private bool _showing;

        // 描画中のフレームの基準（腰の位置・アバターの向き・鏡像・カメラの位置と向き）
        private Vector3 _origin;
        private Quaternion _rotation;
        private bool _mirror;
        private Vector3 _eye;
        private Vector3 _cameraRight;
        private Vector3 _cameraUp;

        /// <summary>
        /// アバターの代わりに表示するなら true（トラッキングが OFF の間は表示しない）。
        /// </summary>
        public bool Visible { get; set; }

        public void Initialize(
            UnityEngine.Camera targetCamera, IFaceTrackingProvider face, IBodyTrackingProvider body,
            AppSettings settings)
        {
            _camera = targetCamera;
            _face = face;
            _body = body;
            _settings = settings;

            // シェーダーが見つからない環境では表示しない（アバターも外さない）
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                VRCastLog.Warning("Tracking", "Skeleton view unavailable: shader not found");
                return;
            }

            // 両面・深度ありの単色描画
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 1);
            _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
            UnityEngine.Camera.onPostRender += OnCameraPostRender;
        }

        /// <summary>
        /// 描画の基準にするアバター（向き）と、腰の位置・大きさを取る Animator（非 Humanoid・null なら足元基準の実寸）を設定する。
        /// </summary>
        public void SetAnchor(Transform avatarRoot, Animator animator)
        {
            _avatarRoot = avatarRoot;

            // Humanoid なら腰と頭のボーンを使う
            bool human = animator != null && animator.isHuman;
            _hips = human ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            Transform head = human ? animator.GetBoneTransform(HumanBodyBones.Head) : null;

            // 腰から頭までの長さを人の標準と比べた倍率（読込直後に測る。測れなければ実寸）
            _scale = _hips != null && head != null
                ? Mathf.Clamp(Vector3.Distance(_hips.position, head.position) / ReferenceHipsToHead, MinScale, MaxScale)
                : 1f;
        }

        /// <summary>
        /// 受信中の両肩から求めた上半身のひねり・傾き（度、正面の補正前）。両肩が映っていなければ false。
        /// </summary>
        public bool TryGetTorsoAngles(out Vector2 angles)
        {
            angles = Vector2.zero;
            return _body != null && _body.TryGetBody(out BodyTrackingFrame body)
                && TorsoPose.TryGetAngles(body, _settings.trackingMirror, out angles);
        }

        private void LateUpdate()
        {
            // 未初期化（シェーダー無し含む）なら何もしない
            if (_settings == null || _material == null || _camera == null)
            {
                return;
            }

            // 表示の切り替わり時だけカメラの描画対象を差し替える（表示中は何も映さず線だけ描く）
            bool show = Visible && _settings.trackingEnabled;
            if (show == _showing)
            {
                return;
            }

            if (show)
            {
                _savedCullingMask = _camera.cullingMask;
                _camera.cullingMask = 0;
            }
            else
            {
                _camera.cullingMask = _savedCullingMask;
            }

            _showing = show;
        }

        private void OnDestroy()
        {
            // 購読解除・描画対象の復元・マテリアル破棄
            UnityEngine.Camera.onPostRender -= OnCameraPostRender;
            if (_showing && _camera != null)
            {
                _camera.cullingMask = _savedCullingMask;
            }

            if (_material != null)
            {
                Destroy(_material);
            }
        }

        private void OnCameraPostRender(UnityEngine.Camera renderingCamera)
        {
            // 表示中のメインカメラだけに描く
            if (!_showing || renderingCamera != _camera)
            {
                return;
            }

            PrepareFrame(renderingCamera);

            // カメラの行列で、ワールド座標の四角形として線を描く
            _material.SetPass(0);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(renderingCamera.projectionMatrix);
            GL.modelview = renderingCamera.worldToCameraMatrix;
            GL.Begin(GL.QUADS);
            DrawFrame();
            GL.End();
            GL.PopMatrix();
        }

        private void PrepareFrame(UnityEngine.Camera renderingCamera)
        {
            // 腰（MediaPipe の体の点の原点）の位置。腰のボーンが無ければアバターの足元から、アバターも無ければ既定位置
            bool hasAvatar = _avatarRoot != null;
            _rotation = hasAvatar ? _avatarRoot.rotation : Quaternion.identity;
            if (_hips != null)
            {
                _origin = _hips.position;
            }
            else
            {
                _origin = hasAvatar ? _avatarRoot.position + _rotation * (DefaultHips * _scale) : DefaultHips;
            }

            // 鏡像設定と、線をカメラへ向けるための視点
            _mirror = _settings.trackingMirror;
            Transform view = renderingCamera.transform;
            _eye = view.position;
            _cameraRight = view.right;
            _cameraUp = view.up;
        }

        private void DrawFrame()
        {
            // 腕・手（受信中のみ）
            bool hasBody = _body.TryGetBody(out BodyTrackingFrame body);
            bool hasLeft = hasBody && HasPose(body.Left);
            bool hasRight = hasBody && HasPose(body.Right);
            if (hasBody)
            {
                DrawArm(body.Left, hasLeft, LeftColor);
                DrawArm(body.Right, hasRight, RightColor);
            }

            // 首: 両肩が映っていれば肩の中点（肩の線・背骨・上半身の向きも描く）、無ければ既定位置
            Vector3 neck;
            if (hasLeft && hasRight)
            {
                Vector3 left = ToWorld(body.Left.Shoulder);
                Vector3 right = ToWorld(body.Right.Shoulder);
                Line(left, right, Scaled(ArmWidth), CenterColor);
                neck = (left + right) * 0.5f;
                DrawTorso(body, neck);
            }
            else
            {
                neck = _origin + _rotation * (DefaultNeck * _scale);
            }

            // 頭・視線・目・口（顔を受信中のみ）
            if (_face.TryGetFrame(out FaceTrackingFrame face))
            {
                DrawHead(face, neck);
            }
        }

        private void DrawTorso(BodyTrackingFrame body, Vector3 neck)
        {
            // 背骨: 腰（体の点の原点）から首へ
            Joint(_origin, CenterColor);
            Line(_origin, neck, Scaled(ArmWidth), CenterColor);

            // 上半身の向き: 肩の線から求めたひねり・傾き（補正前の生の角度）。肩幅が狭すぎて求まらなければ描かない
            if (!TorsoPose.TryGetAngles(body, _mirror, out Vector2 angles))
            {
                return;
            }

            // ひねりを固定する設定ならひねりは 0（アバターと同じ）。アバターに使わない設定（上半身 OFF・腕と手 OFF）は灰色
            if (_settings.trackingTorsoLockTwist)
            {
                angles.x = 0f;
            }

            bool used = _settings.trackingTorso && _settings.trackingHands;
            Color color = used ? TorsoColor : LowVisibilityColor;
            Quaternion chest = _rotation * TorsoPose.ToRotation(angles);
            Vector3 center = Vector3.Lerp(_origin, neck, ChestHeight);
            DrawBox(center, chest, ChestHalfSize * _scale, color);

            // 胸の正面の向き
            Vector3 front = center + chest * new Vector3(0f, 0f, ChestHalfSize.z * _scale);
            Line(front, front + chest * Vector3.forward * Scaled(NoseLength), Scaled(ArmWidth), color);
        }

        private static bool HasPose(ArmTrackingData data)
        {
            // 体が映っていないフレームは 3 点とも 0
            return (data.Shoulder - data.Wrist).sqrMagnitude > 1e-8f;
        }

        private void DrawArm(ArmTrackingData data, bool hasPose, Color color)
        {
            // 腕: 可視度不足（アバターには使わない値）は灰色で描く
            if (hasPose)
            {
                Color armColor = data.HasArm ? color : LowVisibilityColor;
                Vector3 shoulder = ToWorld(data.Shoulder);
                Vector3 elbow = ToWorld(data.Elbow);
                Vector3 wrist = ToWorld(data.Wrist);
                Line(shoulder, elbow, Scaled(ArmWidth), armColor);
                Line(elbow, wrist, Scaled(ArmWidth), armColor);
                Joint(shoulder, armColor);
                Joint(elbow, armColor);
                Joint(wrist, armColor);
            }

            // 手: 手の点は手の中心が原点のため、手首の点を腕の手首に合わせて描く（腕が無ければ腰の位置）
            if (!data.HasHand)
            {
                return;
            }

            Vector3 offset = (hasPose ? data.Wrist : Vector3.zero) - data.Hand[0];
            for (int i = 0; i < HandLines.Length; i += 2)
            {
                Line(ToWorld(data.Hand[HandLines[i]] + offset), ToWorld(data.Hand[HandLines[i + 1]] + offset),
                    Scaled(HandWidth), color);
            }
        }

        private void DrawHead(FaceTrackingFrame face, Vector3 neck)
        {
            // 頭の向き（アバタールート基準 → ワールド、鏡像設定に従う）と中心
            Quaternion local = _mirror ? TrackingMath.MirrorRotation(face.HeadRotation) : face.HeadRotation;
            Quaternion head = _rotation * local;
            Vector3 halfSize = HeadHalfSize * _scale;
            Vector3 center = neck + _rotation * Vector3.up * Scaled(NeckLength);
            Line(neck, center - head * new Vector3(0f, halfSize.y, 0f), Scaled(ArmWidth), CenterColor);
            DrawBox(center, head, halfSize, CenterColor);

            // 鼻（顔の正面）
            Vector3 front = center + head * new Vector3(0f, 0f, halfSize.z);
            Line(front, front + head * Vector3.forward * Scaled(NoseLength), Scaled(ArmWidth), CenterColor);

            // 目: 本人の右目は通常アバターの右（+x）、鏡像では左（顔の左右の入れ替えも反映）。線の向き = 視線、長さ = 目の開き
            bool faceMirror = _settings.FaceMirror;
            float rightSide = faceMirror ? -1f : 1f;
            float yaw = faceMirror ? -face.Gaze.x : face.Gaze.x;
            Quaternion gaze = head * Quaternion.Euler(-face.Gaze.y, yaw, 0f);
            DrawEye(center, head, rightSide, face.EyeOpenRight, gaze, RightColor);
            DrawEye(center, head, -rightSide, face.EyeOpenLeft, gaze, LeftColor);

            // 口: 縦線の長さ = 開き
            Vector3 mouth = center + head * (MouthOffset * _scale);
            Joint(mouth, CenterColor);
            Line(mouth, mouth - head * Vector3.up * (Scaled(MouthLength) * face.MouthOpen), Scaled(ArmWidth), CenterColor);
        }

        private void DrawEye(Vector3 center, Quaternion head, float side, float open, Quaternion gaze, Color color)
        {
            // 目の位置に印、そこから視線の向きへ開き具合の長さの線
            Vector3 eye = center + head * (new Vector3(EyeOffset.x * side, EyeOffset.y, EyeOffset.z) * _scale);
            Joint(eye, color);
            Line(eye, eye + gaze * Vector3.forward * (Scaled(GazeLength) * open), Scaled(HandWidth), color);
        }

        private void DrawBox(Vector3 center, Quaternion rotation, Vector3 halfSize, Color color)
        {
            // 8 隅（ビット 0 = x、1 = y、2 = z の正負）
            Vector3[] corners = _corners;
            for (int i = 0; i < corners.Length; i++)
            {
                var sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                corners[i] = center + rotation * Vector3.Scale(sign, halfSize);
            }

            // 1 ビットだけ違う隅同士を結ぶ 12 辺
            for (int i = 0; i < corners.Length; i++)
            {
                for (int bit = 1; bit < corners.Length; bit <<= 1)
                {
                    if ((i & bit) == 0)
                    {
                        Line(corners[i], corners[i | bit], Scaled(HandWidth), color);
                    }
                }
            }
        }

        private Vector3 ToWorld(Vector3 cameraPoint)
        {
            // カメラ基準（腰が原点）→ アバタールート基準 → アバターの大きさに合わせてワールドへ
            return _origin + _rotation * (TrackingMath.ToAvatar(cameraPoint, _mirror) * _scale);
        }

        private float Scaled(float length)
        {
            // 実寸の長さ・太さをアバターの大きさに合わせる
            return length * _scale;
        }

        private void Line(Vector3 from, Vector3 to, float width, Color color)
        {
            // 線に垂直かつカメラに向いた幅を持つ四角形（長さ 0 やカメラと一直線なら描かない）
            Vector3 side = Vector3.Cross(to - from, _eye - from);
            if (side.sqrMagnitude < 1e-12f)
            {
                return;
            }

            side = side.normalized * (width * 0.5f);
            GL.Color(color);
            GL.Vertex(from - side);
            GL.Vertex(from + side);
            GL.Vertex(to + side);
            GL.Vertex(to - side);
        }

        private void Joint(Vector3 position, Color color)
        {
            // カメラに向いた正方形
            Vector3 right = _cameraRight * Scaled(JointSize);
            Vector3 up = _cameraUp * Scaled(JointSize);
            GL.Color(color);
            GL.Vertex(position - right - up);
            GL.Vertex(position - right + up);
            GL.Vertex(position + right + up);
            GL.Vertex(position + right - up);
        }
    }
}
