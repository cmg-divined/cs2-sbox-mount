//=========================================================================================================================
// CS2's csgo_customweapon.vfx: the weapon finish compositor, as a compute pass.
//
// CS2's composite material system (client.dll's CCompositeMaterialManager, running a paint kit's .vcompmat) draws the
// paint kit's paint material with this shader over the weapon's texture space, once per texture it generates:
//  - g_tColor: the finish's colour, the weapon's own colour (the composite inputs' g_tColor) where it's worn through.
//  - g_tMetalness with F_ROUGHNESS_MODE: roughness (red), metalness (green) and how much paint is left (blue, read as
//    csgo_weapon's pearlescence mask), the weapon's own roughness and metalness where it's worn through. The game draws
//    it into an sRGB target and encodes the values first (SrgbGammaToLinear) so they're stored as they are; this writes
//    them as they are into a linear one.
// The inputs, as the vcompmat copies them in: the paint material's own parameters, the weapon's composite inputs
// (g_tAmbientOcclusion: cavity in red, ambient occlusion in green, unpaintable areas in alpha; g_tMasks; g_tPosition and
// g_tSurface, the weapon's object space position and normal per texel; g_tColor, g_tMetalness), the item's wear and the
// pattern placement its seed rolled.
//
// From the game's compiled shader (decompiled with Source 2 Viewer, every static combo the weapon paint kits use, each
// style with and without F_SEPARATE_CHANNEL_INPUTS, F_ROUGHNESS_TEXTURE, F_PEARLESCENCE_MASK, F_METALNESS_TEXTURE,
// g_flUseAllMasks), its combos and runtime switches as uniforms (this runs once per texture, not per frame):
//  - Styles (g_flPaintStyle): 0 solid, 1 hydrographic, 2 spray paint, 3 anodized, 4 anodized multicoloured, 5 anodized
//    airbrushed, 6 custom texture, 7 patina (antiqued), 8 gunsmith. Spray paint and airbrushed project the pattern
//    from the weapon's surface (three planes blended by the surface normal); the others map it in texture space.
//  - F_SEPARATE_CHANNEL_INPUTS (CS2's composite inputs) against the pre-packed CS:GO ones: the cavity curve, the wear
//    thresholds and the patina terms differ.
//  - F_OVERRIDE_NORMAL changes nothing here (the vcompmat copies the paint's normal map instead).
//
// The VS's texture coordinates (pattern, wear, grunge) come from the texcoord transforms the C# side evaluates from the
// shader's expressions. Mips: each level is composited at its own resolution, its inputs filtered for that size
// (SampleGrad with the level's texel footprint); the game composites at full size and its mips aren't traced
// (unverified how it makes them). All the mips come from one dispatch.
//
// Not ported (the newer kits' templates only): F_OVERLAY_TEXTURE (overlay, its blend modes and masks), F_CASE_HARDENING
// and F_CASE_HARDENING_TRILINEAR, F_PATINA_AGE. Glitter is csgo_weapon's (F_GLITTER), not this shader's.
//=========================================================================================================================

HEADER
{
	DevShader = true;
	Description = "CS2 csgo_customweapon compositor";
}

MODES
{
	Default();
}

COMMON
{
	#include "postprocess/shared.hlsl"
}

CS
{
	// g_sTrilinearWrap and g_sTrilinearClamp are the common ones (common_samplers.fxc), as the game's shader names its own.

	// The weapon's composite inputs and the base it's painted over (sRGB where the game's shader reads them so).
	Texture2D g_tAmbientOcclusion < Attribute( "g_tAmbientOcclusion" ); SrgbRead( true ); >;
	Texture2D g_tMasks < Attribute( "g_tMasks" ); SrgbRead( false ); >;
	Texture2D g_tPosition < Attribute( "g_tPosition" ); SrgbRead( false ); >;
	Texture2D g_tSurface < Attribute( "g_tSurface" ); SrgbRead( false ); >;
	Texture2D g_tColor < Attribute( "g_tColor" ); SrgbRead( true ); >;
	Texture2D g_tMetalness < Attribute( "g_tMetalness" ); SrgbRead( false ); >;

	// The paint's. The pattern is read as sRGB by the styles that take its colour as it is (solid, anodized, custom,
	// patina, gunsmith) and linear by those that take it as colour masks (hydrographic, spray paint, anodized
	// multicoloured and airbrushed): two bindings of the one texture.
	Texture2D g_tPattern < Attribute( "g_tPattern" ); SrgbRead( true ); >;
	Texture2D g_tPatternLinear < Attribute( "g_tPattern" ); SrgbRead( false ); >;
	Texture2D g_tGrunge < Attribute( "g_tGrunge" ); SrgbRead( true ); >;
	Texture2D g_tWear < Attribute( "g_tWear" ); SrgbRead( false ); >;
	Texture2D g_tPaintRoughness < Attribute( "g_tPaintRoughness" ); SrgbRead( false ); >;
	Texture2D g_tPaintMetalness < Attribute( "g_tPaintMetalness" ); SrgbRead( false ); >;
	Texture2D g_tPearlescenceMask < Attribute( "g_tPearlescenceMask" ); SrgbRead( false ); >;

	// The output texture's mips, one binding each (a 4096 texture has 13), all written by one dispatch.
	RWTexture2D<float4> g_tOutput0 < Attribute( "CS2CompositeOutput0" ); >;
	RWTexture2D<float4> g_tOutput1 < Attribute( "CS2CompositeOutput1" ); >;
	RWTexture2D<float4> g_tOutput2 < Attribute( "CS2CompositeOutput2" ); >;
	RWTexture2D<float4> g_tOutput3 < Attribute( "CS2CompositeOutput3" ); >;
	RWTexture2D<float4> g_tOutput4 < Attribute( "CS2CompositeOutput4" ); >;
	RWTexture2D<float4> g_tOutput5 < Attribute( "CS2CompositeOutput5" ); >;
	RWTexture2D<float4> g_tOutput6 < Attribute( "CS2CompositeOutput6" ); >;
	RWTexture2D<float4> g_tOutput7 < Attribute( "CS2CompositeOutput7" ); >;
	RWTexture2D<float4> g_tOutput8 < Attribute( "CS2CompositeOutput8" ); >;
	RWTexture2D<float4> g_tOutput9 < Attribute( "CS2CompositeOutput9" ); >;
	RWTexture2D<float4> g_tOutput10 < Attribute( "CS2CompositeOutput10" ); >;
	RWTexture2D<float4> g_tOutput11 < Attribute( "CS2CompositeOutput11" ); >;
	RWTexture2D<float4> g_tOutput12 < Attribute( "CS2CompositeOutput12" ); >;

	// The output's size (square), its mip count, and whether its colour is stored sRGB encoded (the colour texture,
	// read as sRGB).
	float g_flOutputSize < Attribute( "CS2CompositeOutputSize" ); Default( 1 ); >;
	float g_flOutputMips < Attribute( "CS2CompositeOutputMips" ); Default( 1 ); >;
	float g_flSrgbOutput < Attribute( "CS2CompositeSrgbOutput" ); Default( 0 ); >;

	// The game's static combos (F_PAINT_STYLE, F_ROUGHNESS_MODE...) and runtime switches, as 0 or 1 (the style and
	// address modes as their numbers). Not named F_*: s&box's shader compiler takes those for features.
	float g_flPaintStyle < Attribute( "CS2PaintStyle" ); Default( 0 ); >;
	float g_flRoughnessMode < Attribute( "CS2RoughnessMode" ); Default( 0 ); >;
	float g_flSeparateChannelInputs < Attribute( "CS2SeparateChannelInputs" ); Default( 0 ); >;
	float g_flRoughnessTexture < Attribute( "CS2RoughnessTexture" ); Default( 0 ); >;
	float g_flMetalnessTexture < Attribute( "CS2MetalnessTexture" ); Default( 0 ); >;
	float g_flPearlescenceMask < Attribute( "CS2PearlescenceMask" ); Default( 0 ); >;
	float g_flUseAllMasks < Attribute( "CS2UseAllMasks" ); Default( 0 ); >;
	float g_flSpraypaintHalftone < Attribute( "CS2SpraypaintHalftone" ); Default( 0 ); >;
	float g_flRoughnessPerColor < Attribute( "CS2RoughnessPerColor" ); Default( 0 ); >;
	float g_nPatternTextureHorizontalSampling < Attribute( "g_nPatternTextureHorizontalSampling" ); Default( 0 ); >;
	float g_nPatternTextureVerticalSampling < Attribute( "g_nPatternTextureVerticalSampling" ); Default( 0 ); >;

	// Texture coordinate transforms (two rows: x, y, unused, offset), evaluated on the C# side from the game's
	// expressions: the pattern (and the halftone's three screens), the wear and the grunge.
	float4 g_vPatternTexCoordXform0 < Attribute( "g_vPatternTexCoordXform0" ); Default4( 1, 0, 0, 0 ); >;
	float4 g_vPatternTexCoordXform1 < Attribute( "g_vPatternTexCoordXform1" ); Default4( 0, 1, 0, 0 ); >;
	float4 g_vPatternTexCoordXformHalftoneA0 < Attribute( "g_vPatternTexCoordXformHalftoneA0" ); Default4( 1, 0, 0, 0 ); >;
	float4 g_vPatternTexCoordXformHalftoneA1 < Attribute( "g_vPatternTexCoordXformHalftoneA1" ); Default4( 0, 1, 0, 0 ); >;
	float4 g_vPatternTexCoordXformHalftoneB0 < Attribute( "g_vPatternTexCoordXformHalftoneB0" ); Default4( 1, 0, 0, 0 ); >;
	float4 g_vPatternTexCoordXformHalftoneB1 < Attribute( "g_vPatternTexCoordXformHalftoneB1" ); Default4( 0, 1, 0, 0 ); >;
	float4 g_vPatternTexCoordXformHalftoneC0 < Attribute( "g_vPatternTexCoordXformHalftoneC0" ); Default4( 1, 0, 0, 0 ); >;
	float4 g_vPatternTexCoordXformHalftoneC1 < Attribute( "g_vPatternTexCoordXformHalftoneC1" ); Default4( 0, 1, 0, 0 ); >;
	float4 g_vWearTexCoordXform0 < Attribute( "g_vWearTexCoordXform0" ); Default4( 1, 0, 0, 0 ); >;
	float4 g_vWearTexCoordXform1 < Attribute( "g_vWearTexCoordXform1" ); Default4( 0, 1, 0, 0 ); >;
	float4 g_vGrungeTexCoordXform0 < Attribute( "g_vGrungeTexCoordXform0" ); Default4( 1, 0, 0, 0 ); >;
	float4 g_vGrungeTexCoordXform1 < Attribute( "g_vGrungeTexCoordXform1" ); Default4( 0, 1, 0, 0 ); >;

	// The paint's parameters, as the game's expressions leave them (colours linear, durability inverted).
	float g_flWearAmount < Attribute( "g_flWearAmount" ); Default( 0 ); >;
	float g_fWearSoftness < Attribute( "g_fWearSoftness" ); Default( 0 ); >;
	float3 g_vColor0 < Attribute( "g_vColor0" ); Default3( 0.214, 0.214, 0.214 ); >;
	float3 g_vColor1 < Attribute( "g_vColor1" ); Default3( 0.308, 0.308, 0.308 ); >;
	float3 g_vColor2 < Attribute( "g_vColor2" ); Default3( 0.119, 0.119, 0.119 ); >;
	float3 g_vColor3 < Attribute( "g_vColor3" ); Default3( 0.147, 0.147, 0.147 ); >;
	float g_flColorBrightness < Attribute( "g_flColorBrightness" ); Default( 1 ); >;
	float g_nColorAdjustmentMode < Attribute( "g_nColorAdjustmentMode" ); Default( 1 ); >;
	float4 g_vPaintDurability < Attribute( "g_vPaintDurability" ); Default4( 1, 1, 1, 1 ); >;
	float g_flPaintRoughness < Attribute( "g_flPaintRoughness" ); Default( 0.6 ); >;
	float4 g_vPaintRoughness < Attribute( "g_vPaintRoughness" ); Default4( 0.6, 0.6, 0.6, 0.6 ); >;
	float g_flPaintMetalness < Attribute( "g_flPaintMetalness" ); Default( 0 ); >;
	float4 g_vPaintMetalness < Attribute( "g_vPaintMetalness" ); Default4( 0, 0, 0, 0 ); >;
	float g_flPearlescentScale < Attribute( "g_flPearlescentScale" ); Default( 0 ); >;
	float g_bPearlescentOnMetallicOnly < Attribute( "g_bPearlescentOnMetallicOnly" ); Default( 0 ); >;
	float g_bRoughnessUsesPatternUVs < Attribute( "g_bRoughnessUsesPatternUVs" ); Default( 1 ); >;
	float g_bMetalnessUsesPatternUVs < Attribute( "g_bMetalnessUsesPatternUVs" ); Default( 0 ); >;
	float g_bPearlescenceMaskUsesPatternUVs < Attribute( "g_bPearlescenceMaskUsesPatternUVs" ); Default( 1 ); >;

	// Spray paint: whether the surface normals are biased (g_bBiasSpray), and the blend toward the second and third
	// projections (g_vSprayBlend); the halftone screens.
	float3 g_vSprayBiasBlend < Attribute( "g_vSprayBiasBlend" ); Default3( 0, 1, 1 ); >;
	float g_fHalftoneCavityCutoff < Attribute( "g_fHalftoneCavityCutoff" ); Default( 1 ); >;
	float3 g_vHalftonePatternLevels < Attribute( "g_vHalftonePatternLevels" ); Default3( 0, 1, 2 ); >;
	float2 g_vHalftoneThresholds < Attribute( "g_vHalftoneThresholds" ); Default2( 0, 0 ); >;
	float g_bHalftoneInCavity < Attribute( "g_bHalftoneInCavity" ); Default( 1 ); >;

	// The 17 taps the spray styles blur the position by.
	#define SPRAY_POSITION_TAPS 17

	float2 SprayPositionTap( int i )
	{
		// The shader's constant table, in order.
		switch ( i )
		{
			case 0: return float2( -0.00107234, -0.00400203 );
			case 1: return float2( 0.00195312, -0.00338291 );
			case 2: return float2( 0.00400203, -0.00107234 );
			case 3: return float2( -0.000714896, -0.00266802 );
			case 4: return float2( 0.000976565, -0.00169146 );
			case 5: return float2( 0.00266802, -0.000714896 );
			case 6: return float2( -0.00338291, -0.00195312 );
			case 7: return float2( -0.00169146, -0.000976565 );
			case 8: return float2( 0, 0 );
			case 9: return float2( 0.00169146, 0.000976565 );
			case 10: return float2( 0.00338291, 0.00195312 );
			case 11: return float2( -0.00266802, 0.000714896 );
			case 12: return float2( -0.000976565, 0.00169146 );
			case 13: return float2( 0.000714896, 0.00266802 );
			case 14: return float2( -0.00400203, 0.00107234 );
			case 15: return float2( -0.00195312, 0.00338291 );
			default: return float2( 0.00107234, 0.00400202 );
		}
	}

	//
	// Coordinates: each with its footprint, one output texel across and down, for filtering.
	//
	struct Coordinates
	{
		float2 Uv;
		float2 Dx;
		float2 Dy;
	};

	Coordinates Transformed( Coordinates c, float4 row0, float4 row1 )
	{
		Coordinates t;
		t.Uv = float2( dot( c.Uv, row0.xy ) + row0.w, dot( c.Uv, row1.xy ) + row1.w );
		t.Dx = float2( dot( c.Dx, row0.xy ), dot( c.Dx, row1.xy ) );
		t.Dy = float2( dot( c.Dy, row0.xy ), dot( c.Dy, row1.xy ) );
		return t;
	}

	float4 SampleAt( Texture2D t, SamplerState s, Coordinates c )
	{
		return t.SampleGrad( s, c.Uv, c.Dx, c.Dy );
	}

	// RsTextureAddressMode_t: 0 wrap, 1 mirror, 2 clamp, 3 border (black), 4 mirror once. The game picks a sampler
	// with the paint's modes; here clamping and mirroring move the coordinate and a clamping sampler reads it.
	float AddressCoordinate( float x, int mode )
	{
		if ( mode == 1 )
		{
			float t = frac( x * 0.5 ) * 2.0;
			return t > 1.0 ? 2.0 - t : t;
		}

		if ( mode == 2 )
			return saturate( x );

		if ( mode == 4 )
			return saturate( abs( x ) );

		return mode == 0 ? frac( x ) : x;
	}

	float4 SamplePattern( Texture2D t, Coordinates c )
	{
		int modeU = (int)round( g_nPatternTextureHorizontalSampling );
		int modeV = (int)round( g_nPatternTextureVerticalSampling );

		if ( modeU == 0 && modeV == 0 )
			return SampleAt( t, g_sTrilinearWrap, c );

		if ( ( modeU == 3 && ( c.Uv.x < 0 || c.Uv.x > 1 ) ) || ( modeV == 3 && ( c.Uv.y < 0 || c.Uv.y > 1 ) ) )
			return 0;

		float2 uv = float2( AddressCoordinate( c.Uv.x, modeU ), AddressCoordinate( c.Uv.y, modeV ) );
		return t.SampleGrad( g_sTrilinearClamp, uv, c.Dx, c.Dy );
	}

	//
	// Shared pieces of the styles.
	//
	// Linear to sRGB, as the game's shader encodes (the biased spray's normals; the colour output here).
	float3 LinearToSrgb( float3 c )
	{
		return float3(
			c.x <= 0.0031308 ? c.x * 12.92 : pow( c.x, 1.0 / 2.4 ) * 1.055 - 0.055,
			c.y <= 0.0031308 ? c.y * 12.92 : pow( c.y, 1.0 / 2.4 ) * 1.055 - 0.055,
			c.z <= 0.0031308 ? c.z * 12.92 : pow( c.z, 1.0 / 2.4 ) * 1.055 - 0.055 );
	}

	float LuminanceOf( float3 color )
	{
		return dot( color, float3( 0.2125, 0.7154, 0.0721 ) );
	}

	float Brightest( float3 color )
	{
		return max( color.x, max( color.y, color.z ) );
	}

	// The game's albedo level constants: float3( -x, -1.4427 * log( max( 0.0001, 1 - y ) ), 2 - z ) of
	// g_vPaintAlbedoLevels' ( -0.045, 0.6, 1 ) and g_vMetallicPaintAlbedoLevels' ( -0.08, 0.6, 1 ).
	float3 AlbedoLevels( float3 x )
	{
		return float3( -x.x, -1.4427 * log( max( 0.0001, 1.0 - x.y ) ), 2.0 - x.z );
	}

	float3 PaintAlbedoLevels() { return AlbedoLevels( float3( -0.045, 0.6, 1.0 ) ); }
	float3 MetallicPaintAlbedoLevels() { return AlbedoLevels( float3( -0.08, 0.6, 1.0 ) ); }

	// Worn paint's colour fitted into a physically plausible albedo range: the paint's hue at a brightness between the
	// darkest the levels allow (or the reference colour's luminance, if lower) and their brightest.
	float3 FitAlbedo( float3 paint, float3 reference, float3 levels )
	{
		float3 direction = normalize( max( 0.0003, paint ) );
		float brightness = lerp( min( levels.x, LuminanceOf( reference ) ), levels.z, saturate( pow( Brightest( paint ), levels.y ) ) );
		return direction * brightness / Brightest( direction );
	}

	// The fitted colour faded in by the wear, then the weapon's own colour where the paint's gone.
	float3 FinishColor( float3 paint, float3 reference, float3 levels, float3 baseColor, float wornThrough )
	{
		return lerp( lerp( paint, FitAlbedo( paint, reference, levels ), g_flWearAmount ), baseColor, wornThrough );
	}

	// The middle band of a pattern's alpha: edges paint is thin at and wears from first.
	float Edge( float alpha )
	{
		return smoothstep( 0.5, 0.6, alpha ) * smoothstep( 1.0, 0.9, alpha );
	}

	// Four values mixed by three masks in turn: x, then y by the first, z by the second, w by the third.
	float MixByMasks( float4 values, float3 masks )
	{
		return lerp( lerp( lerp( values.x, values.y, masks.x ), values.z, masks.y ), values.w, masks.z );
	}

	float3 MixColorsByMasks( float3 masks )
	{
		return lerp( lerp( lerp( g_vColor0, g_vColor1, masks.x ), g_vColor2, masks.y ), g_vColor3, masks.z );
	}

	//
	// What every style reads.
	//
	struct Surface
	{
		Coordinates Uv;
		Coordinates PatternUv;
		Coordinates WearUv;
		Coordinates GrungeUv;

		bool Separate;
		float4 AmbientOcclusion;
		float CavityRaw;	// red to the 1.5 (separate channels)
		float Cavity;		// that times 0.96 (separate channels), the red as it is otherwise
		float Occlusion;	// green
		float NoPaint;		// alpha
		float Wear;			// the wear texture's red
		float4 GrungeTexture;
		float4 Grunge;		// the grunge faded in by the cavity and wear
	};

	Surface ReadSurface( Coordinates uv )
	{
		Surface s;
		s.Uv = uv;
		s.PatternUv = Transformed( uv, g_vPatternTexCoordXform0, g_vPatternTexCoordXform1 );
		s.WearUv = Transformed( uv, g_vWearTexCoordXform0, g_vWearTexCoordXform1 );
		s.GrungeUv = Transformed( uv, g_vGrungeTexCoordXform0, g_vGrungeTexCoordXform1 );

		s.Separate = g_flSeparateChannelInputs > 0.5;
		s.AmbientOcclusion = SampleAt( g_tAmbientOcclusion, g_sTrilinearClamp, uv );
		s.CavityRaw = s.Separate ? pow( s.AmbientOcclusion.x, 1.5 ) : s.AmbientOcclusion.x;
		s.Cavity = s.Separate ? s.CavityRaw * 0.96 : s.AmbientOcclusion.x;
		s.Occlusion = s.AmbientOcclusion.y;
		s.NoPaint = s.AmbientOcclusion.w;
		s.Wear = SampleAt( g_tWear, g_sTrilinearWrap, s.WearUv ).x;
		s.GrungeTexture = SampleAt( g_tGrunge, g_sTrilinearWrap, s.GrungeUv );
		s.Grunge = lerp( 1.0, s.GrungeTexture, pow( 1.0 - s.Cavity, 4.0 ) * 0.25 + 0.75 * g_flWearAmount );
		return s;
	}

	// How much of the wear texture shows through the cavity: separate channels use a curve that opens up with wear
	// (the spray style's its own), packed inputs the cavity as it is.
	float WearCavity( Surface s, bool spray )
	{
		if ( !s.Separate )
			return s.Cavity;

		if ( spray )
			return lerp( smoothstep( 0.0, 0.68, pow( s.Cavity, 1.1 ) ), smoothstep( 0.0, 0.51, pow( s.Cavity, 0.8 ) ), pow( g_flWearAmount, 0.3 ) );

		return lerp( smoothstep( 0.0, 0.72, pow( s.Cavity, 1.3 ) ), smoothstep( 0.0, 0.4, s.Cavity ), pow( g_flWearAmount, 1.2 ) );
	}

	// The paint's wear before durability: unpaintable areas, the wear texture in the cavities, all grown by the wear.
	float WearValue( Surface s, float noPaint, bool spray )
	{
		return ( noPaint + s.Wear * WearCavity( s, spray ) ) * ( g_flWearAmount * 6.0 + 1.0 );
	}

	// Where the paint's worn through: thresholds of the wear value, widened by the wear softness.
	float WornThrough( Surface s, float value, float softness, bool packedThresholds )
	{
		return s.Separate && !packedThresholds
			? smoothstep( 0.56 - softness, 0.74 + softness, value )
			: smoothstep( 0.58 - softness, 0.68 + softness, value );
	}

	// How much the colour brightness gives way at the paint's edge: separate channels by the wear value, packed inputs
	// anywhere the paint is worn at all.
	float BrightnessFade( Surface s, float value, float softness, float wornThrough )
	{
		return s.Separate ? smoothstep( 0.53 - softness, 0.72 + softness, value ) : smoothstep( 0.0, 0.01, wornThrough );
	}

	// The layers hydrographic and spray paint lay down in the pattern's channels, each worn away in turn.
	float3 WearLayers( float3 pattern, float value, float softness )
	{
		float second = 0.56 - 0.06 * g_flWearAmount;
		float third = 0.54 - g_flWearAmount * 0.12;
		return pattern * float3(
			smoothstep( 0.58 + softness, second - softness, value ),
			smoothstep( second + softness, third - softness, value ),
			smoothstep( third + softness, ( 0.52 - g_flWearAmount * 0.18 ) - softness, value ) );
	}

	// A texture the paint maps like the pattern (its own coordinates, or the weapon's).
	float PaintTexture( Texture2D t, Surface s, bool patternUvs )
	{
		return SampleAt( t, g_sTrilinearWrap, patternUvs ? s.PatternUv : s.Uv ).x;
	}

	// The roughness pass's output: roughness, metalness, the paint left (csgo_weapon's pearlescence mask), and the
	// pearlescence scale (alpha).
	float4 RoughnessOutput( float roughness, float metalness, float paint )
	{
		return float4( roughness, metalness, paint, min( 1.0, g_flPearlescentScale ) );
	}

	// F_PEARLESCENCE_MASK: the paint left, by the mask. Styles without a pattern take it on the weapon's coordinates as
	// it is; the others mapped like the pattern as it is, or on the weapon's coordinates squared (the game's shader).
	float PearlescenceMasked( float paint, Surface s, bool hasPattern, bool patternUvs )
	{
		if ( g_flPearlescenceMask < 0.5 )
			return paint;

		if ( hasPattern && patternUvs )
			return paint * SampleAt( g_tPearlescenceMask, g_sTrilinearWrap, s.PatternUv ).x;

		float masked = paint * SampleAt( g_tPearlescenceMask, g_sTrilinearWrap, s.Uv ).x;
		return hasPattern ? masked * masked : masked;
	}

	//
	// The spray styles' pattern: the weapon's position (blurred), projected on its three planes, blended by its normal.
	//
	struct SprayPosition
	{
		float3 Position;	// blurred, times two
		float3 Dx;			// its change across the output texel, for filtering the projections
		float3 Dy;
		float3 Normal;
	};

	struct SprayProjection
	{
		Coordinates YZ;
		Coordinates XZ;
		Coordinates YX;
		float3 Normal;
	};

	float3 SurfaceNormal( Surface s )
	{
		float3 surface = SampleAt( g_tSurface, g_sTrilinearClamp, s.Uv ).xyz;

		// Biased spray (separate channels only): the normals' encoding taken as sRGB.
		if ( s.Separate && g_vSprayBiasBlend.x > 0.5 )
			surface = LinearToSrgb( surface );

		return normalize( surface * 2.0 - 1.0 );
	}

	SprayPosition ReadSprayPosition( Surface s )
	{
		SprayPosition p;
		float3 position = 0;
		for ( int i = 0; i < SPRAY_POSITION_TAPS; i++ )
		{
			Coordinates tap = s.Uv;
			tap.Uv += SprayPositionTap( i ) * 0.2;
			position += SampleAt( g_tPosition, g_sTrilinearClamp, tap ).xyz * ( 1.0 / 17.0 );
		}

		p.Position = position * 2.0;

		float3 centre = g_tPosition.SampleGrad( g_sTrilinearClamp, s.Uv.Uv, s.Uv.Dx, s.Uv.Dy ).xyz;
		p.Dx = ( g_tPosition.SampleGrad( g_sTrilinearClamp, s.Uv.Uv + s.Uv.Dx, s.Uv.Dx, s.Uv.Dy ).xyz - centre ) * 2.0;
		p.Dy = ( g_tPosition.SampleGrad( g_sTrilinearClamp, s.Uv.Uv + s.Uv.Dy, s.Uv.Dx, s.Uv.Dy ).xyz - centre ) * 2.0;

		// Halftone flips the position's z.
		if ( g_flSpraypaintHalftone > 0.5 )
		{
			p.Position.z = -p.Position.z;
			p.Dx.z = -p.Dx.z;
			p.Dy.z = -p.Dy.z;
		}

		p.Normal = SurfaceNormal( s );
		return p;
	}

	Coordinates Plane( float2 position, float2 dx, float2 dy, float4 row0, float4 row1 )
	{
		Coordinates plane;
		plane.Uv = position;
		plane.Dx = dx;
		plane.Dy = dy;
		return Transformed( plane, row0, row1 );
	}

	SprayProjection ProjectSpray( SprayPosition p, float4 row0, float4 row1 )
	{
		SprayProjection projection;
		projection.YZ = Plane( p.Position.yz, p.Dx.yz, p.Dy.yz, row0, row1 );
		projection.XZ = Plane( p.Position.xz, p.Dx.xz, p.Dy.xz, row0, row1 );
		projection.YX = Plane( p.Position.yx, p.Dx.yx, p.Dy.yx, row0, row1 );
		projection.Normal = p.Normal;
		return projection;
	}

	float4 BlendProjections( float4 yz, float4 xz, float4 yx, float3 normal )
	{
		return lerp( lerp( yz, xz, g_vSprayBiasBlend.y * pow( abs( normal.y ), 7.0 ) ), yx, g_vSprayBiasBlend.z * pow( abs( normal.z ), 7.0 ) );
	}

	float4 SampleSprayPattern( SprayProjection p )
	{
		return BlendProjections( SamplePattern( g_tPatternLinear, p.YZ ), SamplePattern( g_tPatternLinear, p.XZ ), SamplePattern( g_tPatternLinear, p.YX ), p.Normal );
	}

	float SampleSprayTexture( Texture2D t, SprayProjection p )
	{
		return BlendProjections( SampleAt( t, g_sTrilinearWrap, p.YZ ), SampleAt( t, g_sTrilinearWrap, p.XZ ), SampleAt( t, g_sTrilinearWrap, p.YX ), p.Normal ).x;
	}

	// The spray pattern, with F_SPRAYPAINT_HALFTONE: its colours screened by three rotated copies of its alpha, thinned
	// in (or out of) the cavities.
	float4 SprayPattern( Surface s, out SprayProjection projection )
	{
		SprayPosition position = ReadSprayPosition( s );
		projection = ProjectSpray( position, g_vPatternTexCoordXform0, g_vPatternTexCoordXform1 );
		float4 pattern = SampleSprayPattern( projection );

		if ( g_flSpraypaintHalftone < 0.5 )
			return pattern;

		SprayProjection a = ProjectSpray( position, g_vPatternTexCoordXformHalftoneA0, g_vPatternTexCoordXformHalftoneA1 );
		SprayProjection b = ProjectSpray( position, g_vPatternTexCoordXformHalftoneB0, g_vPatternTexCoordXformHalftoneB1 );
		SprayProjection c = ProjectSpray( position, g_vPatternTexCoordXformHalftoneC0, g_vPatternTexCoordXformHalftoneC1 );

		float3 levels = g_vHalftonePatternLevels;
		bool inverted = g_vHalftoneThresholds.x > g_vHalftoneThresholds.y;
		float screen;
		if ( g_bHalftoneInCavity > 0.5 )
		{
			screen = smoothstep( levels.x, levels.z, pow( min( s.Cavity, g_fHalftoneCavityCutoff ) * s.Occlusion * 2.0, levels.y ) );
			pattern.rgb *= inverted ? screen : 1.0 - screen;
		}
		else
		{
			screen = smoothstep( levels.x, levels.z, pow( s.Cavity * s.Occlusion * 2.0, levels.y ) );
			pattern.rgb *= inverted ? 1.0 - screen : screen;
		}

		float3 screens = float3( SampleSprayPattern( a ).w, SampleSprayPattern( b ).w, SampleSprayPattern( c ).w );
		pattern.rgb = smoothstep( g_vHalftoneThresholds.x, g_vHalftoneThresholds.y, pattern.rgb * screens );
		return pattern;
	}

	//
	// The styles. Each returns the colour pass's colour (alpha 1) or the roughness pass's output.
	//

	// 0 solid, 1 hydrographic, 2 spray paint: colours by masks (the weapon's, the pattern's layers), roughness and
	// metalness per colour.
	float4 Paint( Surface s, int style, bool roughnessMode )
	{
		float4 masks = style == 2 && g_flUseAllMasks < 0.5 ? 0 : SampleAt( g_tMasks, g_sTrilinearClamp, s.Uv );
		float4 pattern = 1;
		SprayProjection spray = (SprayProjection)0;
		if ( style == 1 )
			pattern = SamplePattern( g_tPatternLinear, s.PatternUv );
		else if ( style == 2 )
			pattern = SprayPattern( s, spray );

		bool layered = style != 0;
		bool weaponMasks = style == 1 || ( style == 2 && g_flUseAllMasks > 0.5 );
		float edge = style == 1 || style == 2 ? Edge( pattern.w ) : 0;

		// Solid paints mix their four colours by the weapon's masks; the layered ones by the pattern's channels, then
		// (hydrographic, spray with all masks) the weapon's green and blue masks over them.
		float durability = layered ? MixByMasks( g_vPaintDurability, pattern.rgb ) : MixByMasks( g_vPaintDurability, masks.rgb );
		if ( weaponMasks )
			durability = lerp( lerp( durability, g_vPaintDurability.z, masks.y ), g_vPaintDurability.w, masks.z );

		float value = WearValue( s, s.NoPaint, style == 2 );
		if ( style == 1 )
			value = ( value + edge ) * max( saturate( masks.y + masks.z ), smoothstep( 0.0, 0.5, pattern.w ) );
		value *= durability;

		float softness = g_fWearSoftness * durability;
		float3 layers = layered ? WearLayers( pattern.rgb, value, softness ) : masks.rgb;
		float wornThrough = WornThrough( s, value, softness, false );

		float paintMetalness = MixByMasks( g_vPaintMetalness, layers );
		if ( weaponMasks )
			paintMetalness = lerp( lerp( paintMetalness, g_vPaintMetalness.z, masks.y ), g_vPaintMetalness.w, masks.z );
		if ( g_flMetalnessTexture > 0.5 )
			paintMetalness = PaintTexture( g_tPaintMetalness, s, g_bMetalnessUsesPatternUVs > 0.5 );

		if ( roughnessMode )
		{
			float roughness;
			if ( g_flRoughnessPerColor > 0.5 )
			{
				roughness = MixByMasks( g_vPaintRoughness, layers );
				if ( weaponMasks )
					roughness = lerp( lerp( roughness, g_vPaintRoughness.z, masks.y ), g_vPaintRoughness.w, masks.z );
			}
			else if ( g_flRoughnessTexture > 0.5 )
			{
				bool patternUvs = style != 0 && g_bRoughnessUsesPatternUVs > 0.5;
				roughness = style == 2 && patternUvs ? SampleSprayTexture( g_tPaintRoughness, spray ) : PaintTexture( g_tPaintRoughness, s, patternUvs );
			}
			else
			{
				roughness = g_flPaintRoughness;
			}

			float4 metal = SampleAt( g_tMetalness, g_sTrilinearClamp, s.Uv );
			float4 surface = lerp( float4( min( 1.0, roughness + ( 1.0 - s.Grunge.w ) * g_flWearAmount * g_flWearAmount * 0.5 ), paintMetalness, 0.0, 1.0 ), metal, wornThrough );

			float paint = 1.0 - wornThrough;
			if ( g_flPearlescenceMask > 0.5 )
			{
				bool patternUvs = style != 0 && g_bPearlescenceMaskUsesPatternUVs > 0.5;
				if ( style == 2 && patternUvs )
					paint *= SampleSprayTexture( g_tPearlescenceMask, spray );
				else
					paint = PearlescenceMasked( paint, s, style != 0, patternUvs );

				// Not where the weapon's green and blue masks lay their colours.
				if ( weaponMasks )
					paint *= 1.0 - max( masks.y, masks.z );
			}

			return RoughnessOutput( surface.x, surface.y, paint );
		}

		float3 baseColor = SampleAt( g_tColor, g_sTrilinearClamp, s.Uv ).rgb;
		float3 color;
		if ( layered )
		{
			color = MixColorsByMasks( layers );
			if ( weaponMasks )
				color = lerp( lerp( color, g_vColor2, masks.y ), g_vColor3, masks.z );
			color = saturate( color * lerp( g_flColorBrightness, 1.0, BrightnessFade( s, value, softness, wornThrough ) * ( 1.0 - edge ) ) );
		}
		else
		{
			color = MixColorsByMasks( masks.rgb );
		}

		float3 paint = color * s.Grunge.rgb;
		float3 levels = lerp( PaintAlbedoLevels(), MetallicPaintAlbedoLevels(), paintMetalness );
		return float4( FinishColor( paint, color, levels, baseColor, wornThrough ), 1.0 );
	}

	// 3 anodized, 4 anodized multicoloured, 5 anodized airbrushed: metal dyed where the weapon's red mask says, by one
	// colour or the pattern's, the bare metal where it's worn.
	float4 Anodized( Surface s, int style, bool roughnessMode )
	{
		float4 masks = SampleAt( g_tMasks, g_sTrilinearClamp, s.Uv );
		float metal = masks.x;
		float4 pattern = 0;
		SprayProjection spray = (SprayProjection)0;
		if ( style == 4 )
			pattern = SamplePattern( g_tPatternLinear, s.PatternUv );
		else if ( style == 5 )
			pattern = SprayPattern( s, spray );

		float edge = style == 3 ? 0 : Edge( pattern.w );
		float durability = 1.0;
		if ( style != 3 )
			durability = lerp( lerp( MixByMasks( g_vPaintDurability, pattern.rgb ), g_vPaintDurability.z, masks.y ), g_vPaintDurability.w, masks.z );

		float value = WearValue( s, s.NoPaint, false );
		if ( style == 4 )
			value += edge;
		value *= durability;

		float softness = g_fWearSoftness * durability;
		float wornThrough = WornThrough( s, value, softness, false );
		float worn = max( 1.0 - metal, wornThrough );
		float dyeFade = BrightnessFade( s, value, softness, wornThrough ) * ( 1.0 - edge ) * metal;

		if ( roughnessMode )
		{
			float4 weapon = SampleAt( g_tMetalness, g_sTrilinearClamp, s.Uv );
			float paint = 1.0 - worn;

			float paintRoughness = g_flPaintRoughness;
			if ( g_flRoughnessTexture > 0.5 )
			{
				bool patternUvs = style != 3 && g_bRoughnessUsesPatternUVs > 0.5;
				paintRoughness = style == 5 && patternUvs ? SampleSprayTexture( g_tPaintRoughness, spray ) : PaintTexture( g_tPaintRoughness, s, patternUvs );
			}

			// Multicoloured: the pattern's alpha below a half roughens toward 1 (a texture replaces the paint's
			// roughness there only).
			float roughness = paintRoughness;
			if ( style == 4 )
			{
				float t = 1.0 - min( 1.0, pattern.w * 2.0 );
				roughness = lerp( lerp( t * t * 0.85 + 0.15, paintRoughness, pattern.w >= 0.5 ? 1.0 : 0.0 ), g_flPaintRoughness, max( max( masks.y, masks.z ), worn ) );
			}
			else if ( style == 5 )
			{
				roughness = lerp( paintRoughness, g_flPaintRoughness, max( max( masks.y, masks.z ), worn ) );
			}

			roughness = lerp( weapon.x, min( 1.0, lerp( roughness, 0.35, dyeFade ) + ( 1.0 - LuminanceOf( s.Grunge.rgb ) ) * 0.2 ), max( 0.0, paint ) );
			float metalness = lerp( metal, weapon.y, worn );

			if ( g_flPearlescenceMask > 0.5 )
			{
				bool patternUvs = style != 3 && g_bPearlescenceMaskUsesPatternUVs > 0.5;
				if ( style == 5 && patternUvs )
					paint *= SampleSprayTexture( g_tPearlescenceMask, spray );
				else
					paint = PearlescenceMasked( paint, s, style != 3, patternUvs );

				if ( style != 3 )
					paint *= 1.0 - max( masks.y, masks.z );
			}

			return RoughnessOutput( roughness, metalness, paint );
		}

		float3 baseColor = SampleAt( g_tColor, g_sTrilinearClamp, s.Uv ).rgb;
		float3 dye = g_vColor0;
		if ( style == 4 )
			dye = lerp( lerp( lerp( lerp( lerp( g_vColor0, g_vColor1, pattern.x * metal ), g_vColor2, pattern.y * metal ), g_vColor3, pattern.z * metal ), g_vColor2, masks.y ), g_vColor3, masks.z );
		else if ( style == 5 )
			dye = lerp( lerp( MixColorsByMasks( pattern.rgb ), g_vColor2, masks.y ), g_vColor3, masks.z );

		// Dyed metal toward a grey at the dye's edge, brightened by the colour brightness twice over.
		float brightness = lerp( g_flColorBrightness, 1.0, dyeFade );
		float3 color = saturate( saturate( lerp( lerp( baseColor, dye, metal ), float3( 0.38, 0.37, 0.35 ), dyeFade ) * brightness ) * brightness );
		float3 paint = color * lerp( s.Grunge.rgb, 1.0, dyeFade );
		return float4( FinishColor( paint, color, MetallicPaintAlbedoLevels(), baseColor, worn ), 1.0 );
	}

	// 6 custom texture: the pattern is the paint's colour.
	float4 Custom( Surface s, bool roughnessMode )
	{
		float4 pattern = SamplePattern( g_tPattern, s.PatternUv );
		float durability = max( 0.0, smoothstep( 0.0, 0.5, pattern.w ) );
		float softness = g_fWearSoftness * durability;
		float value = ( WearValue( s, s.NoPaint, false ) + Edge( pattern.w ) ) * durability;
		float wornThrough = WornThrough( s, value, softness, false );

		float paintMetalness = g_flMetalnessTexture > 0.5 ? PaintTexture( g_tPaintMetalness, s, g_bMetalnessUsesPatternUVs > 0.5 ) : g_flPaintMetalness;

		if ( roughnessMode )
		{
			float roughness = g_flRoughnessTexture > 0.5 ? PaintTexture( g_tPaintRoughness, s, g_bRoughnessUsesPatternUVs > 0.5 ) : g_flPaintRoughness;
			float4 metal = SampleAt( g_tMetalness, g_sTrilinearClamp, s.Uv );
			float4 surface = lerp( float4( min( 1.0, roughness + ( 1.0 - s.Grunge.w ) * g_flWearAmount * g_flWearAmount * 0.5 ), paintMetalness, 0.0, 1.0 ), metal, wornThrough );
			float paint = PearlescenceMasked( 1.0 - wornThrough, s, true, g_bPearlescenceMaskUsesPatternUVs > 0.5 );
			return RoughnessOutput( surface.x, surface.y, paint );
		}

		float3 baseColor = SampleAt( g_tColor, g_sTrilinearClamp, s.Uv ).rgb;
		float3 color = saturate( pattern.rgb * g_flColorBrightness );
		float3 paint = color * s.Grunge.rgb;
		float3 levels = lerp( PaintAlbedoLevels(), MetallicPaintAlbedoLevels(), paintMetalness );
		return float4( FinishColor( paint, color, levels, baseColor, wornThrough ), 1.0 );
	}

	// 7 patina, 8 gunsmith: aged metal. The pattern tinted by the patina colours (g_vColor1 fresh, toward g_vColor2 and
	// g_vColor3 with wear, g_vColor0 scratched bright), on the weapon's red mask; gunsmith adds paint elsewhere.
	struct Patina
	{
		float Scratches;	// bright scratches in the cavities' rims
		float Aging;		// how far the patina has aged (1 - _5609 in the game's shader)
		float Fresh;		// the rest of it
		float4 Grunge;
	};

	Patina AgePatina( Surface s )
	{
		Patina p;
		float grime = saturate( s.GrungeTexture.x * s.GrungeTexture.y * s.GrungeTexture.z );
		if ( s.Separate )
		{
			p.Scratches = smoothstep( 0.2, 0.6, ( s.Wear * s.Occlusion ) * ( s.Wear * smoothstep( 0.2, 0.3, s.Cavity ) ) * g_flWearAmount );
			p.Aging = smoothstep( 0.5 * g_flWearAmount, 0.0, saturate( s.CavityRaw * 11.52 * s.Occlusion - g_flWearAmount * grime * 2.0 ) ) * g_flWearAmount;
			p.Fresh = 1.0 - p.Aging;
			p.Grunge = max( s.Grunge, saturate( p.Scratches * 2.0 ) );
		}
		else
		{
			p.Scratches = smoothstep( 0.1, 0.2, ( s.Wear * s.Occlusion ) * ( s.Cavity * s.Cavity ) * g_flWearAmount );
			p.Fresh = smoothstep( 0.0, 0.15, ( saturate( s.Cavity * s.Occlusion - g_flWearAmount * 0.1 ) - grime * 0.23 ) + 0.08 );
			p.Aging = 1.0 - p.Fresh;
			p.Grunge = s.Grunge;
		}

		return p;
	}

	float4 AgedMetal( Surface s, int style, bool roughnessMode )
	{
		float4 masks = SampleAt( g_tMasks, g_sTrilinearClamp, s.Uv );
		float metal = masks.x;
		float4 pattern = SamplePattern( g_tPattern, s.PatternUv );
		Patina patina = AgePatina( s );
		bool gunsmith = style == 8;

		// Gunsmith: paint where the red mask isn't, worn like a custom texture's (the packed thresholds always).
		float wornThrough = 1.0 - metal;
		bool solidMetal = metal > 0.99;
		if ( gunsmith )
		{
			float durability = max( 0.0, smoothstep( 0.0, 0.5, pattern.w ) );
			float value = ( WearValue( s, min( s.NoPaint, 1.0 - metal ), false ) + Edge( pattern.w ) ) * durability;
			wornThrough = lerp( WornThrough( s, value, g_fWearSoftness * durability, true ), value, solidMetal ? 1.0 : 0.0 );
		}

		float paintMetalness = g_flMetalnessTexture > 0.5 && gunsmith ? PaintTexture( g_tPaintMetalness, s, g_bMetalnessUsesPatternUVs > 0.5 ) : g_flPaintMetalness;

		if ( roughnessMode )
		{
			float4 weapon = SampleAt( g_tMetalness, g_sTrilinearClamp, s.Uv );
			float grungeLuminance = LuminanceOf( patina.Grunge.rgb );
			float grungeWear = ( 1.0 - patina.Grunge.w ) * g_flWearAmount;

			float paintRoughness;
			if ( g_flRoughnessTexture > 0.5 )
			{
				paintRoughness = PaintTexture( g_tPaintRoughness, s, g_bRoughnessUsesPatternUVs > 0.5 );
			}
			else if ( gunsmith )
			{
				float t = 1.0 - min( 1.0, pattern.w * 2.0 );
				paintRoughness = lerp( g_flPaintRoughness, lerp( t * t * 0.85 + 0.15, g_flPaintRoughness, pattern.w >= 0.5 ? 1.0 : 0.0 ), metal );
			}
			else
			{
				paintRoughness = g_flPaintRoughness;
			}

			float roughness = saturate( paintRoughness * lerp( 1.0, 0.9, patina.Scratches ) + ( 1.0 - grungeLuminance ) * g_flWearAmount * 0.05 + patina.Aging * 0.15 * g_flWearAmount + grungeWear * 0.15 );
			float agedMetalness = lerp( lerp( 1.0, pow( patina.Fresh * patina.Grunge.w * grungeLuminance, 0.5 ), g_flWearAmount ), 1.0, patina.Scratches );
			float metalRoughness = lerp( min( 1.0, roughness + grungeWear * g_flWearAmount * 0.5 ), roughness, metal );

			float outRoughness, outMetalness, paint;
			if ( gunsmith )
			{
				paint = 1.0 - wornThrough;
				outRoughness = lerp( weapon.x, metalRoughness, solidMetal ? 1.0 : max( 0.0, paint ) );
				outMetalness = lerp( lerp( paintMetalness, weapon.y, wornThrough ), agedMetalness, metal );
			}
			else
			{
				paint = 1.0 - s.NoPaint;
				outRoughness = lerp( weapon.x, metalRoughness, step( s.NoPaint, 0.996 ) * metal );
				outMetalness = lerp( weapon.y, agedMetalness, metal );
			}

			paint = PearlescenceMasked( paint, s, true, g_bPearlescenceMaskUsesPatternUVs > 0.5 );
			if ( gunsmith && g_bPearlescentOnMetallicOnly > 0.5 )
				paint *= metal;

			return RoughnessOutput( outRoughness, outMetalness, paint );
		}

		float3 baseColor = SampleAt( g_tColor, g_sTrilinearClamp, s.Uv ).rgb;
		float3 tinted = lerp( pattern.rgb, pattern.rgb * g_flColorBrightness, max( metal, g_nColorAdjustmentMode ) );
		float3 patinaColor = lerp( lerp( g_vColor1, g_vColor3, pow( g_flWearAmount, 0.5 ) ), lerp( g_vColor1, g_vColor2, g_flWearAmount ), patina.Fresh );
		float3 aged = lerp( patinaColor * tinted, g_vColor0 * LuminanceOf( tinted ), patina.Scratches );

		// The colour pass's metalness is 1 (the game's shader only works it out in the roughness pass).
		if ( gunsmith )
		{
			float3 color = lerp( tinted, aged, metal ) * patina.Grunge.rgb;
			float3 levels = lerp( PaintAlbedoLevels(), MetallicPaintAlbedoLevels(), lerp( paintMetalness, 1.0, metal ) );
			return float4( FinishColor( color, lerp( tinted, tinted * g_vColor1, metal ), levels, baseColor, wornThrough * ( 1.0 - metal ) ), 1.0 );
		}

		float3 color = aged * patina.Grunge.rgb;
		return float4( FinishColor( color, tinted * g_vColor1, MetallicPaintAlbedoLevels(), baseColor, 1.0 - metal ), 1.0 );
	}

	float4 Composite( uint2 texel, float size )
	{
		Coordinates uv;
		uv.Uv = ( texel + 0.5 ) / size;
		uv.Dx = float2( 1.0 / size, 0 );
		uv.Dy = float2( 0, 1.0 / size );

		Surface s = ReadSurface( uv );
		int style = (int)round( g_flPaintStyle );
		bool roughnessMode = g_flRoughnessMode > 0.5;

		float4 result;
		if ( style <= 2 )
			result = Paint( s, style, roughnessMode );
		else if ( style <= 5 )
			result = Anodized( s, style, roughnessMode );
		else if ( style == 6 )
			result = Custom( s, roughnessMode );
		else
			result = AgedMetal( s, style, roughnessMode );

		if ( g_flSrgbOutput > 0.5 )
			result.rgb = LinearToSrgb( saturate( result.rgb ) );

		return result;
	}

	void Store( int mip, uint2 texel, float4 value )
	{
		switch ( mip )
		{
			case 0: g_tOutput0[texel] = value; break;
			case 1: g_tOutput1[texel] = value; break;
			case 2: g_tOutput2[texel] = value; break;
			case 3: g_tOutput3[texel] = value; break;
			case 4: g_tOutput4[texel] = value; break;
			case 5: g_tOutput5[texel] = value; break;
			case 6: g_tOutput6[texel] = value; break;
			case 7: g_tOutput7[texel] = value; break;
			case 8: g_tOutput8[texel] = value; break;
			case 9: g_tOutput9[texel] = value; break;
			case 10: g_tOutput10[texel] = value; break;
			case 11: g_tOutput11[texel] = value; break;
			case 12: g_tOutput12[texel] = value; break;
		}
	}

	// One thread per texel of the top mip; the threads on every second texel also make the next mip's, and so on.
	[numthreads( 8, 8, 1 )]
	void MainCs( uint3 id : SV_DispatchThreadID )
	{
		uint size = (uint)g_flOutputSize;
		if ( any( id.xy >= size ) )
			return;

		int mips = (int)g_flOutputMips;

		[loop]
		for ( int mip = 0; mip < mips; mip++ )
		{
			if ( ( ( id.x | id.y ) & ( ( 1u << mip ) - 1 ) ) != 0 )
				break;

			uint2 texel = id.xy >> mip;
			Store( mip, texel, Composite( texel, max( 1u, size >> mip ) ) );
		}
	}
}
