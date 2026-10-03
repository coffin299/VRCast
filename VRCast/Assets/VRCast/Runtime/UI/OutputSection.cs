using System.Threading.Tasks;
using UnityEngine;
using VRCast.Output;

namespace VRCast.UI
{
    /// <summary>
    /// MainPanel 内の Output セクション（仮想カメラの ON/OFF、ドライバーの登録・解除と状態）。
    /// </summary>
    public class OutputSection
    {
        private readonly VirtualCameraOutput _output;

        // 現在の登録状態（表示時に毎回レジストリを読まないよう、開始時と登録・解除の後に更新）
        private VirtualCameraRegistration _registration;

        // 実行中の登録・解除と、最後の結果の表示
        private Task<string> _pending;
        private string _message = string.Empty;

        public OutputSection(VirtualCameraOutput output)
        {
            _output = output;
            _registration = output.GetRegistration();
        }

        public void Draw()
        {
            GUILayout.Label("Output");
            PollPending();
            _output.Enabled = GUILayout.Toggle(
                _output.Enabled, $" Virtual camera ({VirtualCameraInstaller.DeviceName})");

            // 無効時は詳細を出さない
            if (!_output.Enabled)
            {
                return;
            }

            DrawDriver();
            GUILayout.Label(_output.Status);
        }

        private void DrawDriver()
        {
            // 同梱されていなければ登録できない
            string folder = _output.BundledFolder;
            if (folder == null)
            {
                GUILayout.Label("Driver not bundled (StreamingAssets/UnityCapture)");
                return;
            }

            GUILayout.Label(DescribeRegistration(_registration));
            GUILayout.BeginHorizontal();

            // 登録（初回のみ必要。フォルダを移動したら登録し直す）
            GUI.enabled = _pending == null;
            string installLabel = _registration == VirtualCameraRegistration.NotInstalled
                ? "Install driver"
                : "Reinstall driver";
            if (GUILayout.Button(installLabel))
            {
                Run(folder, true);
            }

            // 解除（登録されているときのみ）
            GUI.enabled = _pending == null && _registration != VirtualCameraRegistration.NotInstalled;
            if (GUILayout.Button("Uninstall driver"))
            {
                Run(folder, false);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // 実行中・直前の結果
            if (_pending != null)
            {
                GUILayout.Label("Waiting for administrator approval...");
            }
            else if (_message.Length > 0)
            {
                GUILayout.Label(_message);
            }
        }

        private void Run(string folder, bool install)
        {
            // 管理者権限で regsvr32 を実行（UAC の確認が出る）
            _message = string.Empty;
            _pending = VirtualCameraInstaller.RunElevatedAsync(folder, install);
        }

        private void PollPending()
        {
            // 実行中でなければ何もしない
            if (_pending == null || !_pending.IsCompleted)
            {
                return;
            }

            // 結果を表示して登録状態を読み直す
            string error = _pending.Result;
            _message = error == null ? "Done (restart the receiving app if the camera is not listed)" : error;
            _registration = _output.GetRegistration();
            _pending = null;
        }

        private static string DescribeRegistration(VirtualCameraRegistration registration)
        {
            // 状態表示の文言
            switch (registration)
            {
                case VirtualCameraRegistration.Installed:
                    return "Driver: installed";
                case VirtualCameraRegistration.InstalledElsewhere:
                    return "Driver: installed from another folder (reinstall if the camera shows an error)";
                default:
                    return "Driver: not installed (install once, needs administrator)";
            }
        }
    }
}
