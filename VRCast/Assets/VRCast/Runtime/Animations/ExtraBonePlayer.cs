using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// 追加の表情に指定したクリップのうち、人体以外のボーンの動きを再生する。
    /// 表情プリセットに結び付いたクリップは、その表情が選ばれているあいだ再生する。
    /// ブレンドシェイプの無いクリップは、顔・表情タブの切り替えで 1 つだけ選ぶ。
    /// 表情側のクリップが再生中のときはそちらを優先し、止めたときは読み込み時の姿勢へ戻す。
    /// 人体のボーンは書き出されていないので、トラッキングや待機ポーズとは重ならない。
    /// </summary>
    public class ExtraBonePlayer : MonoBehaviour
    {
        // 小物（FX）のレイヤーより上。PlayMode.StopSameLayer でも小物は止まらない
        private const int Layer = 100;

        private const string LogCategory = "ExtraBone";

        // 停止時に戻す、読み込み時のローカル姿勢
        private struct RestPose
        {
            public Transform Target;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
        }

        // ブレンドシェイプを持たない、切り替えだけのクリップ
        private struct BoneOnly
        {
            public string Name;
            public string Clip;
        }

        private readonly List<BoneOnly> _boneOnly = new List<BoneOnly>();
        private readonly Dictionary<string, AnimationState> _states = new Dictionary<string, AnimationState>();
        private readonly List<RestPose> _rest = new List<RestPose>();

        private Animation _animation;

        // 今再生しているクリップ名（無ければ空）
        private string _active = string.Empty;

        // 表情が選んでいるクリップ（空 = 表情はボーンを動かさない）
        private string _expressionClip = string.Empty;

        // 切り替えで選んだボーンだけクリップ（-1 = なし）
        private int _manual = -1;

        public int Count => _boneOnly.Count;

        public int ManualIndex => _manual;

        /// <summary>
        /// 今再生しているクリップがあるなら true（表情経由でも切り替え経由でも）。
        /// </summary>
        public bool IsPlaying => _active.Length > 0;

        public void Initialize(Transform root, ExpressionSet expressions, ExpressionController controller)
        {
            _animation = root.GetComponent<Animation>();
            if (_animation != null)
            {
                // 追加のボーンだけを拾う。小物の自動再生（VRCastFx_）には含めない
                foreach (AnimationState state in _animation)
                {
                    if (state.clip == null || !state.name.StartsWith(AvatarPackageLayout.ExtraBoneClipPrefix,
                            System.StringComparison.Ordinal))
                    {
                        continue;
                    }

                    state.layer = Layer;
                    state.weight = 1f;
                    state.enabled = false;
                    state.wrapMode = state.clip.wrapMode == WrapMode.Default
                        ? WrapMode.ClampForever
                        : state.clip.wrapMode;
                    _states[state.name] = state;
                }

                _animation.cullingType = AnimationCullingType.AlwaysAnimate;
            }

            var expressionNames = new HashSet<string>();
            if (controller != null)
            {
                foreach (string name in controller.Names)
                {
                    expressionNames.Add(name);
                }
            }

            var cached = new HashSet<Transform>();
            if (expressions != null && expressions.presets != null)
            {
                foreach (ExpressionPreset preset in expressions.presets)
                {
                    if (preset == null || string.IsNullOrEmpty(preset.boneClip) || !_states.ContainsKey(preset.boneClip))
                    {
                        continue;
                    }

                    RememberRest(root, preset.bonePaths, cached);

                    // 表情一覧に出るプリセットは、その表情を選んだときに再生する
                    bool shownAsExpression = preset.values != null && preset.values.Length > 0
                        && expressionNames.Contains(preset.name);
                    if (!shownAsExpression)
                    {
                        _boneOnly.Add(new BoneOnly { Name = preset.name, Clip = preset.boneClip });
                    }
                }
            }

            if (_states.Count > 0)
            {
                VRCastLog.Info(LogCategory, $"Extra bone clips: {_states.Count}, bone-only: {_boneOnly.Count}.");
            }
        }

        public string NameAt(int index)
        {
            return index >= 0 && index < _boneOnly.Count ? _boneOnly[index].Name : string.Empty;
        }

        /// <summary>
        /// 表情が選んでいるボーンクリップを切り替える（空なら表情はボーンを動かさない）。
        /// 空のときは、切り替えで選んだクリップがあればそれを再生する。
        /// </summary>
        public void SetExpressionClip(string boneClip)
        {
            _expressionClip = boneClip ?? string.Empty;
            UpdatePlayback();
        }

        /// <summary>
        /// ボーンだけのクリップを選ぶ。同じものを選び直すと止める。表情のボーンが再生中のときは、そちらが優先される。
        /// </summary>
        public void ToggleManual(int index)
        {
            _manual = index == _manual || index < 0 || index >= _boneOnly.Count ? -1 : index;
            UpdatePlayback();
        }

        private void UpdatePlayback()
        {
            // 表情に結び付いたクリップがあればそれ、無ければ切り替えで選んだもの
            string next = _expressionClip.Length > 0
                ? _expressionClip
                : _manual >= 0 ? _boneOnly[_manual].Clip : string.Empty;
            if (next == _active)
            {
                return;
            }

            StopActive();
            if (next.Length == 0 || !_states.TryGetValue(next, out AnimationState state))
            {
                return;
            }

            state.time = 0f;
            state.enabled = true;
            state.weight = 1f;
            state.layer = Layer;
            if (_animation != null && _animation.isActiveAndEnabled)
            {
                // 同じレイヤーだけ止める。小物のレイヤーは動かしたままにする
                _animation.Play(state.name, PlayMode.StopSameLayer);
            }

            _active = next;
        }

        private void StopActive()
        {
            if (_active.Length == 0)
            {
                return;
            }

            if (_states.TryGetValue(_active, out AnimationState state))
            {
                state.enabled = false;
            }

            // 読み込み時の姿勢へ戻す（人体のボーンはここには含まれない）
            foreach (RestPose rest in _rest)
            {
                if (rest.Target == null)
                {
                    continue;
                }

                rest.Target.localPosition = rest.Position;
                rest.Target.localRotation = rest.Rotation;
                rest.Target.localScale = rest.Scale;
            }

            _active = string.Empty;
        }

        private void RememberRest(Transform root, string[] paths, HashSet<Transform> cached)
        {
            if (root == null || paths == null)
            {
                return;
            }

            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }

                Transform target = root.Find(path);
                if (target == null || !cached.Add(target))
                {
                    continue;
                }

                _rest.Add(new RestPose
                {
                    Target = target,
                    Position = target.localPosition,
                    Rotation = target.localRotation,
                    Scale = target.localScale,
                });
            }
        }
    }
}
