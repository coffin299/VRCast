using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// カメラの視点（注視点・距離・向き・画角）。アバターごとに settings.json へ保存する。
    /// 範囲の補正は適用する側（OrbitCameraController）が行う。
    /// </summary>
    [Serializable]
    public struct CameraPose
    {
        public Vector3 target;
        public float distance;
        public float yaw;
        public float pitch;
        public float fieldOfView;

        /// <summary>
        /// 全要素が有限値なら true（壊れた設定ファイルの値を使わないため）。
        /// </summary>
        public bool IsFinite => IsFiniteValue(target.x) && IsFiniteValue(target.y) && IsFiniteValue(target.z)
            && IsFiniteValue(distance) && IsFiniteValue(yaw) && IsFiniteValue(pitch) && IsFiniteValue(fieldOfView);

        /// <summary>
        /// 全要素が同じなら true（変化したときだけ記録するための比較）。
        /// </summary>
        public bool SameAs(CameraPose other)
        {
            return target == other.target && distance == other.distance && yaw == other.yaw
                && pitch == other.pitch && fieldOfView == other.fieldOfView;
        }

        private static bool IsFiniteValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// アバター（.vrcaster のパス）ごとの記録。カメラの視点と見た目の設定（hasLook が false なら未記録）、
    /// BlendShape の上限（制限しているものだけ）、表情のショートカットキー（割り当てたものだけ）。
    /// </summary>
    [Serializable]
    public class AvatarEntry
    {
        public string avatarPath = string.Empty;
        public CameraPose pose;
        public bool hasLook;
        public AvatarLook look;
        public List<BlendShapeLimit> blendShapeLimits = new List<BlendShapeLimit>();
        public List<ExpressionHotkey> expressionHotkeys = new List<ExpressionHotkey>();
    }
}
