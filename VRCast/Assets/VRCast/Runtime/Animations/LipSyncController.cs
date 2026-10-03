using UnityEngine;
using VRCast.Audio;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// マイク音量で口を開閉する基本リップシンク。
    /// Viseme 方式は aa を、JawFlap 方式は口開閉 BlendShape を音量に応じて上乗せする。
    /// </summary>
    public class LipSyncController : MonoBehaviour
    {
        private BlendShapeOverlay _mouth;
        private MicrophoneInput _microphone;
        private AppSettings _settings;

        public bool IsAvailable => _mouth != null;

        public void Initialize(Transform root, LipSyncData data, MicrophoneInput microphone, AppSettings settings)
        {
            _microphone = microphone;
            _settings = settings;

            // モードに応じて口を開く BlendShape 名を選ぶ
            string shape = null;
            if (data.mode == LipSyncData.ModeVisemeBlendShape && data.visemes.Length == AvatarDescriptorData.VisemeCount)
            {
                shape = data.visemes[AvatarDescriptorData.VisemeAa];
            }
            else if (data.mode == LipSyncData.ModeJawFlapBlendShape)
            {
                shape = data.mouthOpenBlendShape;
            }

            // 見つからなければリップシンク無し
            _mouth = BlendShapeOverlay.Create(root, data.meshPath, shape);
        }

        private void LateUpdate()
        {
            // 対象が無ければ何もしない
            if (_mouth == null)
            {
                return;
            }

            // 無効時・マイク無しは 0（元の値に戻る）
            bool active = _settings.lipSyncEnabled && _microphone != null;
            _mouth.Write(active ? _microphone.Level * 100f : 0f);
        }
    }
}
