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
    /// 外部入力（フェイストラッキングの口の開き）は口の開き具合に使い、形は声が出ている間はマイクの母音、無音なら aa（口を開く形）。
    /// ニュートラル以外の表情を出している間、AppSettings.lipSyncPausedByExpression ならマイク分、
    /// trackingMouthPausedByExpression なら外部入力分を止める（両方なら元の値に戻る）。
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

        // 1 つの口の形で動かす BlendShape（_shapes の添字）と、出し切ったときの重み（0〜1）
        private readonly struct Target : System.IEquatable<Target>
        {
            public readonly int Shape;
            public readonly float Weight;

            public Target(int shape, float weight)
            {
                Shape = shape;
                Weight = weight;
            }

            public bool Equals(Target other) => Shape == other.Shape && Weight == other.Weight;
        }

        // 書き込み先の BlendShape（同じメッシュの同名は 1 つにまとめる）と、そのフレームの値（0〜1）
        private readonly List<BlendShapeOverlay> _shapes = new List<BlendShapeOverlay>();
        private readonly List<(string path, string name)> _shapeKeys = new List<(string, string)>();
        private float[] _values;

        // 母音ごとの口の形と、口を開く形（外部入力用）
        private readonly List<Target>[] _vowelTargets = new List<Target>[VowelAnalyzer.VowelCount];
        private List<Target> _openTargets = new List<Target>();
        private bool _hasVowels;

        private MicrophoneInput _microphone;
        private AppSettings _settings;
        private ExpressionController _expressions;

        public bool IsAvailable => _openTargets.Count > 0;

        /// <summary>
        /// 母音ごとの口の形を使える（あいうえおの口の形が 2 種類以上ある）なら true。
        /// </summary>
        public bool HasVowels => _hasVowels;

        /// <summary>
        /// 外部（フェイストラッキング）からの口の開き（0〜1）。マイクの音量と大きい方を口の開き具合に使う。
        /// </summary>
        public float ExternalLevel { get; set; }

        /// <summary>
        /// 読み上げの口の開き（0〜1）。0 より大きい間はマイクよりこちらを使い、口を開く形にする。
        /// </summary>
        public float SpeechLevel { get; set; }

        /// <summary>
        /// true の間はマイク・外部入力とも止めて口を元の形にする（パーフェクトシンク化の編集中など）。
        /// </summary>
        public bool Suspended { get; set; }

        /// <summary>
        /// expressions は表情中に口を止める設定のために見る（無ければ止めない）。
        /// </summary>
        public void Initialize(Transform root, LipSyncData data, MicrophoneInput microphone, AppSettings settings,
            ExpressionController expressions)
        {
            _microphone = microphone;
            _settings = settings;
            _expressions = expressions;

            // モードに応じて母音ごとの口の形を選ぶ（JawFlap は全母音で同じ口開閉）
            for (int v = 0; v < VowelAnalyzer.VowelCount; v++)
            {
                _vowelTargets[v] = CollectTargets(root, data, VowelVisemes[v]);
            }

            // 口を開く形は aa、無ければ見つかった最初の母音
            _openTargets = _vowelTargets[VowelAnalyzer.A];
            for (int v = 0; v < VowelAnalyzer.VowelCount && _openTargets.Count == 0; v++)
            {
                _openTargets = _vowelTargets[v];
            }

            // 口の形が無い母音は口を開く形で代用し、開く形と違う母音があれば母音を使える
            for (int v = 0; v < VowelAnalyzer.VowelCount; v++)
            {
                if (_vowelTargets[v].Count == 0)
                {
                    _vowelTargets[v] = _openTargets;
                }

                _hasVowels |= !System.Linq.Enumerable.SequenceEqual(_vowelTargets[v], _openTargets);
            }

            _values = new float[_shapes.Count];
        }

        private List<Target> CollectTargets(Transform root, LipSyncData data, int viseme)
        {
            var targets = new List<Target>();
            switch (data.mode)
            {
                case LipSyncData.ModeVisemeBlendShape when data.visemes.Length == AvatarDescriptorData.VisemeCount:
                    AddTarget(targets, root, data.meshPath, data.visemes[viseme], 1f);
                    break;
                case LipSyncData.ModeJawFlapBlendShape:
                    AddTarget(targets, root, data.meshPath, data.mouthOpenBlendShape, 1f);
                    break;
                case LipSyncData.ModeVisemeAnimation when data.visemePoses.Length == AvatarDescriptorData.VisemeCount:
                    // FX のアニメーションから読んだ口の形（複数メッシュ・途中の重みもそのまま使う）
                    foreach (BlendShapeValue value in data.visemePoses[viseme].values)
                    {
                        AddTarget(targets, root, value.path, value.blendShape, value.weight / 100f);
                    }

                    break;
            }

            return targets;
        }

        private void AddTarget(List<Target> targets, Transform root, string meshPath, string name, float weight)
        {
            int shape = AddShape(root, meshPath, name);
            if (shape >= 0 && weight > 0f)
            {
                targets.Add(new Target(shape, weight));
            }
        }

        private int AddShape(Transform root, string meshPath, string name)
        {
            // 名前が無ければ対象なし
            if (string.IsNullOrEmpty(name))
            {
                return -1;
            }

            // 同じメッシュの同じ名前は共有（別の母音に同じ BlendShape が割り当てられているアバター向け）
            int existing = _shapeKeys.IndexOf((meshPath ?? string.Empty, name));
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
            _shapeKeys.Add((meshPath ?? string.Empty, name));
            return _shapes.Count - 1;
        }

        private void LateUpdate()
        {
            // 対象が無ければ何もしない
            if (_openTargets.Count == 0)
            {
                return;
            }

            // 表情を出しているか（止める設定ごとに使う）
            bool expressionShown = _expressions != null && _expressions.Current >= 0;

            // 無効時・一時停止中・マイク無し・表情中にマイクを止める設定ならマイク分 0
            bool active = _settings.lipSyncEnabled && !Suspended && _microphone != null
                && !(expressionShown && _settings.lipSyncPausedByExpression);
            float level = active ? _microphone.Level : 0f;

            // 一時停止中・表情中にトラッキングの口を止める設定なら外部入力分 0
            float external = Suspended || (expressionShown && _settings.trackingMouthPausedByExpression)
                ? 0f
                : Mathf.Clamp01(ExternalLevel);

            // 口の開き具合はマイクの音量と外部入力（カメラの口の開き）の大きい方。
            // 読み上げ中はその音量だけで口を開き、マイクの母音では上書きしない
            float speech = Suspended ? 0f : Mathf.Clamp01(SpeechLevel);
            float amount = speech > 0f ? speech : Mathf.Max(level, external);
            if (speech > 0f)
            {
                level = 0f;
            }

            System.Array.Clear(_values, 0, _values.Length);

            // 声が出ている間はマイクの母音の重みで各 BlendShape へ配る（同じ BlendShape の母音は合算）。
            // カメラの開きを「あ」に足すと、い・う などの形が「あ」に潰れるため混ぜない
            if (level > 0f)
            {
                for (int v = 0; v < VowelAnalyzer.VowelCount; v++)
                {
                    float vowel = amount * _microphone.GetVowel(v);
                    foreach (Target target in _vowelTargets[v])
                    {
                        _values[target.Shape] += vowel * target.Weight;
                    }
                }
            }
            else
            {
                // 無音（カメラだけ）は口を開く形
                foreach (Target target in _openTargets)
                {
                    _values[target.Shape] += amount * target.Weight;
                }
            }

            // 上乗せ（0 なら元の値に戻る）
            for (int i = 0; i < _shapes.Count; i++)
            {
                _shapes[i].Write(Mathf.Clamp01(_values[i]) * 100f);
            }
        }    }
}
