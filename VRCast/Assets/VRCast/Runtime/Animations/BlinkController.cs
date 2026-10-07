using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;
using VRCast.Core;

namespace VRCast.Animations
{
    /// <summary>
    /// まばたき。ランダム間隔の自動まばたき（AppSettings.autoBlink で ON/OFF）と、外部（トラッキング）からの左右別の閉じ具合。
    /// AppSettings.blinkPausedByExpression なら、ニュートラル以外の表情を出している間はどちらも止めて開いたままにする。
    /// 両目用の BlendShape には左右の小さい方（両目とも閉じている分）、片目用（ウインク）には左右の差分を上乗せし、二重に閉じないようにする。
    /// </summary>
    public class BlinkController : MonoBehaviour
    {
        // まばたき間隔（秒）の範囲
        private const float MinInterval = 2f;
        private const float MaxInterval = 6f;

        // 閉じる・閉じたまま・開くの時間（秒）
        private const float CloseDuration = 0.06f;
        private const float HoldDuration = 0.04f;
        private const float OpenDuration = 0.12f;
        private const float TotalDuration = CloseDuration + HoldDuration + OpenDuration;

        // BlendShape が受け持つ目（アバターから見た左右）
        private enum EyeSide
        {
            Both,
            Left,
            Right,
        }

        private readonly List<BlendShapeOverlay> _overlays = new List<BlendShapeOverlay>();
        private readonly List<EyeSide> _sides = new List<EyeSide>();
        private AppSettings _settings;
        private ExpressionController _expressions;
        private float _nextBlinkTime;
        private bool _hasBoth;

        // 外部入力（左右の閉じ具合 0〜1）と、その有無
        private bool _hasExternal;
        private float _externalLeft;
        private float _externalRight;

        // まばたき開始時刻（負なら非まばたき中）
        private float _blinkStart = -1f;

        public bool IsAvailable => _overlays.Count > 0;

        /// <summary>
        /// 片目だけ閉じられる（ウインク用 BlendShape がある）か。
        /// </summary>
        public bool HasWink { get; private set; }

        /// <summary>
        /// expressions は表情中にまばたきを止める設定のために見る（無ければ止めない）。
        /// </summary>
        public void Initialize(Transform root, EyelidData data, AppSettings settings, ExpressionController expressions)
        {
            _settings = settings;
            _expressions = expressions;

            // まばたき用 BlendShape（ウインクと同名なら片目用として扱う）
            foreach (string shape in data.blinkBlendShapes)
            {
                EyeSide side = shape == data.winkLeftBlendShape ? EyeSide.Left
                    : shape == data.winkRightBlendShape ? EyeSide.Right
                    : EyeSide.Both;
                Add(root, data.meshPath, shape, side);
            }

            // ウインク用 BlendShape（まばたき用と別名のときだけ追加。左右揃った場合のみ）
            bool winkPair = !string.IsNullOrEmpty(data.winkLeftBlendShape) && !string.IsNullOrEmpty(data.winkRightBlendShape);
            if (winkPair)
            {
                AddIfNew(root, data.meshPath, data.winkLeftBlendShape, data.blinkBlendShapes, EyeSide.Left);
                AddIfNew(root, data.meshPath, data.winkRightBlendShape, data.blinkBlendShapes, EyeSide.Right);
            }

            // 左右とも片目用があればウインク可能
            _hasBoth = _sides.Contains(EyeSide.Both);
            HasWink = _sides.Contains(EyeSide.Left) && _sides.Contains(EyeSide.Right);
            ScheduleNext();
        }

        /// <summary>
        /// 外部入力（アバターから見た左右の閉じ具合 0〜1）を設定する。設定中は自動まばたきより優先する。
        /// </summary>
        public void SetExternal(float left, float right)
        {
            _hasExternal = true;
            _externalLeft = Mathf.Clamp01(left);
            _externalRight = Mathf.Clamp01(right);
        }

        /// <summary>
        /// 外部入力を解除し、自動まばたきへ戻す。
        /// </summary>
        public void ClearExternal()
        {
            _hasExternal = false;
        }

        private void Add(Transform root, string meshPath, string shape, EyeSide side)
        {
            // 見つかった BlendShape だけを対象にする
            BlendShapeOverlay overlay = BlendShapeOverlay.Create(root, meshPath, shape);
            if (overlay != null)
            {
                _overlays.Add(overlay);
                _sides.Add(side);
            }
        }

        private void AddIfNew(Transform root, string meshPath, string shape, string[] existing, EyeSide side)
        {
            // 同じ BlendShape に 2 つの上乗せを作らない（互いの書き込みを元の値と誤認して閉じたままになる）
            if (System.Array.IndexOf(existing, shape) < 0)
            {
                Add(root, meshPath, shape, side);
            }
        }

        private void LateUpdate()
        {
            // 対象が無ければ何もしない
            if (!IsAvailable)
            {
                return;
            }

            // 表情中に止める設定なら、トラッキング・自動とも開いたまま（表情の目の形をそのまま見せる）
            if (IsPausedByExpression)
            {
                _blinkStart = -1f;
                Write(0f, 0f);
                return;
            }

            // 外部入力がある間はその値を使い、自動まばたきは止める
            if (_hasExternal)
            {
                _blinkStart = -1f;
                Write(_externalLeft, _externalRight);
                return;
            }

            // OFF の間は開いたまま（元の値）
            if (!_settings.autoBlink)
            {
                _blinkStart = -1f;
                Write(0f, 0f);
                return;
            }

            // 予定時刻になったらまばたき開始
            float now = Time.time;
            if (_blinkStart < 0f && now >= _nextBlinkTime)
            {
                _blinkStart = now;
            }

            // まばたき中なら経過時間から閉じ具合を求める
            float closed = 0f;
            if (_blinkStart >= 0f)
            {
                float elapsed = now - _blinkStart;
                closed = Evaluate(elapsed);

                // 終わったら次を予約
                if (elapsed >= TotalDuration)
                {
                    _blinkStart = -1f;
                    ScheduleNext();
                }
            }

            Write(closed, closed);
        }

        // ニュートラル以外の表情を出していて、その間は止める設定なら true
        private bool IsPausedByExpression =>
            _settings.blinkPausedByExpression && _expressions != null && _expressions.Current >= 0;

        private void Write(float left, float right)
        {
            // 両目とも閉じている分は両目用、残りは片目用（両目用が無ければ片目用だけで閉じる）
            float both = Mathf.Min(left, right);
            float leftOnly = _hasBoth ? left - both : left;
            float rightOnly = _hasBoth ? right - both : right;

            for (int i = 0; i < _overlays.Count; i++)
            {
                // 受け持つ目に応じた値を 0〜100 で上乗せ
                float value = _sides[i] == EyeSide.Both ? both : _sides[i] == EyeSide.Left ? leftOnly : rightOnly;
                _overlays[i].Write(value * 100f);
            }
        }

        private static float Evaluate(float elapsed)
        {
            // 閉じる途中
            if (elapsed < CloseDuration)
            {
                return elapsed / CloseDuration;
            }

            // 閉じたまま
            if (elapsed < CloseDuration + HoldDuration)
            {
                return 1f;
            }

            // 開く途中
            return 1f - Mathf.Clamp01((elapsed - CloseDuration - HoldDuration) / OpenDuration);
        }

        private void ScheduleNext()
        {
            // 次のまばたきをランダムな間隔で予約
            _nextBlinkTime = Time.time + Random.Range(MinInterval, MaxInterval);
        }
    }
}
