using System.Threading.Tasks;
using UnityEngine;
using VRCast.Output;
using VRCast.Rendering;

namespace VRCast.UI
{
    /// <summary>
    /// Output タブ（仮想カメラの ON/OFF、ドライバーの登録・解除と状態、Spout2 の ON/OFF と状態）。
    /// </summary>
    public class OutputSection
    {
        private readonly VirtualCameraOutput _output;
        private readonly SpoutOutput _spout;
        private readonly RenderingController _rendering;

        // 現在の登録状態（表示時に毎回レジストリを読まないよう、開始時・方式の切り替え時・登録・解除の後に更新）と、読んだときの方式
        private VirtualCameraRegistration _registration;
        private bool _registrationMediaFoundation;

        // 実行中の登録・解除と、最後の結果（エラー文言、成功時は null）
        private Task<string> _pending;
        private bool _hasResult;
        private string _error;

        public OutputSection(VirtualCameraOutput output, SpoutOutput spout, RenderingController rendering)
        {
            _output = output;
            _spout = spout;
            _rendering = rendering;
            _registration = output.GetRegistration();
            _registrationMediaFoundation = output.UseMediaFoundation;
        }

        public void Draw()
        {
            PollPending();

            // 方式が変わったら（切り替え・全設定のリセット）その方式の登録状態を読み直し、前の方式の結果は消す
            if (_pending == null && _output.UseMediaFoundation != _registrationMediaFoundation)
            {
                _registrationMediaFoundation = _output.UseMediaFoundation;
                _registration = _output.GetRegistration();
                _hasResult = false;
            }

            GuiControls.BeginCard(Loc.T("Virtual camera", "仮想カメラ", "가상 카메라", "虚拟摄像头", "虛擬攝影機"));
            GuiControls.Hint(Loc.T(
                "Use the avatar as a webcam in OBS, Discord, Zoom, etc. (this panel is not shown)",
                "OBS・Discord・Zoom などで Web カメラとして使えます（このパネルは映りません）",
                "OBS·Discord·Zoom 등에서 웹캠으로 사용할 수 있습니다 (이 패널은 표시되지 않습니다)",
                "可在 OBS、Discord、Zoom 等中作为摄像头使用（不会显示此面板）",
                "可在 OBS、Discord、Zoom 等中作為網路攝影機使用（不會顯示此面板）"));
            _output.Enabled = GUILayout.Toggle(
                _output.Enabled,
                Loc.T("Output", "出力する", "출력하기", "输出", "輸出") + $" ({_output.DeviceName})");

            // 有効時のみ詳細を出す
            if (_output.Enabled)
            {
                DrawMethod();
                DrawDriver();
                GuiControls.Hint(DescribeState(_output));
            }

            GuiControls.EndCard();
            DrawSpout();
        }

        private void DrawSpout()
        {
            GuiControls.BeginCard("Spout2");
            GuiControls.Hint(Loc.T(
                "Share the image with OBS on the GPU (lighter than the virtual camera, keeps transparency). " +
                "In OBS, add a \"Spout2 Capture\" source (needs the Spout2 plugin for OBS)",
                "OBS へ GPU 上で映像を渡します（仮想カメラより軽く、透過もそのまま）。" +
                "OBS では「Spout2 Capture」ソースを追加してください（OBS 用 Spout2 プラグインが必要）",
                "OBS에 GPU에서 영상을 전달합니다 (가상 카메라보다 가볍고 투명도 유지). " +
                "OBS에서 「Spout2 Capture」 소스를 추가하세요 (OBS용 Spout2 플러그인 필요)",
                "在 GPU 上将画面传给 OBS（比虚拟摄像头更轻，并保留透明）。" +
                "请在 OBS 中添加“Spout2 Capture”来源（需要 OBS 的 Spout2 插件）",
                "在 GPU 上將畫面傳給 OBS（比虛擬攝影機更輕，並保留透明）。" +
                "請在 OBS 中新增「Spout2 Capture」來源（需要 OBS 的 Spout2 外掛）"));
            _spout.Enabled = GUILayout.Toggle(
                _spout.Enabled,
                Loc.T("Output", "出力する", "출력하기", "输出", "輸出") + $" ({SpoutOutput.SenderName})");

            // 有効時のみ状態と透過の設定を出す
            if (_spout.Enabled)
            {
                GuiControls.Hint(_spout.Status);
                DrawSpoutTransparency();
            }

            GuiControls.EndCard();
        }

        private void DrawSpoutTransparency()
        {
            // 背景の透過は表示タブの設定と共通（Spout2 はカメラの描画結果のアルファをそのまま送る）
            _rendering.TransparentBackground = GUILayout.Toggle(
                _rendering.TransparentBackground,
                Loc.T("Transparent background", "背景を透過する", "배경을 투명하게", "背景透明", "背景透明"));

            // OBS の Spout2 Capture は合成モードの既定が不透明のため、透過時は切り替えを案内する
            if (_rendering.TransparentBackground)
            {
                GuiControls.Hint(Loc.T(
                    "In the OBS Spout2 Capture source properties, set \"Composite mode\" to \"Default\" "
                    + "(the background stays opaque otherwise)",
                    "OBS の Spout2 Capture ソースのプロパティで「Composite mode」を「Default」にしてください"
                    + "（そのままだと背景が透過しません）",
                    "OBS의 Spout2 Capture 소스 속성에서 「Composite mode」를 「Default」로 설정하세요 "
                    + "(그대로 두면 배경이 투명해지지 않습니다)",
                    "请在 OBS 的 Spout2 Capture 来源属性中将“Composite mode”设为“Default”"
                    + "（否则背景不会透明）",
                    "請在 OBS 的 Spout2 Capture 來源屬性中將「Composite mode」設為「Default」"
                    + "（否則背景不會透明）"));
            }
        }

        private void DrawMethod()
        {
            // Windows 11 の方式（Media Foundation）への切り替え。対応していない環境では選べない
            bool supported = MediaFoundationCamera.IsSupported;
            GUI.enabled = supported && _pending == null;
            bool useMediaFoundation = GUILayout.Toggle(_output.UseMediaFoundation, Loc.T(
                "Use the Windows 11 method (Media Foundation)",
                "Windows 11 の方式（Media Foundation）で出力する",
                "Windows 11 방식 (Media Foundation)으로 출력",
                "使用 Windows 11 方式（Media Foundation）输出",
                "使用 Windows 11 方式（Media Foundation）輸出"));
            GUI.enabled = true;

            if (supported)
            {
                _output.UseMediaFoundation = useMediaFoundation;
            }

            if (!MediaFoundationCamera.PluginFound)
            {
                GuiControls.Hint(Loc.T("Not bundled (VRCastVirtualCamera.dll)",
                    "同梱されていません（VRCastVirtualCamera.dll）",
                    "포함되어 있지 않습니다 (VRCastVirtualCamera.dll)",
                    "未附带（VRCastVirtualCamera.dll）",
                    "未附帶（VRCastVirtualCamera.dll）"));
            }
            else if (!supported)
            {
                GuiControls.Hint(Loc.T("Requires Windows 11 or later", "Windows 11 以降が必要です",
                    "Windows 11 이상이 필요합니다", "需要 Windows 11 或更高版本", "需要 Windows 11 或更新版本"));
            }
            else
            {
                GuiControls.Hint(Loc.T(
                    "Turn on if Discord etc. says the camera failed to start. Shown as a regular Windows camera named "
                    + $"\"{MediaFoundationCamera.DeviceName}\" (needs its own driver install)",
                    "Discord などで「カメラの起動に失敗しました」と出るときに ON。Windows の通常のカメラとして"
                    + $"「{MediaFoundationCamera.DeviceName}」の名前で表示されます（ドライバーの登録は別に必要）",
                    "Discord 등에서 「카메라 시작에 실패했습니다」가 나올 때 켜세요. Windows의 일반 카메라로 "
                    + $"「{MediaFoundationCamera.DeviceName}」 이름으로 표시됩니다 (드라이버 등록은 별도로 필요)",
                    "在 Discord 等中提示“摄像头启动失败”时开启。将作为 Windows 的普通摄像头以"
                    + $"“{MediaFoundationCamera.DeviceName}”的名称显示（需另行安装驱动程序）",
                    "在 Discord 等中提示「攝影機啟動失敗」時開啟。將作為 Windows 的一般攝影機以"
                    + $"「{MediaFoundationCamera.DeviceName}」的名稱顯示（需另行安裝驅動程式）"));
            }
        }

        private void DrawDriver()
        {
            // 同梱されていなければ登録できない
            if (!_output.HasDriverFiles)
            {
                GuiControls.Hint(_output.UseMediaFoundation
                    ? Loc.T("Driver not bundled (VRCastVirtualCamera.dll)",
                        "ドライバーが同梱されていません（VRCastVirtualCamera.dll）",
                        "드라이버가 포함되어 있지 않습니다 (VRCastVirtualCamera.dll)",
                        "未附带驱动程序（VRCastVirtualCamera.dll）",
                        "未附帶驅動程式（VRCastVirtualCamera.dll）")
                    : Loc.T("Driver not bundled (StreamingAssets/UnityCapture)",
                        "ドライバーが同梱されていません（StreamingAssets/UnityCapture）",
                        "드라이버가 포함되어 있지 않습니다 (StreamingAssets/UnityCapture)",
                        "未附带驱动程序（StreamingAssets/UnityCapture）",
                        "未附帶驅動程式（StreamingAssets/UnityCapture）"));
                return;
            }

            GUILayout.Label(DescribeRegistration(_registration));
            GUILayout.BeginHorizontal();

            // 登録（初回のみ必要。フォルダを移動したら登録し直す）
            GUI.enabled = _pending == null;
            string installLabel = _registration == VirtualCameraRegistration.NotInstalled
                ? Loc.T("Install driver", "ドライバーを登録", "드라이버 등록", "安装驱动程序", "安裝驅動程式")
                : _registration == VirtualCameraRegistration.Outdated
                    ? Loc.T("Update driver", "ドライバーを更新", "드라이버 업데이트", "更新驱动程序", "更新驅動程式")
                    : Loc.T("Reinstall driver", "ドライバーを再登録", "드라이버 재등록", "重新安装驱动程序", "重新安裝驅動程式");
            if (GUILayout.Button(installLabel, GuiControls.Shrinkable))
            {
                Run(true);
            }

            // 解除（登録されているときのみ）
            GUI.enabled = _pending == null && _registration != VirtualCameraRegistration.NotInstalled;
            if (GUILayout.Button(Loc.T("Uninstall driver", "ドライバーを解除", "드라이버 해제", "卸载驱动程序", "解除安裝驅動程式"), GuiControls.Shrinkable))
            {
                Run(false);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // 実行中・直前の結果（言語切替に追従するよう表示時に文言を決める）
            if (_pending != null)
            {
                GuiControls.Hint(Loc.T("Waiting for administrator approval...", "管理者の承認を待っています...",
                    "관리자 승인을 기다리는 중...", "正在等待管理员批准...", "正在等待系統管理員核准..."));
            }
            else if (_hasResult)
            {
                GuiControls.Hint(_error ?? Loc.T(
                    "Done (restart the receiving app if the camera is not listed)",
                    "完了（カメラが一覧に出ない場合は受け側アプリを再起動してください）",
                    "완료 (카메라가 목록에 없으면 받는 쪽 앱을 다시 시작하세요)",
                    "已完成（若列表中没有摄像头，请重新启动接收端应用）",
                    "已完成（若清單中沒有攝影機，請重新啟動接收端應用程式）"));
            }
        }

        private void Run(bool install)
        {
            // 使用中の方式のドライバーを管理者権限で登録・解除（UAC の確認が出る）
            _hasResult = false;
            _error = null;
            _pending = _output.RunDriverAsync(install);
        }

        private void PollPending()
        {
            // 実行中でなければ何もしない
            if (_pending == null || !_pending.IsCompleted)
            {
                return;
            }

            // 結果を保持して登録状態を読み直す
            _error = _pending.Result;
            _hasResult = true;
            _registration = _output.GetRegistration();
            _pending = null;
        }

        private static string DescribeState(VirtualCameraOutput output)
        {
            // 送信状態と、次に何をすればよいかを表示言語で伝える（エラーの原因は英語のまま添える）
            string name = output.DeviceName;
            switch (output.State)
            {
                case VirtualCameraState.Sending:
                    return Loc.T($"Sending to the app showing {name}",
                        $"{name} を開いているアプリへ送信中",
                        $"{name}를 연 앱으로 전송 중",
                        $"正在发送到打开 {name} 的应用",
                        $"正在傳送到開啟 {name} 的應用程式");
                case VirtualCameraState.WaitingForApp:
                    return Loc.T(
                        $"Ready. Choose \"{name}\" as the camera in Discord, Zoom, OBS, etc. to show the avatar " +
                        "(restart that app if it is not listed)",
                        $"準備できました。Discord・Zoom・OBS などのカメラで「{name}」を選ぶと映ります" +
                        "（一覧に無ければそのアプリを再起動）",
                        $"준비되었습니다. Discord·Zoom·OBS 등의 카메라에서 「{name}」를 선택하면 표시됩니다" +
                        " (목록에 없으면 그 앱을 다시 시작)",
                        $"已就绪。在 Discord、Zoom、OBS 等的摄像头中选择“{name}”即可显示（若未列出，请重启该应用）",
                        $"已就緒。在 Discord、Zoom、OBS 等的攝影機中選擇「{name}」即可顯示（若未列出，請重新啟動該應用程式）");
                case VirtualCameraState.Error:
                    return Loc.T("Cannot send: ", "送信できません: ", "전송할 수 없습니다: ", "无法发送：", "無法傳送：")
                        + output.Status;
                case VirtualCameraState.Starting:
                    return Loc.T("Starting...", "開始しています...", "시작하는 중...", "正在启动...", "正在啟動...");
                default:
                    return Loc.T("Off", "停止中", "꺼짐", "已停止", "已停止");
            }
        }

        private static string DescribeRegistration(VirtualCameraRegistration registration)
        {
            // 状態表示の文言
            switch (registration)
            {
                case VirtualCameraRegistration.Installed:
                    return Loc.T("Driver: installed", "ドライバー: 登録済み", "드라이버: 등록됨",
                        "驱动程序：已安装", "驅動程式：已安裝");
                case VirtualCameraRegistration.InstalledElsewhere:
                    return Loc.T("Driver: installed from another folder (reinstall if the camera shows an error)",
                        "ドライバー: 別フォルダで登録済み（カメラがエラーになる場合は再登録）",
                        "드라이버: 다른 폴더에서 등록됨 (카메라 오류 시 재등록)",
                        "驱动程序：已从其他文件夹安装（摄像头出错时请重新安装）",
                        "驅動程式：已從其他資料夾安裝（攝影機出錯時請重新安裝）");
                case VirtualCameraRegistration.Outdated:
                    return Loc.T("Driver: an older version is installed (press \"Update driver\", needs administrator)",
                        "ドライバー: 古いバージョンが登録されています（「ドライバーを更新」を押してください、管理者権限）",
                        "드라이버: 이전 버전이 등록되어 있습니다 (「드라이버 업데이트」를 누르세요, 관리자 권한)",
                        "驱动程序：已安装旧版本（请按“更新驱动程序”，需管理员权限）",
                        "驅動程式：已安裝舊版本（請按「更新驅動程式」，需系統管理員權限）");
                default:
                    return Loc.T("Driver: not installed (install once, needs administrator)",
                        "ドライバー: 未登録（初回のみ登録が必要、管理者権限）",
                        "드라이버: 미등록 (처음 한 번 등록 필요, 관리자 권한)",
                        "驱动程序：未安装（仅首次需要安装，需管理员权限）",
                        "驅動程式：未安裝（僅首次需要安裝，需系統管理員權限）");
            }
        }
    }
}
