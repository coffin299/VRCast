using System.Collections.Generic;
using UnityEngine;
using VRCast.AvatarFormat;

namespace VRCast.Avatars
{
    /// <summary>
    /// 読み込んだ VRM の表示用情報。
    /// </summary>
    public sealed class VrmAvatarInfo
    {
        // VRM に書かれたアバター名（無ければファイル名）
        public string name = string.Empty;

        // 読み込んだ VRM の仕様バージョン（"1.0" / "0.x"）
        public string specVersion = string.Empty;

        // 作者（複数はカンマ区切り。空なら不明）
        public string authors = string.Empty;
    }

    /// <summary>
    /// VRM から変換した、.vrcaster の metadata と同じ形式のデータ。
    /// </summary>
    public sealed class VrmAvatarData
    {
        public VrmAvatarInfo Info = new VrmAvatarInfo();
        public ExpressionSet Expressions = new ExpressionSet();
        public AvatarDescriptorData Descriptor = new AvatarDescriptorData();
        public PhysBoneSet PhysBones = new PhysBoneSet();
    }

    /// <summary>
    /// VRM の表情が動かす 1 つの BlendShape（アバタールートからの相対パス・BlendShape 名・重み 0〜1）。
    /// </summary>
    public struct VrmMorphBinding
    {
        public string path;
        public string blendShape;
        public float weight;

        public VrmMorphBinding(string path, string blendShape, float weight)
        {
            this.path = path;
            this.blendShape = blendShape;
            this.weight = weight;
        }
    }

    /// <summary>
    /// 表情プリセットにする VRM の表情（感情のプリセットと独自の表情）。
    /// </summary>
    public sealed class VrmExpression
    {
        public string name = string.Empty;
        public List<VrmMorphBinding> bindings = new List<VrmMorphBinding>();
    }

    /// <summary>
    /// 揺れもののチェーンの 1 関節（パラメーターは VRM 1.0 の値）。
    /// </summary>
    public sealed class VrmSpringJoint
    {
        public Transform transform;
        public float stiffness = 1f;
        public float gravityPower;
        public Vector3 gravityDir = Vector3.down;
        public float dragForce = 0.4f;
        public float radius;
    }

    /// <summary>
    /// 揺れものの 1 チェーン（親 → 子の順の関節と、当たるコライダーの番号）。
    /// </summary>
    public sealed class VrmSpring
    {
        public List<VrmSpringJoint> joints = new List<VrmSpringJoint>();
        public List<int> colliders = new List<int>();
    }

    /// <summary>
    /// 揺れもののコライダーの形。
    /// </summary>
    public enum VrmColliderShape
    {
        Sphere,
        Capsule,
        Plane,
    }

    /// <summary>
    /// 揺れもののコライダー（位置・末端・法線は transform のローカル空間）。
    /// </summary>
    public sealed class VrmCollider
    {
        public Transform transform;
        public VrmColliderShape shape;
        public bool inside;
        public Vector3 offset;
        public Vector3 tail;
        public Vector3 normal = Vector3.up;
        public float radius;
    }
}
