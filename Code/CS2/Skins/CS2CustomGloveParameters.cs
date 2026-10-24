using System;

namespace CS2;

/// <summary>
/// A glove paint material's parameters (csgo_customglove.vfx) as <c>shaders/cs2_customglove.shader</c> takes them: the
/// texture coordinate transforms and the other values the game's shader computes with its Expression()s (decompiled
/// with Source 2 Viewer), the shader's defaults for what the material doesn't set, and its textures.
/// The glove vcompmat's passes pick the output by F_OUTPUT_MODE: 0 g_tColor (to be stored sRGB encoded: the caller sets
/// CS2CompositeSrgbOutput to 1), 1 g_tNormal, 2 g_tMetalness, 3 g_tAmbientOcclusion and 4 g_tAnisoGloss (stored as they
/// are). Only the Source1 texture layout (F_BACKWARDS_COMPATIBILITY 0) is ported, which every glove paint in the game
/// uses; a material with the Source2 height blending layout (only the workshop template) is composited as if it had the
/// other.
/// </summary>
internal static class CS2CustomGloveParameters
{
	/// <summary>
	/// The switch and static combo, by the attribute the shader takes each as (F_OUTPUT_MODE is the game shader's
	/// g_nOutputMode), and the pattern mode.
	/// </summary>
	private static readonly (string Parameter, string Attribute)[] Flags =
	[
		("F_OUTPUT_MODE", "CS2OutputMode"),
		("F_ANISOTROPIC_GLOSS", "CS2AnisotropicGloss"),
		("g_nPatternMode", "g_nPatternMode"),
	];

	/// <summary>
	/// The parameters each of the four textile layers has (g_fDetailScale1 to g_fDetailScale4...), with the shader's
	/// default (the same for every layer). g_vDamageLevels1 to 4 are the float2 one, default ( 0, 0 ).
	/// </summary>
	private static readonly (string Name, float Default)[] LayerParameters =
	[
		("g_fDetailBlackPoint", 0.047f),
		("g_fDetailScale", 4),
		("g_fDetailRoughnessContrast", 0),
		("g_fDetailRoughnessBrightness", 1),
		("g_fDetailAnisotropyScalar", 0),
		("g_fDetailMetalness", 0),
		("g_fDetailCloth", 0),
		("g_fDetailNormalContrast", 1),
		("g_fDamageRoughnessContrast", 0),
		("g_fDamageRoughnessBrightness", 1),
		("g_fDamageSaturation", 0),
		("g_fDamageBrightness", 0),
		("g_fDamageMetalness", 0),
		("g_fDamageCloth", 0),
		("g_fDamageBleaching", 0),
		("g_fCurvaturePower", 1),
		("g_fCurvatureWearBoost", 0),
		("g_fDamageNormalEdgeWidth", 1),
		("g_fDamageEdgeRoughness", 0.8f),
		("g_fDamageEdgeMetalness", 0),
		("g_fDetailGrunge", 0),
		("g_fGrungeMax", 1),
		("g_fGrungeRoughnessBrightness", 0.8f),
		("g_fWearBleaching", 0.25f),
		("g_fGrimeSaturation", 0),
		("g_fGrimeBrightness", 0),
	];

	/// <summary>
	/// The palette's gamma colours, with the shader's defaults.
	/// </summary>
	private static readonly (string Name, Vector3 Default)[] Palette =
	[
		("g_vColorTint1", new( 0.15f, 0.11f, 0.08f )),
		("g_vColorTint2", new( 0.23f, 0.16f, 0.26f )),
		("g_vColorTint3", new( 0.16f, 0.16f, 0.28f )),
		("g_vColorTint4", new( 0.2f, 0.3f, 0.3f )),
		("g_vColorTint5", new( 0.09f, 0.16f, 0.1f )),
		("g_vColorTint6", new( 0.29f, 0.26f, 0.11f )),
		("g_vColorTint7", new( 0.32f, 0.22f, 0.12f )),
		("g_vColorTint8", new( 0.21f, 0.12f, 0.09f )),
	];

	/// <summary>
	/// The textures, with the default the game's shader gives each (CreateInputTexture2D) as bytes in the texture's
	/// own encoding, or the compiled default file. The normal maps' defaults are taken as their red and green (a flat
	/// normal hemi-octahedral; that the compiled defaults hold that is assumed). The game's compiled glove paints name
	/// every texture.
	/// </summary>
	private static readonly (string Name, Vector4 Default, string File)[] Textures =
	[
		("g_tLayerMask", new( 0, 0, 0, 1 ), null),
		("g_tSurface", new( 0.5f, 1, 0, 0.5f ), null),
		("g_tNormal", new( 0.5f, 0.5f, 1, 0 ), null),
		("g_tNoise", new( 1, 1, 1, 1 ), "materials/default/gloves/textiles/noise_psd_bfc4dcd1.vtex"),
		("g_tPattern", new( 0, 0, 0, 1 ), null),
		("g_tDetail1", new( 0.5f, 0.5f, 1, 1 ), null),
		("g_tDetail2", new( 0.5f, 0.5f, 1, 1 ), null),
		("g_tDetail3", new( 0.5f, 0.5f, 1, 1 ), null),
		("g_tDetail4", new( 0.5f, 0.5f, 1, 1 ), null),
		("g_tDetailNormal1", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tDetailNormal2", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tDetailNormal3", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tDetailNormal4", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tDamageNormal1", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tDamageNormal2", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tDamageNormal3", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tDamageNormal4", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tGrunge1", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tGrunge2", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tGrunge3", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
		("g_tGrunge4", new( 0.5f, 0.5f, 0.5f, 0.5f ), null),
	];

	public static void Set( RenderAttributes attributes, IReadOnlyDictionary<string, CS2CompositeValue> values, Func<string, Texture> load, Func<Vector4, Texture> solid )
	{
		float Float( string name, float fallback ) => values.TryGetValue( name, out var value ) && !value.IsText ? value.X : fallback;
		Vector4 Vector( string name, Vector4 fallback ) => values.TryGetValue( name, out var value ) && !value.IsText ? value.Number : fallback;
		bool Bool( string name, bool fallback ) => Float( name, fallback ? 1 : 0 ) != 0;

		foreach ( var (parameter, attribute) in Flags )
			attributes.Set( attribute, Float( parameter, 0 ) );

		// g_bPattern: F_PATTERN ? 1 : 0.
		attributes.Set( "CS2Pattern", Bool( "F_PATTERN", false ) ? 1f : 0f );

		// The wear (the vcompmat copies the item's float in): pow( this, g_fWearExponent ).
		attributes.Set( "g_fWearProgress", MathF.Pow( Float( "g_fWearProgress", 0 ), Float( "g_fWearExponent", 1 ) ) );

		// The left hand (g_bFlipFixup) mirrors the pattern's u and turns its rotation the other way.
		var flip = Bool( "g_bFlipFixup", false );
		attributes.Set( "g_fFlipFixup", flip ? -1f : 1f );

		void Xform( string name, float degrees, float scale, Vector4 offset )
		{
			var (row0, row1) = TexCoordXform( degrees, scale, new Vector2( offset.x, offset.y ) );
			attributes.Set( name + "0", row0 );
			attributes.Set( name + "1", row1 );
		}

		var grungeRotation = Float( "g_fGrungeTexCoordRotation", 0 );
		Xform( "g_vGrungeTexCoordXform", grungeRotation, Float( "g_fGrungeTexCoordScale", 2.5f ), Vector( "g_fGrungeTexCoordOffset", default ) );
		Xform( "g_vGrungeTexRotationXform", -grungeRotation, 1, default );

		var patternRotation = Float( "g_fPatternTexCoordRotation", 0 );
		Xform( "g_vPatternTexCoordXform", flip ? -patternRotation : patternRotation, Float( "g_fPatternTexCoordScale", 2.5f ), Vector( "g_fPatternTexCoordOffset", default ) );
		Xform( "g_vPatternTexRotationXform", flip ? patternRotation : -patternRotation, 1, default );

		attributes.Set( "g_fColorMaskBlur", Float( "g_fColorMaskBlur", 2 ) );
		attributes.Set( "g_flSheenScale", Float( "g_flSheenScale", 0.667f ) );

		// Gamma: the shader linearises the palette after mixing it.
		foreach ( var (name, fallback) in Palette )
		{
			var color = Vector( name, new Vector4( fallback.x, fallback.y, fallback.z, 0 ) );
			attributes.Set( name, new Vector3( color.x, color.y, color.z ) );
		}

		// The pattern. g_nPatternReplaceIndex: this - 1 (the palette region from 0).
		attributes.Set( "g_nPatternReplaceIndex", Float( "g_nPatternReplaceIndex", 1 ) - 1 );
		attributes.Set( "g_vPatternPaletteIndices", Vector( "g_vPatternPaletteIndices", new Vector4( 5, 6, 7, 8 ) ) );
		attributes.Set( "g_fPatternRoughnessBrightness", Float( "g_fPatternRoughnessBrightness", 1 ) );
		attributes.Set( "g_fPatternMetalness", Float( "g_fPatternMetalness", 0 ) );
		attributes.Set( "g_fPaintDurability", Float( "g_fPaintDurability", 0.5f ) );
		attributes.Set( "g_fPatternDetailInfluence", Float( "g_fPatternDetailInfluence", 0 ) );

		// g_fPaintShadowPower: max( .01, g_fPaintThickness * .15 ).
		var thickness = Float( "g_fPaintThickness", 1 );
		attributes.Set( "g_fPaintThickness", thickness );
		attributes.Set( "g_fPaintShadowPower", MathF.Max( 0.01f, thickness * 0.15f ) );

		for ( var layer = 1; layer <= 4; layer++ )
		{
			foreach ( var (name, fallback) in LayerParameters )
				attributes.Set( $"{name}{layer}", Float( $"{name}{layer}", fallback ) );

			// g_fDetailBlackPointCompensation: 1 - g_fDetailBlackPoint.
			attributes.Set( $"g_fDetailBlackPointCompensation{layer}", 1 - Float( $"g_fDetailBlackPoint{layer}", 0.047f ) );

			var levels = Vector( $"g_vDamageLevels{layer}", default );
			attributes.Set( $"g_vDamageLevels{layer}", new Vector2( levels.x, levels.y ) );
		}

		foreach ( var (name, fallback, file) in Textures )
			attributes.Set( name, Texture( values, name, load ) ?? (file is not null ? load( file ) : null) ?? solid( fallback ) );
	}

	private static Texture Texture( IReadOnlyDictionary<string, CS2CompositeValue> values, string name, Func<string, Texture> load )
	{
		return values.TryGetValue( name, out var value ) && value.IsText && !value.Text.StartsWith( CS2CompositeProgram.GeneratedPrefix, StringComparison.Ordinal ) ? load( value.Text ) : null;
	}

	/// <summary>
	/// The game's texture coordinate transform expression (csgo_customglove's g_v*TexCoordXform0 and 1, and the
	/// rotation alone, g_v*TexRotationXform0 and 1, at scale 1 and no offset): rotation (with the expression's pi of
	/// 3.14159) and scale about the middle, then the offset, as two rows. Unlike csgo_customweapon's, nothing's rounded.
	/// </summary>
	private static (Vector4 Row0, Vector4 Row1) TexCoordXform( float degrees, float scale, Vector2 offset )
	{
		var angle = degrees * 3.14159f / 180f;
		var cos = MathF.Cos( angle );
		var sin = MathF.Sin( angle );
		var half = 0.5f / (scale != 0 ? scale : 1);
		var cosBack = MathF.Cos( -angle );
		var sinBack = MathF.Sin( -angle );
		var v7 = half * cosBack - half * sinBack;
		var v8 = v7 * sinBack + half * cosBack;

		return (
			new Vector4( cos * scale, -sin * scale, 0, scale * cos * v7 + scale * -sin * v8 + (offset.x - 0.5f) ),
			new Vector4( sin * scale, cos * scale, 0, scale * sin * v7 + scale * cos * v8 + (offset.y - 0.5f) ));
	}
}
