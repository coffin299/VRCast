using System.Threading.Tasks;
using UnityEngine;
using VRCast.Output;

namespace VRCast.UI
{
    /// <summary>
    /// Output タブ（仮想カメラの ON/OFF、ドライバーの登録・解除と状態）。
    /// </summary>
    public class OutputSection
    {
        private readonly VirtualCameraOutput _output;

        // 現在の登録状態（表示時に毎回レジストリを読まないよう、開始時と登録・解除の後に更新）
        private VirtualCameraRegistration _registration;

        // 実行中の登録・解除と、最後の結果（エラー文言、成功時は null）
        private Task<string> _pending;
        private bool _hasResult;
        private string _error;

        public OutputSection(VirtualCameraOutput output)
        {
            _output = output;
            _registration = output.GetRegistration();
        }

        public void Draw()
        {
            PollPending();
            GuiControls.BeginCard(Loc.T("Virtual camera", "仮想カメラ", "가상 카메라", "虚拟摄像头", "虛擬攝影機"));
            GuiControls.Hint(Loc.T(
                "Use the avatar as a webcam in OBS, Discord, Zoom, etc. (this panel is not shown)",
                "OBS・Discord・Zoom などで Web カメラとして使えます（このパネルは映りません）",
                "OBS·Discord·Zoom 등에서 웹캠으로 사용할 수 있습니다 (이 패널은 표시되지 않습니다)",
                "可在 OBS、Discord、Zoom 等中作为摄像头使用（不会显示此面板）",
                "可在 OBS、Discord、Zoom 等中作為網路攝影機使用（不會顯示此面板）"));
            _output.Enabled = GUILayout.Toggle(
                _output.Enabled,
                Loc.T("Output", "出力する", "출력하기", "输出", "輸出") + $" ({VirtualCameraInstaller.DeviceName})");

            // 有効時のみ詳細を出す
            if (_output.Enabled)
            {
                DrawDriver();
                GuiControls.Hint(_output.Status);
            }

            GuiControls.EndCard();
        }

        private void DrawDriver()
        {
            // 同梱されていなければ登録できない
            string folder = _output.BundledFolder;
            if (folder == null)
            {
                GuiControls.Hint(Loc.T("Driver not bundled (StreamingAssets/UnityCapture)",
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
                : Loc.T("Reinstall driver", "ドライバーを再登録", "드라이버 재등록", "重新安装驱动程序", "重新安裝驅動程式");
            if (GUILayout.Button(installLabel, GuiControls.Shrinkable))
            {
                Run(folder, true);
            }

            // 解除（登録されているときのみ）
            GUI.enabled = _pending == null && _registration != VirtualCameraRegistration.NotInstalled;
            if (GUILayout.Button(Loc.T("Uninstall driver", "ドライバーを解除", "드라이버 해제", "卸载驱动程序", "解除安裝驅動程式"), GuiControls.Shrinkable))
            {
                Run(folder, false);
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

        private void Run(string folder, bool install)
        {
            // 管理者権限で regsvr32 を実行（UAC の確認が出る）
            _hasResult = false;
            _error = null;
            _pending = VirtualCameraInstaller.RunElevatedAsync(folder, install);
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
