using System;
using System.Collections.Generic;
using UnityEngine;

namespace VRCast.Core
{
    /// <summary>
    /// カメラの視点（注視点・距離・向き・画角・画角による拡大の倍率）。アバターごとに settings.json へ保存する。
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

        // 体に入り込まないよう画角で拡大した倍率（1 = 拡大なし。旧版の記録は 0 で、拡大なし扱い）
        public float zoom;

        /// <summary>
        /// 全要素が有限値なら true（壊れた設定ファイルの値を使わないため）。
        /// </summary>
        public bool IsFinite => IsFiniteValue(target.x) && IsFiniteValue(target.y) && IsFiniteValue(target.z)
            && IsFiniteValue(distance) && IsFiniteValue(yaw) && IsFiniteValue(pitch) && IsFiniteValue(fieldOfView)
            && IsFiniteValue(zoom);

        /// <summary>
        /// 全要素が同じなら true（変化したときだけ記録するための比較）。
        /// </summary>
        public bool SameAs(CameraPose other)
        {
            return target == other.target && distance == other.distance && yaw == other.yaw
                && pitch == other.pitch && fieldOfView == other.fieldOfView && zoom == other.zoom;
        }

        private static bool IsFiniteValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// アバター（.vrcaster / .vrm のパス）ごとの記録。カメラの視点と見た目の設定（hasLook が false なら未記録）、
    /// BlendShape の上限（制限しているものだけ）、表情のショートカットキー（割り当てたものだけ）、
    /// パーティクルの ON/OFF（切り替えたことのあるアバターだけ）、オブジェクトの表示の ON/OFF（切り替えたものだけ）、最近使ったアバターの一覧に出す画像（サムネイル用フォルダ内のファイル名。空なら未設定）と、
    /// VRCast に入れた日時（UNIX 時刻の秒・UTC。0 なら不明）、まばたきに使う BlendShape の指定（未指定なら自動）。
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
        public List<ParticleToggle> particles = new List<ParticleToggle>();
        public List<PhysBoneToggle> physBones = new List<PhysBoneToggle>();
        public List<ObjectToggle> objects = new List<ObjectToggle>();
        public string thumbnail = string.Empty;
        public long addedAt;
        public BlinkShapeOverride blinkShapes = new BlinkShapeOverride();

        // 自動まばたきに使う .anim のパス（空なら使わない）
        public string blinkAnimation = string.Empty;
    }
}
