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
            GuiControls.BeginCard(Loc.T("Virtual camera", "仮想カメラ"));
            GuiControls.Hint(Loc.T(
                "Use the avatar as a webcam in OBS, Discord, Zoom, etc. (this panel is not shown)",
                "OBS・Discord・Zoom などで Web カメラとして使えます（このパネルは映りません）"));
            _output.Enabled = GUILayout.Toggle(
                _output.Enabled, Loc.T("Output", "出力する") + $" ({VirtualCameraInstaller.DeviceName})");

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
                    "ドライバーが同梱されていません（StreamingAssets/UnityCapture）"));
                return;
            }

            GUILayout.Label(DescribeRegistration(_registration));
            GUILayout.BeginHorizontal();

            // 登録（初回のみ必要。フォルダを移動したら登録し直す）
            GUI.enabled = _pending == null;
            string installLabel = _registration == VirtualCameraRegistration.NotInstalled
                ? Loc.T("Install driver", "ドライバーを登録")
                : Loc.T("Reinstall driver", "ドライバーを再登録");
            if (GUILayout.Button(installLabel))
            {
                Run(folder, true);
            }

            // 解除（登録されているときのみ）
            GUI.enabled = _pending == null && _registration != VirtualCameraRegistration.NotInstalled;
            if (GUILayout.Button(Loc.T("Uninstall driver", "ドライバーを解除")))
            {
                Run(folder, false);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // 実行中・直前の結果（言語切替に追従するよう表示時に文言を決める）
            if (_pending != null)
            {
                GuiControls.Hint(Loc.T("Waiting for administrator approval...", "管理者の承認を待っています..."));
            }
            else if (_hasResult)
            {
                GuiControls.Hint(_error ?? Loc.T(
                    "Done (restart the receiving app if the camera is not listed)",
                    "完了（カメラが一覧に出ない場合は受け側アプリを再起動してください）"));
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
                    return Loc.T("Driver: installed", "ドライバー: 登録済み");
                case VirtualCameraRegistration.InstalledElsewhere:
                    return Loc.T("Driver: installed from another folder (reinstall if the camera shows an error)",
                        "ドライバー: 別フォルダで登録済み（カメラがエラーになる場合は再登録）");
                default:
                    return Loc.T("Driver: not installed (install once, needs administrator)",
                        "ドライバー: 未登録（初回のみ登録が必要、管理者権限）");
            }
        }
    }
}
