using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;

namespace VRCast.UI
{
    /// <summary>
    /// 操作・連携タブのショートカットキーのカード（リセット・カメラ目線・スクショのキーの割り当て・解除・既定へ戻す）。
    /// 表情のキーはアバターごとなので、顔・表情タブの表情の行で割り当てる。
    /// </summary>
    public class ShortcutSection
    {
        // 行のボタン幅
        private const float KeyButtonWidth = 120f;
        private const float ClearButtonWidth = 70f;

        private readonly AvatarComponentCache _avatar;
        private readonly AppSettings _settings;

        // リセットのショートカットキーの割り当て（対象 = ResetAction）
        private readonly KeyCaptureSession _capture = new KeyCaptureSession();

        public ShortcutSection(AvatarSession session, AppSettings settings)
        {
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        public void Draw()
        {
            GuiControls.BeginCard(Loc.T("Shortcut keys (reset / look at camera / screenshot / away)",
                "ショートカットキー（リセット・カメラ目線・スクショ・離席）", "단축키 (초기화·카메라 시선·스크린샷·자리 비움)",
                "快捷键（重置·看向镜头·截图·离开）", "快捷鍵（重設·看向鏡頭·截圖·離席）"));

            // 割り当てが決まったら記録する（同じ組み合わせのほかのリセットは外れる。保存は終了時）
            if (_capture.Update(out int target, out KeyCombo captured))
            {
                _settings.SetResetHotkey((ResetAction)target, captured);
            }

            // 割り当て中はやり方を、それ以外は使い方を案内する
            GuiControls.Hint(_capture.IsCapturing
                ? KeyCaptureSession.AssignHint
                : Loc.T(
                    "Runs the same resets, look-at-camera toggle, transparent screenshot and away toggle as the buttons at the bottom of the panel, even while the panel is hidden. " +
                    "The defaults are numeric keypad 1–7 (NumLock on). Click a key button to change it.",
                    "パネル下部のリセット・カメラ目線・スクショ・離席のボタンと同じ操作を、パネルを隠していてもキーで行えます。" +
                    "既定はテンキーの 1〜7（NumLock ON）です。キーのボタンを押すと変更できます。",
                    "패널 아래의 초기화·카메라 시선·스크린샷·자리 비움 버튼과 같은 동작을 패널을 숨긴 상태에서도 키로 실행합니다. " +
                    "기본값은 숫자 키패드 1~7 (NumLock 켜짐)입니다. 키 버튼을 누르면 변경할 수 있습니다.",
                    "可用按键执行与面板底部重置、看向镜头、截图、离开按钮相同的操作，隐藏面板时也有效。默认为小键盘 1～7（NumLock 开启）。点击按键按钮即可更改。",
                    "可用按鍵執行與面板底部重設、看向鏡頭、截圖、離席按鈕相同的操作，隱藏面板時也有效。預設為數字鍵台 1～7（NumLock 開啟）。點擊按鍵按鈕即可變更。"));
            GuiControls.Hint(Loc.T(
                "Expression shortcut keys are assigned per avatar in the Face & expressions tab.",
                "表情のショートカットキーは、顔・表情タブで表情ごとに割り当てます（アバターごとに保存）。",
                "표정 단축키는 얼굴·표정 탭에서 표정마다 할당합니다 (아바타별로 저장).",
                "表情的快捷键在“面部·表情”标签页中为每个表情分配（按虚拟形象分别保存）。",
                "表情的快捷鍵在「臉部·表情」分頁中為每個表情分配（依虛擬形象分別儲存）。"));

            // 表情の割り当てと比べるため、表示中のアバターの表情を取る（アバターが替わったときだけ取り直す）
            _avatar.Refresh();
            var expressions = _avatar.Get<ExpressionController>();
            for (int i = 0; i < ResetHotkey.ActionCount; i++)
            {
                DrawRow((ResetAction)i, expressions);
            }

            // 背面でも使うか（OBS などを操作中でもリセットできる）
            _settings.resetHotkeysInBackground = GUILayout.Toggle(_settings.resetHotkeysInBackground,
                Loc.T("Also work when VRCast is in the background", "VRCast が背面にあるときも使う",
                    "VRCast가 뒤에 있을 때도 사용", "VRCast 在后台时也可使用", "VRCast 在背景時也可使用"));

            // 既定（テンキー 1〜7）へ戻す
            if (GUILayout.Button(Loc.T("Restore default keys", "既定のキーに戻す", "기본 키로 되돌리기", "恢复默认按键",
                    "恢復預設按鍵"), GUILayout.ExpandWidth(false)))
            {
                _capture.Cancel();
                _settings.ResetResetHotkeys();
            }

            GuiControls.EndCard();
        }

        private void DrawRow(ResetAction action, ExpressionController expressions)
        {
            GUILayout.BeginHorizontal();

            // リセットの名前、キーのボタン（押すと割り当て開始、割り当て中の行をもう一度押すとやめる）
            GUILayout.Label(ResetActions.Label(action), GuiControls.Shrinkable);
            KeyCombo combo = _settings.GetResetHotkey(action);
            _capture.DrawKeyButton((int)action, combo, KeyButtonWidth);

            // 割り当てを外す（未割り当てなら押せない）
            GUI.enabled = combo.IsAssigned;
            if (GUILayout.Button(Loc.T("Clear", "解除", "해제", "清除", "清除"), GUILayout.Width(ClearButtonWidth)))
            {
                _settings.SetResetHotkey(action, KeyCombo.None);
            }

            GUI.enabled = true;
            GUILayout.EndHorizontal();

            // 表示中のアバターの表情と同じキーなら警告（表情が優先され、このリセットはキーでは動かない）
            if (expressions != null && expressions.TryFindHotkey(combo, out int preset))
            {
                string name = preset >= 0 && preset < expressions.Names.Count
                    ? expressions.Names[preset]
                    : Loc.T("Neutral", "ニュートラル", "무표정", "无表情", "無表情");
                GuiControls.Warning(string.Format(Loc.T(
                        "⚠ Same key as the expression \"{0}\". The expression takes priority, so this reset does not run.",
                        "⚠ 表情「{0}」と同じキーです。表情が優先され、このリセットは動きません。",
                        "⚠ 표정 「{0}」과(와) 같은 키입니다. 표정이 우선되어 이 초기화는 동작하지 않습니다.",
                        "⚠ 与表情“{0}”的按键相同。表情优先，此重置不会执行。",
                        "⚠ 與表情「{0}」的按鍵相同。表情優先，此重設不會執行。"),
                    name));
            }
        }
    }
}
