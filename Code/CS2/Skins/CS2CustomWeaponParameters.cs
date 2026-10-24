using System;

namespace CS2;

/// <summary>
/// A paint material's parameters (csgo_customweapon.vfx) as <c>shaders/cs2_customweapon.shader</c> takes them: the
/// texture coordinate transforms and the other values the game's shader computes with its Expression()s (decompiled
/// with Source 2 Viewer), the shader's defaults for what the material doesn't set, and its textures.
/// </summary>
internal static class CS2CustomWeaponParameters
{
	/// <summary>
	/// The static combos and switches, by the attribute the shader takes each as.
	/// </summary>
	private static readonly (string Parameter, string Attribute)[] Flags =
	[
		("F_PAINT_STYLE", "CS2PaintStyle"),
		("F_ROUGHNESS_MODE", "CS2RoughnessMode"),
		("F_SEPARATE_CHANNEL_INPUTS", "CS2SeparateChannelInputs"),
		("F_ROUGHNESS_TEXTURE", "CS2RoughnessTexture"),
		("F_METALNESS_TEXTURE", "CS2MetalnessTexture"),
		("F_PEARLESCENCE_MASK", "CS2PearlescenceMask"),
		("F_USE_ALL_MASKS", "CS2UseAllMasks"),
		("F_SPRAYPAINT_HALFTONE", "CS2SpraypaintHalftone"),
		("F_ROUGHNESS_PER_COLOR", "CS2RoughnessPerColor"),
		("g_nPatternTextureHorizontalSampling", "g_nPatternTextureHorizontalSampling"),
		("g_nPatternTextureVerticalSampling", "g_nPatternTextureVerticalSampling"),
	];

	/// <summary>
	/// The textures, with the default the game's shader gives each (CreateInputTexture2D) as bytes in the texture's
	/// own encoding, or the compiled default file.
	/// </summary>
	private static readonly (string Name, Vector4 Default, string File)[] Textures =
	[
		("g_tMasks", new( 1, 1, 1, 1 ), null),
		("g_tPosition", new( 1, 1, 1, 1 ), null),
		("g_tSurface", new( 1, 1, 1, 1 ), null),
		("g_tColor", new( 1, 1, 1, 1 ), null),
		("g_tMetalness", new( 0.5f, 0, 0, 0 ), null),
		("g_tPattern", new( 1, 1, 1, 1 ), null),
		("g_tGrunge", new( 1, 1, 1, 1 ), "materials/models/weapons/customization/shared/gun_grunge_psd_f3419fed.vtex"),
		("g_tWear", new( 1, 1, 1, 1 ), "materials/models/weapons/customization/shared/paint_wear_psd_a2abffd8.vtex"),
		("g_tPaintRoughness", new( 0.5f, 0.5f, 0.5f, 0 ), null),
		("g_tPaintMetalness", new( 0, 0, 0, 0 ), null),
		("g_tPearlescenceMask", new( 1, 1, 1, 0 ), null),
	];

	public static void Set( RenderAttributes attributes, IReadOnlyDictionary<string, CS2CompositeValue> values, Func<string, Texture> load, Func<Vector4, Texture> solid )
	{
		float Float( string name, float fallback ) => values.TryGetValue( name, out var value ) && !value.IsText ? value.X : fallback;
		Vector4 Vector( string name, Vector4 fallback ) => values.TryGetValue( name, out var value ) && !value.IsText ? value.Number : fallback;
		bool Bool( string name, bool fallback ) => Float( name, fallback ? 1 : 0 ) != 0;

		foreach ( var (parameter, attribute) in Flags )
			attributes.Set( attribute, Float( parameter, 0 ) );

		var style = (int)MathF.Round( Float( "F_PAINT_STYLE", 0 ) );
		var separate = Bool( "F_SEPARATE_CHANNEL_INPUTS", false );

		// The transforms scale by the weapon: its UV scale (g_flUvScale1), or for the styles projected from its position
		// (spray paint, anodized airbrushed; also F_USE_ALL_MASKS and F_CASE_HARDENING_TRILINEAR) its length over 36
		// units; g_bIgnoreWeaponSizeScale leaves the scale as the paint's. The pixel shader's parameter block says so
		// for the pattern; that the vertex shader's wear and grunge transforms follow the same combos is inferred.
		var projected = style is 2 or 5 || Bool( "F_USE_ALL_MASKS", false ) || Bool( "F_CASE_HARDENING_TRILINEAR", false );
		var weaponScale = projected ? Float( "g_flWeaponLength1", 36 ) / 36f : Float( "g_flUvScale1", 1 );
		var ignoreWeaponScale = Bool( "g_bIgnoreWeaponSizeScale", false );

		void Xform( string name, float rotation, float scale, Vector4 offset )
		{
			var (row0, row1) = TexCoordXform( rotation, scale * (ignoreWeaponScale ? 1 : weaponScale), new Vector2( offset.x, offset.y ) );
			attributes.Set( name + "0", row0 );
			attributes.Set( name + "1", row1 );
		}

		var patternRotation = Float( "g_flPatternTexCoordRotation", 0 );
		var patternScale = Float( "g_flPatternTexCoordScale", 1 );
		var patternOffset = Vector( "g_vPatternTexCoordOffset", default );
		Xform( "g_vPatternTexCoordXform", patternRotation, patternScale, patternOffset );
		Xform( "g_vPatternTexCoordXformHalftoneA", Float( "g_flHalftoneRotationOffsetA", 0 ) + patternRotation, patternScale, patternOffset );
		Xform( "g_vPatternTexCoordXformHalftoneB", Float( "g_flHalftoneRotationOffsetB", 4 ) + patternRotation, patternScale, patternOffset );
		Xform( "g_vPatternTexCoordXformHalftoneC", Float( "g_flHalftoneRotationOffsetC", -4 ) + patternRotation, patternScale, patternOffset );
		Xform( "g_vWearTexCoordXform", Float( "g_flWearTexCoordRotation", 0 ), Float( "g_flWearTexCoordScale", 1 ), Vector( "g_vWearTexCoordOffset", default ) );
		Xform( "g_vGrungeTexCoordXform", Float( "g_flGrungeTexCoordRotation", 0 ), Float( "g_flGrungeTexCoordScale", 1 ), Vector( "g_vGrungeTexCoordOffset", default ) );

		attributes.Set( "g_flWearAmount", Float( "g_flWearAmount", 0 ) );
		attributes.Set( "g_fWearSoftness", Float( "g_fWearSoftness", 0 ) );

		// Colours: SrgbGammaToLinear( this ), from the shader's gamma defaults.
		attributes.Set( "g_vColor0", SrgbToLinear( Vector( "g_vColor0", new Vector4( 0.5f, 0.5f, 0.5f, 0 ) ) ) );
		attributes.Set( "g_vColor1", SrgbToLinear( Vector( "g_vColor1", new Vector4( 0.59f, 0.59f, 0.59f, 0 ) ) ) );
		attributes.Set( "g_vColor2", SrgbToLinear( Vector( "g_vColor2", new Vector4( 0.38f, 0.38f, 0.38f, 0 ) ) ) );
		attributes.Set( "g_vColor3", SrgbToLinear( Vector( "g_vColor3", new Vector4( 0.42f, 0.42f, 0.42f, 0 ) ) ) );
		attributes.Set( "g_flColorBrightness", Float( "g_flColorBrightness", 1 ) );
		attributes.Set( "g_nColorAdjustmentMode", Float( "g_nColorAdjustmentMode", 1 ) );

		// Durability: 1 - this.
		attributes.Set( "g_vPaintDurability", new Vector4( 1, 1, 1, 1 ) - Vector( "g_vPaintDurability", default ) );

		// The paint roughness defaults to 0.2 for the anodized multicoloured and airbrushed styles, 0.6 otherwise.
		attributes.Set( "g_flPaintRoughness", Float( "g_flPaintRoughness", style is 4 or 5 ? 0.2f : 0.6f ) );
		attributes.Set( "g_vPaintRoughness", Vector( "g_vPaintRoughness", new Vector4( 0.6f, 0.6f, 0.6f, 0.6f ) ) );
		attributes.Set( "g_flPaintMetalness", Float( "g_flPaintMetalness", 0 ) );
		attributes.Set( "g_vPaintMetalness", Vector( "g_vPaintMetalness", default ) );
		attributes.Set( "g_flPearlescentScale", Float( "g_flPearlescentScale", 0 ) );
		attributes.Set( "g_bPearlescentOnMetallicOnly", Float( "g_bPearlescentOnMetallicOnly", 0 ) );
		attributes.Set( "g_bRoughnessUsesPatternUVs", Float( "g_bRoughnessUsesPatternUVs", 1 ) );
		attributes.Set( "g_bMetalnessUsesPatternUVs", Float( "g_bMetalnessUsesPatternUVs", 0 ) );
		attributes.Set( "g_bPearlescenceMaskUsesPatternUVs", Float( "g_bPearlescenceMaskUsesPatternUVs", 1 ) );

		// g_vSprayBiasBlend: float3( g_bBiasSpray ? 1 : 0, g_vSprayBlend.x, g_vSprayBlend.y ).
		var sprayBlend = Vector( "g_vSprayBlend", new Vector4( 1, 1, 0, 0 ) );
		attributes.Set( "g_vSprayBiasBlend", new Vector3( Bool( "g_bBiasSpray", false ) ? 1 : 0, sprayBlend.x, sprayBlend.y ) );

		// g_vHalftonePatternLevels: float3( -x, -1.4427 * log( max( 0.0001, 1 - y ) ), 2 - z ), default ( 0, 0.5, 0 ).
		var levels = Vector( "g_vHalftonePatternLevels", new Vector4( 0, 0.5f, 0, 0 ) );
		attributes.Set( "g_vHalftonePatternLevels", new Vector3( -levels.x, -1.4427f * MathF.Log( MathF.Max( 0.0001f, 1 - levels.y ) ), 2 - levels.z ) );
		attributes.Set( "g_fHalftoneCavityCutoff", Float( "g_fHalftoneCavityCutoff", 1 ) );
		var thresholds = Vector( "g_vHalftoneThresholds", default );
		attributes.Set( "g_vHalftoneThresholds", new Vector2( thresholds.x, thresholds.y ) );
		attributes.Set( "g_bHalftoneInCavity", Float( "g_bHalftoneInCavity", 1 ) );

		// The ambient occlusion's default: separate channels' cavity 0.5, occlusion 1 and no-paint 0; packed white.
		attributes.Set( "g_tAmbientOcclusion", Texture( values, "g_tAmbientOcclusion", load ) ?? solid( separate ? new Vector4( 0.5f, 1, 0, 0 ) : new Vector4( 1, 1, 1, 1 ) ) );
		foreach ( var (name, fallback, file) in Textures )
			attributes.Set( name, Texture( values, name, load ) ?? (file is not null ? load( file ) : null) ?? solid( fallback ) );
	}

	private static Texture Texture( IReadOnlyDictionary<string, CS2CompositeValue> values, string name, Func<string, Texture> load )
	{
		return values.TryGetValue( name, out var value ) && value.IsText && !value.Text.StartsWith( CS2CompositeProgram.GeneratedPrefix, StringComparison.Ordinal ) ? load( value.Text ) : null;
	}

	/// <summary>
	/// The game's texture coordinate transform expression (csgo_customweapon's g_v*TexCoordXform0 and 1): rotation and
	/// scale (both rounded to hundredths) about the middle, then the offset (rounded too), as two rows.
	/// </summary>
	public static (Vector4 Row0, Vector4 Row1) TexCoordXform( float rotation, float scale, Vector2 offset )
	{
		static float Round( float x ) => MathF.Floor( (x + 0.005f) * 100f ) / 100f;

		var degrees = Round( rotation );
		var s = Round( scale );
		var angle = degrees * 3.1415927f / 180f;
		var cos = MathF.Cos( angle );
		var sin = MathF.Sin( angle );
		var half = 0.5f / (s != 0 ? s : 1);
		var cosBack = MathF.Cos( -angle );
		var sinBack = MathF.Sin( -angle );
		var v9 = half * cosBack - half * sinBack;
		var v10 = v9 * sinBack + half * cosBack;

		return (
			new Vector4( cos * s, -sin * s, 0, s * cos * v9 + s * -sin * v10 + (Round( offset.x ) - 0.5f) ),
			new Vector4( sin * s, cos * s, 0, s * sin * v9 + s * cos * v10 + (Round( offset.y ) - 0.5f) ));
	}

	private static float SrgbToLinear( float x ) => x <= 0.04045f ? x * 0.077399380505f : MathF.Pow( x * 0.947867334f + 0.052132703f, 2.4f );

	private static Vector3 SrgbToLinear( Vector4 x ) => new( SrgbToLinear( x.x ), SrgbToLinear( x.y ), SrgbToLinear( x.z ) );
}
