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

        /// <summary>
        /// 外部（フェイストラッキング）からの口の開き（0〜1）。マイク音量と大きい方を使う。
        /// </summary>
        public float ExternalLevel { get; set; }

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

            // 無効時・マイク無しはマイク分 0、外部入力と大きい方を上乗せ（両方 0 なら元の値に戻る）
            bool active = _settings.lipSyncEnabled && _microphone != null;
            float level = Mathf.Max(active ? _microphone.Level : 0f, Mathf.Clamp01(ExternalLevel));
            _mouth.Write(level * 100f);
        }
    }
}
