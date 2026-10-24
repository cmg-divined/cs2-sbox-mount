using System;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// The parameters CS2's shaders compute from a material's own values when it loads: the Expression(...) annotations of
/// their .vfx declarations (decompiled with Source 2 Viewer). Compiled materials keep only the authored values, so the
/// mount evaluates these the same way before handing them to its shaders.
/// </summary>
internal static class MaterialParameters
{
	/// <summary>
	/// SrgbGammaToLinear, as the game's shaders compute it.
	/// </summary>
	public static float SrgbToLinear( float value ) => value <= 0.04045f
		? value * 0.077399380505f
		: MathF.Pow( value * 0.947867334f + 0.052132703f, 2.4f );

	public static Vector3 SrgbToLinear( Vector3 value ) => new( SrgbToLinear( value.x ), SrgbToLinear( value.y ), SrgbToLinear( value.z ) );

	/// <summary>
	/// g_vTexCoordXform0 and 1: the colour texture coordinates' scale, rotation (degrees) about the center, and offset,
	/// as two rows (x, y, unused, offset). The center isn't scaled, as in the game's expression.
	/// </summary>
	public static (Vector4 Row0, Vector4 Row1) TexCoordXform( Vector2 scale, float rotation, Vector2 center, Vector2 offset )
	{
		var angle = rotation * 3.1415927f / 180f;
		var cos = MathF.Cos( angle );
		var sin = MathF.Sin( angle );

		return (
			new Vector4( cos * scale.x, -sin * scale.y, 0, center.x + offset.x + sin * center.y - cos * center.x ),
			new Vector4( sin * scale.x, cos * scale.y, 0, center.y + offset.y - (sin * center.x + cos * center.y) ));
	}

	/// <summary>
	/// g_vNormalTexCoordXform0 and 1: like <see cref="TexCoordXform"/>, but with the center scaled too (and the
	/// expression's 3.14159 for pi).
	/// </summary>
	public static (Vector4 Row0, Vector4 Row1) NormalTexCoordXform( Vector2 scale, float rotation, Vector2 center, Vector2 offset )
	{
		var angle = rotation * 3.14159f / 180f;
		var cos = MathF.Cos( angle );
		var sin = MathF.Sin( angle );

		return (
			new Vector4( cos * scale.x, -sin * scale.y, 0, center.x + offset.x + sin * scale.y * center.y - cos * scale.x * center.x ),
			new Vector4( sin * scale.x, cos * scale.y, 0, center.y + offset.y + sin * scale.x * center.x - cos * scale.y * center.y ));
	}

	/// <summary>
	/// g_vDetailTexCoordXform (a 2x2 matrix, rows in xy and zw) and the offset the expression rewrites
	/// g_vDetailTexCoordOffset to, which rotates about the texture's middle.
	/// </summary>
	public static (Vector4 Xform, Vector2 Offset) DetailTexCoordXform( Vector2 scale, float rotation, Vector2 offset )
	{
		var angle = rotation * 3.1415927f / 180f;
		var cos = MathF.Cos( angle );
		var sin = MathF.Sin( angle );

		return (
			new Vector4( scale.x * cos, scale.x * -sin, scale.y * sin, scale.y * cos ),
			new Vector2( -0.5f * scale.x * (cos - sin) + offset.x + 0.5f, -0.5f * scale.y * (sin + cos) + offset.y + 0.5f ));
	}

	/// <summary>
	/// g_vSelfIllumTintScaled: pow( 2, brightness ) * scale * SrgbGammaToLinear( tint ).
	/// </summary>
	public static Vector3 SelfIllumTintScaled( float brightness, float scale, Vector3 tint ) => SrgbToLinear( tint ) * (MathF.Pow( 2, brightness ) * scale);

	/// <summary>
	/// A normal map rotation as the game's g_vNormalXform expressions build it: float4( cos, -sin, sin, cos ).
	/// </summary>
	public static Vector4 RotationXform( float rotation )
	{
		var angle = rotation * 3.1415927f / 180f;
		return new Vector4( MathF.Cos( angle ), -MathF.Sin( angle ), MathF.Sin( angle ), MathF.Cos( angle ) );
	}

	/// <summary>
	/// The albedo levels automatic PBR colour fitting uses, from the game's expression on its constants:
	/// float3( -x, -1.4427 * log( max( 0.0001, 1 - y ) ), 2 - z ).
	/// </summary>
	public static Vector3 AlbedoLevels( float x, float y, float z ) => new( -x, -1.4427f * MathF.Log( MathF.Max( 0.0001f, 1 - y ) ), 2 - z );

	/// <summary>
	/// g_flReflectance: the specular reflectance at normal incidence of an index of refraction of 1.5,
	/// pow( ( 1.5 - 1 ) / ( 1.5 + 1 ), 2 ).
	/// </summary>
	public static readonly float Reflectance = MathF.Pow( (1.5f - 1f) / (1.5f + 1f), 2f );
}
