using UnityEngine;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// パネル下部に常に表示するリセットボタン（顔の向き・視線・表情・カメラ）。
    /// 配信中によく使う操作を、どのタブを開いていても押せるようにする（ショートカットキーと同じ ResetActions を使う）。
    /// ボタンの 2 行目に割り当て中のショートカットキーを出す。
    /// </summary>
    public class ResetBar
    {
        private readonly ResetActions _actions;
        private readonly AppSettings _settings;

        public ResetBar(ResetActions actions, AppSettings settings)
        {
            _actions = actions;
            _settings = settings;
        }

        public void Draw(GUIStyle style)
        {
            GUILayout.BeginHorizontal(style);
            GUILayout.Label(Loc.T("Reset", "リセット", "초기화", "重置", "重設"), UiTheme.Current.Hint,
                GUILayout.ExpandWidth(false));

            // 種類の順にボタンを並べ、実行できない状態のものは押せなくする
            for (int i = 0; i < ResetHotkey.ActionCount; i++)
            {
                var action = (ResetAction)i;
                GUI.enabled = _actions.CanRun(action);
                if (GUILayout.Button(ButtonLabel(action)))
                {
                    _actions.Run(action);
                }
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();
        }

        private string ButtonLabel(ResetAction action)
        {
            // 1 行目にリセットの名前、2 行目に割り当て中のキー（未割り当てならその旨）
            string key = KeyCaptureSession.Describe(_settings.GetResetHotkey(action));
            return ResetActions.Label(action) + "\n" + string.Format(
                Loc.T("Key: {0}", "キー: {0}", "키: {0}", "按键：{0}", "按鍵：{0}"), key);
        }
    }
}
