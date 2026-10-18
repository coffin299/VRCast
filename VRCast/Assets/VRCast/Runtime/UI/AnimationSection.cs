using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Dynamics;

namespace VRCast.UI
{
    /// <summary>
    /// 体・ポーズタブ（待機ポーズ・待機モーション・揺れもの・小物のアニメーション）。表示中アバターのコントローラーを操作する。
    /// </summary>
    public class AnimationSection
    {
        // チェーン一覧の最大の高さ（これ以上はスクロール）
        private const float MaxChainListHeight = 200f;
        private const float ChainRowHeight = 22f;

        private readonly AvatarSession _session;
        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;
        private PoseController _pose;
        private IdleMotionController _idleMotion;
        private PhysBoneSimulator _physics;

        // チェーン一覧を出すトグル（既定 OFF。アバターが替わったら閉じる）とスクロール位置
        private bool _showChainList;
        private Vector2 _chainScroll;

        public AnimationSection(AvatarSession session, AppSettings settings)
        {
            _session = session;
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        public void Draw()
        {
            // アバター未表示なら案内だけ（揺れものの全体 ON/OFF は全アバター共通なので出す）
            if (!RefreshControllers())
            {
                GuiControls.BeginCard(Loc.T("Pose", "ポーズ", "포즈", "姿势", "姿勢"));
                GuiControls.Hint(Loc.T("Load an avatar first.", "先にアバターを読み込んでください。", "먼저 아바타를 불러오세요.",
                    "请先加载虚拟形象。", "請先載入虛擬形象。"));
                GuiControls.EndCard();
                DrawPhysics();
                DrawPropAnimations();
                return;
            }

            DrawPose();
            DrawIdleMotion();
            DrawPhysics();
            DrawPropAnimations();
        }

        private bool RefreshControllers()
        {
            // アバターが替わったときだけ取り直す（チェーン一覧は閉じる）
            if (_avatar.Refresh())
            {
                _pose = _avatar.Get<PoseController>();
                _idleMotion = _avatar.Get<IdleMotionController>();
                _physics = _avatar.Get<PhysBoneSimulator>();
                _showChainList = false;
                _chainScroll = Vector2.zero;
            }

            return _avatar.HasAvatar;
        }

        private void DrawPose()
        {
            GuiControls.BeginCard(Loc.T("Pose", "ポーズ", "포즈", "姿势", "姿勢"));
            if (_pose == null)
            {
                GuiControls.EndCard();
                return;
            }

            GuiControls.Hint(Loc.T("Body yaw and arm pose are remembered for each avatar.",
                "体の向きと腕のポーズはアバターごとに記憶されます。",
                "몸 방향과 팔 포즈는 아바타마다 기억됩니다.",
                "身体朝向和手臂姿势会按虚拟形象分别记住。",
                "身體朝向和手臂姿勢會依虛擬形象分別記住。"));

            // アバターの向き（非 Humanoid でも有効）
            _pose.BodyYaw = GuiControls.Slider(Loc.T("Body yaw", "体の向き", "몸 방향", "身体朝向", "身體朝向"), _pose.BodyYaw, -180f, 180f);

            // 非 Humanoid は腕の操作不可
            if (!_pose.IsAvailable)
            {
                GuiControls.Hint(Loc.T("Arm pose requires a Humanoid avatar.", "腕のポーズは Humanoid アバターのみ対応です。",
                    "팔 포즈는 Humanoid 아바타만 지원합니다.",
                    "手臂姿势仅支持 Humanoid 虚拟形象。", "手臂姿勢僅支援 Humanoid 虛擬形象。"));
                GuiControls.EndCard();
                return;
            }

            // よく使う 3 状態へのショートカット
            GUILayout.BeginHorizontal();

            // 気を付け（既定）: 腕を下ろし切り、肘はまっすぐ
            if (GUILayout.Button(Loc.T("Attention", "気を付け", "차렷", "立正", "立正"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 1f;
                _pose.ElbowBend = 0f;
            }

            // 腕を少し開き、肘を軽く曲げる
            if (GUILayout.Button(Loc.T("Relaxed", "リラックス", "편안하게", "放松", "放鬆"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 0.85f;
                _pose.ElbowBend = 0.3f;
            }

            // 読込時の姿勢（通常 T ポーズ）
            if (GUILayout.Button(Loc.T("T-Pose", "T ポーズ", "T 포즈", "T 姿势", "T 姿勢"), GuiControls.Shrinkable))
            {
                _pose.ArmDown = 0f;
                _pose.ElbowBend = 0f;
            }

            GUILayout.EndHorizontal();

            // 腕と肘の度合い
            _pose.ArmDown = GuiControls.Slider(Loc.T("Arms down", "腕を下ろす", "팔 내리기", "放下手臂", "放下手臂"), _pose.ArmDown, 0f, 1f);
            _pose.ElbowBend = GuiControls.Slider(Loc.T("Elbow bend", "肘の曲げ", "팔꿈치 굽힘", "肘部弯曲", "肘部彎曲"), _pose.ElbowBend, 0f, 1f);
            DrawArmAdjust();
            GuiControls.EndCard();
        }

        private void DrawArmAdjust()
        {
            GuiControls.SubHeading(Loc.T("Arm direction", "腕の向きの微調整", "팔 방향 미세 조정", "手臂方向微调", "手臂方向微調"));
            GuiControls.Hint(Loc.T(
                "If an arm hides behind the body or skirt, move it forward. Left and right are as seen from the avatar " +
                "(the left arm is on the right side of the screen when facing the camera). Twist changes where the bent " +
                "forearm points (+ = inward).",
                "腕が体やスカートの後ろに隠れるときは、前後を + にして前へ出してください。左右はアバターから見た向きです" +
                "（正面から見ると左腕は画面の右側）。ひねりは曲げた肘から先の向きが変わります（+ = 内側）。",
                "팔이 몸이나 치마 뒤에 숨으면 앞뒤를 + 로 해서 앞으로 내미세요. 좌우는 아바타 기준입니다" +
                "(정면에서 보면 왼팔은 화면 오른쪽). 비틀기는 굽힌 팔꿈치 아래의 방향이 바뀝니다(+ = 안쪽).",
                "手臂藏到身体或裙子后面时，请把前后调为 + 往前移。左右以虚拟形象自身为准" +
                "（从正面看左臂在画面右侧）。扭转会改变弯曲后前臂的朝向（+ = 向内）。",
                "手臂藏到身體或裙子後面時，請把前後調為 + 往前移。左右以虛擬形象自身為準" +
                "（從正面看左臂在畫面右側）。扭轉會改變彎曲後前臂的朝向（+ = 向內）。"));

            _pose.ArmSpread = GuiControls.Slider(Loc.T("Arm spread", "腕の開き", "팔 벌리기", "手臂张开", "手臂張開"),
                _pose.ArmSpread, 0f, AppSettings.MaxPoseArmSpread, "F0");
            _pose.ArmForwardLeft = GuiControls.Slider(
                Loc.T("Left arm forward", "左腕の前後", "왼팔 앞뒤", "左臂前后", "左臂前後"),
                _pose.ArmForwardLeft, AppSettings.MinPoseArmForward, AppSettings.MaxPoseArmForward, "F0");
            _pose.ArmForwardRight = GuiControls.Slider(
                Loc.T("Right arm forward", "右腕の前後", "오른팔 앞뒤", "右臂前后", "右臂前後"),
                _pose.ArmForwardRight, AppSettings.MinPoseArmForward, AppSettings.MaxPoseArmForward, "F0");
            _pose.ArmTwistLeft = GuiControls.Slider(
                Loc.T("Left arm twist", "左腕のひねり", "왼팔 비틀기", "左臂扭转", "左臂扭轉"),
                _pose.ArmTwistLeft, -AppSettings.MaxPoseArmTwist, AppSettings.MaxPoseArmTwist, "F0");
            _pose.ArmTwistRight = GuiControls.Slider(
                Loc.T("Right arm twist", "右腕のひねり", "오른팔 비틀기", "右臂扭转", "右臂扭轉"),
                _pose.ArmTwistRight, -AppSettings.MaxPoseArmTwist, AppSettings.MaxPoseArmTwist, "F0");

            if (GUILayout.Button(Loc.T("Reset arm direction", "腕の向きを元に戻す", "팔 방향 초기화", "重置手臂方向",
                    "重設手臂方向"), GuiControls.Shrinkable))
            {
                _pose.ResetArmAdjust();
            }
        }

        private void DrawIdleMotion()
        {
            GuiControls.BeginCard(Loc.T("Idle motion", "待機モーション", "대기 모션", "待机动作", "待機動作"));

            // 非 Humanoid は動かせるボーンが無い
            if (_idleMotion == null || !_idleMotion.IsAvailable)
            {
                GuiControls.Hint(Loc.T("Idle motion requires a Humanoid avatar.", "待機モーションは Humanoid アバターのみ対応です。",
                    "대기 모션은 Humanoid 아바타만 지원합니다.",
                    "待机动作仅支持 Humanoid 虚拟形象。", "待機動作僅支援 Humanoid 虛擬形象。"));
                GuiControls.EndCard();
                return;
            }

            _settings.idleMotionEnabled = GUILayout.Toggle(
                _settings.idleMotionEnabled, Loc.T("Breathing and sway", "呼吸・体の揺れ", "호흡 · 몸 흔들림",
                    "呼吸 · 身体摇摆", "呼吸 · 身體搖擺"));

            // OFF の間は強さの調整を出さない
            if (!_settings.idleMotionEnabled)
            {
                GuiControls.EndCard();
                return;
            }

            GuiControls.Hint(Loc.T("Head motion pauses while face tracking is active.",
                "顔のトラッキング中は、頭のゆらぎを止めて本人の動きに任せます。",
                "얼굴 트래킹 중에는 머리 흔들림을 멈추고 본인의 움직임을 따릅니다.",
                "面部追踪时会停止头部晃动，跟随本人的动作。",
                "臉部追蹤時會停止頭部晃動，跟隨本人的動作。"));

            // 強さ（0 = 動かさない、1 = 標準）と速さの倍率
            float max = AppSettings.MaxIdleMotionStrength;
            _settings.idleBreathing = GuiControls.Slider(
                Loc.T("Breathing", "呼吸", "호흡", "呼吸", "呼吸"), _settings.idleBreathing, 0f, max);
            _settings.idleSway = GuiControls.Slider(
                Loc.T("Body sway", "体の揺れ", "몸 흔들림", "身体摇摆", "身體搖擺"), _settings.idleSway, 0f, max);
            _settings.idleHeadMotion = GuiControls.Slider(
                Loc.T("Head motion", "頭のゆらぎ", "머리 흔들림", "头部晃动", "頭部晃動"), _settings.idleHeadMotion, 0f, max);
            _settings.idleMotionSpeed = GuiControls.Slider(
                Loc.T("Speed", "速さ", "속도", "速度", "速度"), _settings.idleMotionSpeed,
                AppSettings.MinIdleMotionSpeed, AppSettings.MaxIdleMotionSpeed);
            GuiControls.EndCard();
        }

        private void DrawPhysics()
        {
            GuiControls.BeginCard(Loc.T("Physics", "揺れもの", "흔들림", "物理摆动", "物理擺動"));
            _settings.physicsEnabled = GUILayout.Toggle(
                _settings.physicsEnabled, Loc.T("PhysBone (approx.)", "揺れもの（PhysBone 近似）", "흔들림 (PhysBone 근사)",
                    "物理摆动（PhysBone 近似）", "物理擺動（PhysBone 近似）"));

            // 揺れものが無いアバターでは注記、あればチェーン数を表示
            if (_avatar.HasAvatar)
            {
                if (_physics == null)
                {
                    _physics = _avatar.Get<PhysBoneSimulator>();
                }

                int chains = _physics != null ? _physics.ChainCount : 0;
                GuiControls.Hint(_physics != null && _physics.IsAvailable
                    ? Loc.T($"{chains} chains", $"{chains} チェーン", $"{chains} 체인", $"{chains} 条链", $"{chains} 條鏈")
                    : Loc.T("No PhysBones in this avatar", "このアバターには PhysBone がありません",
                        "이 아바타에는 PhysBone이 없습니다", "此虚拟形象没有 PhysBone", "此虛擬形象沒有 PhysBone"));

                // 全体 ON かつチェーンがあるときだけ、個別一覧を出すトグル（既定は閉じたまま）
                if (_settings.physicsEnabled && _physics != null && _physics.IsAvailable)
                {
                    DrawPhysicsChains();
                }
            }

            GuiControls.EndCard();
        }

        private void DrawPhysicsChains()
        {
            _showChainList = GUILayout.Toggle(
                _showChainList, Loc.T("Choose chains", "チェーンを個別に選ぶ", "체인 개별 선택",
                    "单独选择链条", "單獨選擇鏈條"));

            // 閉じている間は一覧を出さない（個別の ON/OFF 自体は記録どおり効いたまま）
            if (!_showChainList)
            {
                GuiControls.Hint(Loc.T(
                    "Turn on to show each PhysBone chain. Choices are saved per avatar.",
                    "ON にすると各 PhysBone チェーンの一覧が出ます。選択はアバターごとに保存されます。",
                    "켜면 각 PhysBone 체인 목록이 나옵니다. 선택은 아바타별로 저장됩니다.",
                    "开启后显示各 PhysBone 链条。选择按虚拟形象分别保存。",
                    "開啟後顯示各 PhysBone 鏈條。選擇依虛擬形象分別儲存。"));
                return;
            }

            GuiControls.Hint(Loc.T(
                $"{_physics.EnabledCount} / {_physics.ChainCount} on. Names are the PhysBone root objects. Saved per avatar.",
                $"{_physics.EnabledCount} / {_physics.ChainCount} が ON。名前は PhysBone の root オブジェクトです。アバターごとに保存されます。",
                $"{_physics.EnabledCount} / {_physics.ChainCount} 켜짐. 이름은 PhysBone root 오브젝트입니다. 아바타별로 저장됩니다.",
                $"{_physics.EnabledCount} / {_physics.ChainCount} 开启。名称为 PhysBone 根对象。按虚拟形象分别保存。",
                $"{_physics.EnabledCount} / {_physics.ChainCount} 開啟。名稱為 PhysBone 根物件。依虛擬形象分別儲存。"));

            bool changed = false;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(Loc.T("All on", "すべて ON", "모두 켜기", "全部开启", "全部開啟"), GuiControls.Shrinkable))
            {
                _physics.SetAll(true);
                changed = true;
            }

            if (GUILayout.Button(Loc.T("All off", "すべて OFF", "모두 끄기", "全部关闭", "全部關閉"), GuiControls.Shrinkable))
            {
                _physics.SetAll(false);
                changed = true;
            }

            GUILayout.EndHorizontal();

            float height = Mathf.Min(MaxChainListHeight, Mathf.Max(ChainRowHeight, _physics.ChainCount * ChainRowHeight));
            _chainScroll = GUILayout.BeginScrollView(
                _chainScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(height));
            foreach (PhysBoneSimulator.Chain chain in _physics.Chains)
            {
                bool enabled = GUILayout.Toggle(chain.Enabled, chain.Label, GuiControls.Shrinkable);
                if (enabled != chain.Enabled)
                {
                    _physics.SetEnabled(chain, enabled);
                    changed = true;
                }
            }

            GUILayout.EndScrollView();

            // 変えたらこのアバターの分として記録する（保存は終了時）
            if (changed && _session.Current != null)
            {
                _settings.SetPhysBoneToggles(_session.Current.SourcePath, _physics.Export());
            }
        }

        private void DrawPropAnimations()
        {
            GuiControls.BeginCard(Loc.T("Prop animations", "小物のアニメーション", "소품 애니메이션", "小物件动画", "小物件動畫"));
            _settings.propAnimationsEnabled = GUILayout.Toggle(
                _settings.propAnimationsEnabled, Loc.T("Play automatically (like VRChat)", "自動で再生する（VRChat と同じ）",
                    "자동 재생 (VRChat과 동일)", "自动播放（与 VRChat 相同）", "自動播放（與 VRChat 相同）"));

            // 再生するものの数、無ければ書き出し直しの案内
            if (_avatar.HasAvatar)
            {
                var props = _avatar.Get<PropAnimationPlayer>();
                int animators = props != null ? props.AnimatorCount : 0;
                int motions = props != null ? props.FxMotionCount : 0;
                GuiControls.Hint(props != null && props.IsAvailable
                    ? Loc.T($"Prop animators: {animators}, motions from FX: {motions}",
                        $"小物の Animator: {animators}、FX の動き: {motions}",
                        $"소품 Animator: {animators}, FX 움직임: {motions}",
                        $"小物件的 Animator：{animators}，FX 动作：{motions}",
                        $"小物件的 Animator：{animators}，FX 動作：{motions}")
                    : Loc.T("No prop animations in this avatar. Avatars exported with an older exporter need to be exported again.",
                        "このアバターには小物のアニメーションがありません。以前の書き出しツールで書き出したアバターは、書き出し直すと取り込まれます。",
                        "이 아바타에는 소품 애니메이션이 없습니다. 이전 내보내기 도구로 내보낸 아바타는 다시 내보내면 포함됩니다.",
                        "此虚拟形象没有小物件动画。用旧版导出工具导出的虚拟形象，重新导出后即可包含。",
                        "此虛擬形象沒有小物件動畫。用舊版匯出工具匯出的虛擬形象，重新匯出後即可包含。"));
            }

            GuiControls.EndCard();
        }
    }
}
