using System.Collections.Generic;
using UnityEngine;
using VRCast.Audio;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// マイク音声で口を動かすリップシンク。
    /// Viseme 方式は母音（あいうえお）の重みで aa / ih / ou / E / oh を、JawFlap 方式は口開閉 BlendShape を音量に応じて上乗せする。
    /// 外部入力（フェイストラッキングの口の開き）は aa（口を開く形）に使う。
    /// </summary>
    public class LipSyncController : MonoBehaviour
    {
        // VowelAnalyzer の母音の並び（A, I, U, E, O）に対応する Viseme
        private static readonly int[] VowelVisemes =
        {
            AvatarDescriptorData.VisemeAa,
            AvatarDescriptorData.VisemeI,
            AvatarDescriptorData.VisemeU,
            AvatarDescriptorData.VisemeE,
            AvatarDescriptorData.VisemeO,
        };

        // 書き込み先の BlendShape（同名は 1 つにまとめる）と、そのフレームの値（0〜1）
        private readonly List<BlendShapeOverlay> _shapes = new List<BlendShapeOverlay>();
        private readonly List<string> _shapeNames = new List<string>();
        private float[] _values;

        // 母音ごとの書き込み先（_shapes の添字）と、口を開く形（外部入力用）
        private readonly int[] _vowelShape = new int[VowelAnalyzer.VowelCount];
        private int _openShape = -1;

        private MicrophoneInput _microphone;
        private AppSettings _settings;

        public bool IsAvailable => _openShape >= 0;

        /// <summary>
        /// Viseme 方式で母音ごとの口の形を使える（あいうえおの BlendShape が 2 種類以上ある）なら true。
        /// </summary>
        public bool HasVowels => _shapes.Count >= 2;

        /// <summary>
        /// 外部（フェイストラッキング）からの口の開き（0〜1）。マイクの口の形と大きい方を使う。
        /// </summary>
        public float ExternalLevel { get; set; }

        public void Initialize(Transform root, LipSyncData data, MicrophoneInput microphone, AppSettings settings)
        {
            _microphone = microphone;
            _settings = settings;

            // モードに応じて母音ごとの BlendShape を選ぶ（JawFlap は全母音で同じ口開閉）
            bool visemes = data.mode == LipSyncData.ModeVisemeBlendShape
                && data.visemes.Length == AvatarDescriptorData.VisemeCount;
            for (int v = 0; v < VowelAnalyzer.VowelCount; v++)
            {
                string name = visemes
                    ? data.visemes[VowelVisemes[v]]
                    : data.mode == LipSyncData.ModeJawFlapBlendShape ? data.mouthOpenBlendShape : null;
                _vowelShape[v] = AddShape(root, data.meshPath, name);
            }

            // 口を開く形は aa、無ければ見つかった最初の母音
            _openShape = _vowelShape[VowelAnalyzer.A];
            for (int v = 0; v < VowelAnalyzer.VowelCount && _openShape < 0; v++)
            {
                _openShape = _vowelShape[v];
            }

            // BlendShape が無い母音は口を開く形で代用
            for (int v = 0; v < VowelAnalyzer.VowelCount; v++)
            {
                _vowelShape[v] = _vowelShape[v] >= 0 ? _vowelShape[v] : _openShape;
            }

            _values = new float[_shapes.Count];
        }

        private int AddShape(Transform root, string meshPath, string name)
        {
            // 名前が無ければ対象なし
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            // 同じ名前は共有（別の母音に同じ BlendShape が割り当てられているアバター向け）
            int existing = _shapeNames.IndexOf(name);
            if (existing >= 0)
            {
                return existing;
            }

            // メッシュに無ければ対象なし
            BlendShapeOverlay overlay = BlendShapeOverlay.Create(root, meshPath, name);
            if (overlay == null)
            {
                return -1;
            }

            _shapes.Add(overlay);
            _shapeNames.Add(name);
            return _shapes.Count - 1;
        }

        private void LateUpdate()
        {
            // 対象が無ければ何もしない
            if (_openShape < 0)
            {
                return;
            }

            // 無効時・マイク無しはマイク分 0
            bool active = _settings.lipSyncEnabled && _microphone != null;
            float level = active ? _microphone.Level : 0f;

            // 音量 × 母音の重みを各 BlendShape へ配る（同じ BlendShape の母音は合算）
            System.Array.Clear(_values, 0, _values.Length);
            for (int v = 0; v < VowelAnalyzer.VowelCount; v++)
            {
                _values[_vowelShape[v]] += level * (active ? _microphone.GetVowel(v) : 0f);
            }

            // 外部入力は口を開く形と大きい方
            _values[_openShape] = Mathf.Max(_values[_openShape], Mathf.Clamp01(ExternalLevel));

            // 上乗せ（0 なら元の値に戻る）
            for (int i = 0; i < _shapes.Count; i++)
            {
                _shapes[i].Write(Mathf.Clamp01(_values[i]) * 100f);
            }
        }
    }
}
