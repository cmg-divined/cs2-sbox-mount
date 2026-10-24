using System;
using System.Numerics;
namespace CS2.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = CS2.Animation.ModelAnimation2.AnimationGraph;
using Transform = CS2.Animation.ModelAnimation.FrameBone;
[KV3Transfer]
public partial struct Target
{
    /// <summary>
    /// Either the actual transform or the offsets that need to be applied
    /// </summary>
    public Transform Transform { get; set; }
    public StringToken BoneID { get; }
    public bool IsBoneTarget { get; }
    public bool IsUsingBoneSpaceOffsets { get; set; } = true;
    public bool HasOffsets { get; set; }
    public bool IsSet { get; set; }
}
