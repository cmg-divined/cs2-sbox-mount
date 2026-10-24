using System;
using System.Numerics;
namespace CS2.Animation;


using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;
using AnimationGraph = CS2.Animation.ModelAnimation2.AnimationGraph;
partial struct Percent
{
    /// <summary>Creates a percentage from a 0 to 1 fraction.</summary>
    public Percent(float value)
    {
        Value = value;
    }
}
