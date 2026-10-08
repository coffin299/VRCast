using UnityEngine;
using VRCast.Animations;
using VRCast.Avatars;
using VRCast.Core;
using VRCast.Platform;

namespace VRCast.UI
{
    /// <summary>
    /// リセット（顔の向き・視線・表情・カメラ）のショートカットキーを毎フレーム見て実行する（パネルを隠していても動く）。
    /// 表示中のアバターの表情に同じ組み合わせが割り当てられていれば、表情を優先してそのリセットは行わない。
    /// </summary>
    public class ResetHotkeyListener : MonoBehaviour
    {
        // リセットの種類の順の組み合わせ（毎フレーム設定から詰め直す）
        private readonly KeyCombo[] _combos = new KeyCombo[ResetHotkey.ActionCount];
        private readonly HotkeyPoller _poller = new HotkeyPoller();

        private ResetActions _actions;
        private AvatarComponentCache _avatar;
        private AppSettings _settings;

        public void Initialize(ResetActions actions, AvatarSession session, AppSettings settings)
        {
            _actions = actions;
            _avatar = new AvatarComponentCache(session);
            _settings = settings;
        }

        private void Update()
        {
            // 未初期化なら何もしない
            if (_settings == null)
            {
                return;
            }

            // 表情のショートカットと同じ組み合わせは外す（表情を優先し、1 つのキーで 2 つの操作をしない）
            _avatar.Refresh();
            var expressions = _avatar.Get<ExpressionController>();
            for (int i = 0; i < _combos.Length; i++)
            {
                KeyCombo combo = _settings.GetResetHotkey((ResetAction)i);
                bool taken = expressions != null && expressions.TryFindHotkey(combo, out _);
                _combos[i] = taken ? KeyCombo.None : combo;
            }

            // 押された組み合わせのリセットを行う（実行できない状態なら何もしない）
            int pressed = _poller.Poll(_combos, _settings.resetHotkeysInBackground);
            if (pressed >= 0)
            {
                _actions.Run((ResetAction)pressed);
            }
        }
    }
}
