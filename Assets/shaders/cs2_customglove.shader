//=========================================================================================================================
// CS2's csgo_customglove.vfx: the glove finish compositor, as a compute pass.
//
// CS2's composite material system (client.dll's CCompositeMaterialManager, running gloves/paints/_shared_paint_generic
// .vcompmat) draws the glove paint material with this shader over the glove's texture space, once per texture of the
// glove's csgo_character material, F_OUTPUT_MODE set per pass:
//  - 0 Albedo, g_tColor: the colour (rgb), alpha 1. Written linear; the game draws into an sRGB target, so the C# side
//    asks for it sRGB encoded (CS2CompositeSrgbOutput).
//  - 1 Normal, g_tNormal: the normal, hemi-octahedral (rg, csgo_character's HemiOctIsoRoughness_RG_B), the roughness
//    (b; the first of the two with F_ANISOTROPIC_GLOSS), alpha 1.
//  - 2 Metalness, g_tMetalness: 0 (r), metalness (g), the cloth mask csgo_character's F_CLOTH_SHADING sheen takes (b),
//    alpha 1 (which csgo_character reads as its rim mask).
//  - 3 Ambient Occlusion, g_tAmbientOcclusion: the occlusion (rgb), alpha 1.
//  - 4 Roughness, g_tAnisoGloss (only generated for paints with F_ANISOTROPIC_GLOSS): the two roughnesses (rg), 0 (b),
//    alpha 1.
// Every mode's alpha is g_flSheenScale instead of 1 where the cloth mask comes out negative (only with negative cloth
// parameters). Modes 1 to 4 encode their values first (SrgbGammaToLinear) so the sRGB target stores them as they are;
// this writes them as they are into a linear one.
//
// From the game's compiled shader (decompiled with Source 2 Viewer): the combos the game's 74 glove paints use, the
// Source1 texture layout (S_BACKWARDS_COMPATIBILITY 0) with and without S_ANISOTROPIC_GLOSS, which differ only in the
// roughness's anisotropy. F_OUTPUT_MODE and F_PATTERN are runtime uniforms in the game's shader too (g_nOutputMode,
// g_bPattern); F_CLOTH_SHADING changes nothing here (the vcompmat copies it to the glove's material). The vertex shader
// passes the texture coordinates through as they are: every transform below is the pixel shader's.
//
// The glove is four textile layers (leather, fabric, metal...) blended by its layer mask, each with its detail, detail
// normal, damage normal and grunge textures and its own parameters (g_fDetailScale1 to 4...); colours come from an eight
// colour palette by the mask's palette regions, or from the pattern; wear damages the layers by the glove's surface
// properties and the grunge.
//
// Screen space derivatives: the game's shader takes dFdx and dFdy of three values (the detail brightness and the surface
// properties, for the bright edges of the weave); here they're the differences to the texel's neighbours in its 2x2 quad,
// worked out from scratch at those texels (fine derivatives; whether the game's GPU takes fine or coarse ones is the
// driver's choice, unverified). Texture filtering: each input is sampled with SampleGrad by the output texel's footprint
// through the linear part of its coordinates. The game's implicit derivatives also see the parts that vary per pixel
// (the layer blended detail scale at layer edges, the pattern's detail nudge, the wear and paint edge taps' offsets),
// where it would pick blurrier mips: not reproduced.
//
// Mips: each level is composited at its own resolution, its inputs filtered for that size; the game composites at full
// size and its mips aren't traced (unverified how it makes them). All the mips come from one dispatch.
//
// Not ported: the Source2 height blending layout (S_BACKWARDS_COMPATIBILITY 1: substrate and surface layers, damage
// height, grime, object properties, S_TINT_ID, F_PATTERN_PAINT, F_PUFFY_PAINT), which only the workshop template
// (workshop/paintkits/templates/glove_compositor.vmat) uses.
//=========================================================================================================================

HEADER
{
	DevShader = true;
	Description = "CS2 csgo_customglove compositor";
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
	// g_sTrilinearWrap and g_sPointWrap are the common ones (common_samplers.fxc). The game's shader also samples the
	// glove's normal map and the noise with g_sAniso (the footprints here are square, which it filters as trilinear),
	// and the palette regions with g_sPoint (its address mode unverified: wrap here).

	// The glove's textures: the layer mask (red, green and blue blend layers 2, 3 and 4 over layer 1; alpha the palette
	// region, in eighths), its surface properties (by how the shader uses them: red curvature, green ambient occlusion,
	// blue where wear bleaches, alpha how much it wears), its normal map (hemi-octahedral), the noise and the pattern.
	Texture2D g_tLayerMask < Attribute( "g_tLayerMask" ); SrgbRead( false ); >;
	Texture2D g_tSurface < Attribute( "g_tSurface" ); SrgbRead( false ); >;
	Texture2D g_tNormal < Attribute( "g_tNormal" ); SrgbRead( false ); >;
	Texture2D g_tNoise < Attribute( "g_tNoise" ); SrgbRead( true ); >;
	Texture2D g_tPattern < Attribute( "g_tPattern" ); SrgbRead( false ); >;

	// The layers' textures: detail (by use: red the brightness where damaged, green elsewhere, blue how deep wear goes,
	// alpha gloss), detail and damage normals (hemi-octahedral), grunge (colour, alpha how much it wears).
	Texture2D g_tDetail1 < Attribute( "g_tDetail1" ); SrgbRead( false ); >;
	Texture2D g_tDetail2 < Attribute( "g_tDetail2" ); SrgbRead( false ); >;
	Texture2D g_tDetail3 < Attribute( "g_tDetail3" ); SrgbRead( false ); >;
	Texture2D g_tDetail4 < Attribute( "g_tDetail4" ); SrgbRead( false ); >;
	Texture2D g_tDetailNormal1 < Attribute( "g_tDetailNormal1" ); SrgbRead( false ); >;
	Texture2D g_tDetailNormal2 < Attribute( "g_tDetailNormal2" ); SrgbRead( false ); >;
	Texture2D g_tDetailNormal3 < Attribute( "g_tDetailNormal3" ); SrgbRead( false ); >;
	Texture2D g_tDetailNormal4 < Attribute( "g_tDetailNormal4" ); SrgbRead( false ); >;
	Texture2D g_tDamageNormal1 < Attribute( "g_tDamageNormal1" ); SrgbRead( false ); >;
	Texture2D g_tDamageNormal2 < Attribute( "g_tDamageNormal2" ); SrgbRead( false ); >;
	Texture2D g_tDamageNormal3 < Attribute( "g_tDamageNormal3" ); SrgbRead( false ); >;
	Texture2D g_tDamageNormal4 < Attribute( "g_tDamageNormal4" ); SrgbRead( false ); >;
	Texture2D g_tGrunge1 < Attribute( "g_tGrunge1" ); SrgbRead( true ); >;
	Texture2D g_tGrunge2 < Attribute( "g_tGrunge2" ); SrgbRead( true ); >;
	Texture2D g_tGrunge3 < Attribute( "g_tGrunge3" ); SrgbRead( true ); >;
	Texture2D g_tGrunge4 < Attribute( "g_tGrunge4" ); SrgbRead( true ); >;

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

	// The game's switches and static combo, as numbers: F_OUTPUT_MODE (g_nOutputMode), F_PATTERN (g_bPattern),
	// S_ANISOTROPIC_GLOSS. Not named F_*: s&box's shader compiler takes those for features.
	float g_flOutputMode < Attribute( "CS2OutputMode" ); Default( 0 ); >;
	float g_flPattern < Attribute( "CS2Pattern" ); Default( 0 ); >;
	float g_flAnisotropicGloss < Attribute( "CS2AnisotropicGloss" ); Default( 0 ); >;

	// Texture coordinate transforms (two rows: x, y, unused, offset), evaluated on the C# side from the game's
	// expressions: the grunge and the pattern, and their rotations alone (for turning slopes back into the glove's space).
	float4 g_vGrungeTexCoordXform0 < Attribute( "g_vGrungeTexCoordXform0" ); Default4( 2.5, 0, 0, -0.5 ); >;
	float4 g_vGrungeTexCoordXform1 < Attribute( "g_vGrungeTexCoordXform1" ); Default4( 0, 2.5, 0, -0.5 ); >;
	float4 g_vGrungeTexRotationXform0 < Attribute( "g_vGrungeTexRotationXform0" ); Default4( 1, 0, 0, 0 ); >;
	float4 g_vGrungeTexRotationXform1 < Attribute( "g_vGrungeTexRotationXform1" ); Default4( 0, 1, 0, 0 ); >;
	float4 g_vPatternTexCoordXform0 < Attribute( "g_vPatternTexCoordXform0" ); Default4( 2.5, 0, 0, -0.5 ); >;
	float4 g_vPatternTexCoordXform1 < Attribute( "g_vPatternTexCoordXform1" ); Default4( 0, 2.5, 0, -0.5 ); >;
	float4 g_vPatternTexRotationXform0 < Attribute( "g_vPatternTexRotationXform0" ); Default4( 1, 0, 0, 0 ); >;
	float4 g_vPatternTexRotationXform1 < Attribute( "g_vPatternTexRotationXform1" ); Default4( 0, 1, 0, 0 ); >;

	// The wear (the item's float to the paint's g_fWearExponent), -1 for the left hand's mirrored u (g_bFlipFixup), how
	// far the palette regions blur, and the sheen scale.
	float g_fWearProgress < Attribute( "g_fWearProgress" ); Default( 0 ); >;
	float g_fFlipFixup < Attribute( "g_fFlipFixup" ); Default( 1 ); >;
	float g_fColorMaskBlur < Attribute( "g_fColorMaskBlur" ); Default( 2 ); >;
	float g_flSheenScale < Attribute( "g_flSheenScale" ); Default( 0.667 ); >;

	// The palette (gamma colours, linearised after mixing).
	float3 g_vColorTint1 < Attribute( "g_vColorTint1" ); Default3( 0.15, 0.11, 0.08 ); >;
	float3 g_vColorTint2 < Attribute( "g_vColorTint2" ); Default3( 0.23, 0.16, 0.26 ); >;
	float3 g_vColorTint3 < Attribute( "g_vColorTint3" ); Default3( 0.16, 0.16, 0.28 ); >;
	float3 g_vColorTint4 < Attribute( "g_vColorTint4" ); Default3( 0.2, 0.3, 0.3 ); >;
	float3 g_vColorTint5 < Attribute( "g_vColorTint5" ); Default3( 0.09, 0.16, 0.1 ); >;
	float3 g_vColorTint6 < Attribute( "g_vColorTint6" ); Default3( 0.29, 0.26, 0.11 ); >;
	float3 g_vColorTint7 < Attribute( "g_vColorTint7" ); Default3( 0.32, 0.22, 0.12 ); >;
	float3 g_vColorTint8 < Attribute( "g_vColorTint8" ); Default3( 0.21, 0.12, 0.09 ); >;

	// The pattern (with F_PATTERN): its mode (0 indexed, 1 unmasked indexed, 2 bitmap, 3 unmasked bitmap), the palette
	// region it replaces (from 0), the palette colours an indexed pattern's red, green and blue pick (from 1), and its
	// paint.
	float g_nPatternMode < Attribute( "g_nPatternMode" ); Default( 0 ); >;
	float g_nPatternReplaceIndex < Attribute( "g_nPatternReplaceIndex" ); Default( 0 ); >;
	float4 g_vPatternPaletteIndices < Attribute( "g_vPatternPaletteIndices" ); Default4( 5, 6, 7, 8 ); >;
	float g_fPatternRoughnessBrightness < Attribute( "g_fPatternRoughnessBrightness" ); Default( 1 ); >;
	float g_fPatternMetalness < Attribute( "g_fPatternMetalness" ); Default( 0 ); >;
	float g_fPaintThickness < Attribute( "g_fPaintThickness" ); Default( 1 ); >;
	float g_fPaintDurability < Attribute( "g_fPaintDurability" ); Default( 0.5 ); >;
	float g_fPaintShadowPower < Attribute( "g_fPaintShadowPower" ); Default( 0.15 ); >;
	float g_fPatternDetailInfluence < Attribute( "g_fPatternDetailInfluence" ); Default( 0 ); >;

	// Each layer's parameters (1 to 4).
	float g_fDetailBlackPoint1 < Attribute( "g_fDetailBlackPoint1" ); Default( 0.047 ); >;
	float g_fDetailBlackPoint2 < Attribute( "g_fDetailBlackPoint2" ); Default( 0.047 ); >;
	float g_fDetailBlackPoint3 < Attribute( "g_fDetailBlackPoint3" ); Default( 0.047 ); >;
	float g_fDetailBlackPoint4 < Attribute( "g_fDetailBlackPoint4" ); Default( 0.047 ); >;
	float g_fDetailBlackPointCompensation1 < Attribute( "g_fDetailBlackPointCompensation1" ); Default( 0.953 ); >;
	float g_fDetailBlackPointCompensation2 < Attribute( "g_fDetailBlackPointCompensation2" ); Default( 0.953 ); >;
	float g_fDetailBlackPointCompensation3 < Attribute( "g_fDetailBlackPointCompensation3" ); Default( 0.953 ); >;
	float g_fDetailBlackPointCompensation4 < Attribute( "g_fDetailBlackPointCompensation4" ); Default( 0.953 ); >;
	float g_fDetailScale1 < Attribute( "g_fDetailScale1" ); Default( 4 ); >;
	float g_fDetailScale2 < Attribute( "g_fDetailScale2" ); Default( 4 ); >;
	float g_fDetailScale3 < Attribute( "g_fDetailScale3" ); Default( 4 ); >;
	float g_fDetailScale4 < Attribute( "g_fDetailScale4" ); Default( 4 ); >;
	float g_fDetailRoughnessContrast1 < Attribute( "g_fDetailRoughnessContrast1" ); Default( 0 ); >;
	float g_fDetailRoughnessContrast2 < Attribute( "g_fDetailRoughnessContrast2" ); Default( 0 ); >;
	float g_fDetailRoughnessContrast3 < Attribute( "g_fDetailRoughnessContrast3" ); Default( 0 ); >;
	float g_fDetailRoughnessContrast4 < Attribute( "g_fDetailRoughnessContrast4" ); Default( 0 ); >;
	float g_fDetailRoughnessBrightness1 < Attribute( "g_fDetailRoughnessBrightness1" ); Default( 1 ); >;
	float g_fDetailRoughnessBrightness2 < Attribute( "g_fDetailRoughnessBrightness2" ); Default( 1 ); >;
	float g_fDetailRoughnessBrightness3 < Attribute( "g_fDetailRoughnessBrightness3" ); Default( 1 ); >;
	float g_fDetailRoughnessBrightness4 < Attribute( "g_fDetailRoughnessBrightness4" ); Default( 1 ); >;
	float g_fDetailAnisotropyScalar1 < Attribute( "g_fDetailAnisotropyScalar1" ); Default( 0 ); >;
	float g_fDetailAnisotropyScalar2 < Attribute( "g_fDetailAnisotropyScalar2" ); Default( 0 ); >;
	float g_fDetailAnisotropyScalar3 < Attribute( "g_fDetailAnisotropyScalar3" ); Default( 0 ); >;
	float g_fDetailAnisotropyScalar4 < Attribute( "g_fDetailAnisotropyScalar4" ); Default( 0 ); >;
	float g_fDetailMetalness1 < Attribute( "g_fDetailMetalness1" ); Default( 0 ); >;
	float g_fDetailMetalness2 < Attribute( "g_fDetailMetalness2" ); Default( 0 ); >;
	float g_fDetailMetalness3 < Attribute( "g_fDetailMetalness3" ); Default( 0 ); >;
	float g_fDetailMetalness4 < Attribute( "g_fDetailMetalness4" ); Default( 0 ); >;
	float g_fDetailCloth1 < Attribute( "g_fDetailCloth1" ); Default( 0 ); >;
	float g_fDetailCloth2 < Attribute( "g_fDetailCloth2" ); Default( 0 ); >;
	float g_fDetailCloth3 < Attribute( "g_fDetailCloth3" ); Default( 0 ); >;
	float g_fDetailCloth4 < Attribute( "g_fDetailCloth4" ); Default( 0 ); >;
	float g_fDetailNormalContrast1 < Attribute( "g_fDetailNormalContrast1" ); Default( 1 ); >;
	float g_fDetailNormalContrast2 < Attribute( "g_fDetailNormalContrast2" ); Default( 1 ); >;
	float g_fDetailNormalContrast3 < Attribute( "g_fDetailNormalContrast3" ); Default( 1 ); >;
	float g_fDetailNormalContrast4 < Attribute( "g_fDetailNormalContrast4" ); Default( 1 ); >;
	float2 g_vDamageLevels1 < Attribute( "g_vDamageLevels1" ); Default2( 0, 0 ); >;
	float2 g_vDamageLevels2 < Attribute( "g_vDamageLevels2" ); Default2( 0, 0 ); >;
	float2 g_vDamageLevels3 < Attribute( "g_vDamageLevels3" ); Default2( 0, 0 ); >;
	float2 g_vDamageLevels4 < Attribute( "g_vDamageLevels4" ); Default2( 0, 0 ); >;
	float g_fDamageRoughnessContrast1 < Attribute( "g_fDamageRoughnessContrast1" ); Default( 0 ); >;
	float g_fDamageRoughnessContrast2 < Attribute( "g_fDamageRoughnessContrast2" ); Default( 0 ); >;
	float g_fDamageRoughnessContrast3 < Attribute( "g_fDamageRoughnessContrast3" ); Default( 0 ); >;
	float g_fDamageRoughnessContrast4 < Attribute( "g_fDamageRoughnessContrast4" ); Default( 0 ); >;
	float g_fDamageRoughnessBrightness1 < Attribute( "g_fDamageRoughnessBrightness1" ); Default( 1 ); >;
	float g_fDamageRoughnessBrightness2 < Attribute( "g_fDamageRoughnessBrightness2" ); Default( 1 ); >;
	float g_fDamageRoughnessBrightness3 < Attribute( "g_fDamageRoughnessBrightness3" ); Default( 1 ); >;
	float g_fDamageRoughnessBrightness4 < Attribute( "g_fDamageRoughnessBrightness4" ); Default( 1 ); >;
	float g_fDamageSaturation1 < Attribute( "g_fDamageSaturation1" ); Default( 0 ); >;
	float g_fDamageSaturation2 < Attribute( "g_fDamageSaturation2" ); Default( 0 ); >;
	float g_fDamageSaturation3 < Attribute( "g_fDamageSaturation3" ); Default( 0 ); >;
	float g_fDamageSaturation4 < Attribute( "g_fDamageSaturation4" ); Default( 0 ); >;
	float g_fDamageBrightness1 < Attribute( "g_fDamageBrightness1" ); Default( 0 ); >;
	float g_fDamageBrightness2 < Attribute( "g_fDamageBrightness2" ); Default( 0 ); >;
	float g_fDamageBrightness3 < Attribute( "g_fDamageBrightness3" ); Default( 0 ); >;
	float g_fDamageBrightness4 < Attribute( "g_fDamageBrightness4" ); Default( 0 ); >;
	float g_fDamageMetalness1 < Attribute( "g_fDamageMetalness1" ); Default( 0 ); >;
	float g_fDamageMetalness2 < Attribute( "g_fDamageMetalness2" ); Default( 0 ); >;
	float g_fDamageMetalness3 < Attribute( "g_fDamageMetalness3" ); Default( 0 ); >;
	float g_fDamageMetalness4 < Attribute( "g_fDamageMetalness4" ); Default( 0 ); >;
	float g_fDamageCloth1 < Attribute( "g_fDamageCloth1" ); Default( 0 ); >;
	float g_fDamageCloth2 < Attribute( "g_fDamageCloth2" ); Default( 0 ); >;
	float g_fDamageCloth3 < Attribute( "g_fDamageCloth3" ); Default( 0 ); >;
	float g_fDamageCloth4 < Attribute( "g_fDamageCloth4" ); Default( 0 ); >;
	float g_fDamageBleaching1 < Attribute( "g_fDamageBleaching1" ); Default( 0 ); >;
	float g_fDamageBleaching2 < Attribute( "g_fDamageBleaching2" ); Default( 0 ); >;
	float g_fDamageBleaching3 < Attribute( "g_fDamageBleaching3" ); Default( 0 ); >;
	float g_fDamageBleaching4 < Attribute( "g_fDamageBleaching4" ); Default( 0 ); >;
	float g_fCurvaturePower1 < Attribute( "g_fCurvaturePower1" ); Default( 1 ); >;
	float g_fCurvaturePower2 < Attribute( "g_fCurvaturePower2" ); Default( 1 ); >;
	float g_fCurvaturePower3 < Attribute( "g_fCurvaturePower3" ); Default( 1 ); >;
	float g_fCurvaturePower4 < Attribute( "g_fCurvaturePower4" ); Default( 1 ); >;
	float g_fCurvatureWearBoost1 < Attribute( "g_fCurvatureWearBoost1" ); Default( 0 ); >;
	float g_fCurvatureWearBoost2 < Attribute( "g_fCurvatureWearBoost2" ); Default( 0 ); >;
	float g_fCurvatureWearBoost3 < Attribute( "g_fCurvatureWearBoost3" ); Default( 0 ); >;
	float g_fCurvatureWearBoost4 < Attribute( "g_fCurvatureWearBoost4" ); Default( 0 ); >;
	float g_fDamageNormalEdgeWidth1 < Attribute( "g_fDamageNormalEdgeWidth1" ); Default( 1 ); >;
	float g_fDamageNormalEdgeWidth2 < Attribute( "g_fDamageNormalEdgeWidth2" ); Default( 1 ); >;
	float g_fDamageNormalEdgeWidth3 < Attribute( "g_fDamageNormalEdgeWidth3" ); Default( 1 ); >;
	float g_fDamageNormalEdgeWidth4 < Attribute( "g_fDamageNormalEdgeWidth4" ); Default( 1 ); >;
	float g_fDamageEdgeRoughness1 < Attribute( "g_fDamageEdgeRoughness1" ); Default( 0.8 ); >;
	float g_fDamageEdgeRoughness2 < Attribute( "g_fDamageEdgeRoughness2" ); Default( 0.8 ); >;
	float g_fDamageEdgeRoughness3 < Attribute( "g_fDamageEdgeRoughness3" ); Default( 0.8 ); >;
	float g_fDamageEdgeRoughness4 < Attribute( "g_fDamageEdgeRoughness4" ); Default( 0.8 ); >;
	float g_fDamageEdgeMetalness1 < Attribute( "g_fDamageEdgeMetalness1" ); Default( 0 ); >;
	float g_fDamageEdgeMetalness2 < Attribute( "g_fDamageEdgeMetalness2" ); Default( 0 ); >;
	float g_fDamageEdgeMetalness3 < Attribute( "g_fDamageEdgeMetalness3" ); Default( 0 ); >;
	float g_fDamageEdgeMetalness4 < Attribute( "g_fDamageEdgeMetalness4" ); Default( 0 ); >;
	float g_fDetailGrunge1 < Attribute( "g_fDetailGrunge1" ); Default( 0 ); >;
	float g_fDetailGrunge2 < Attribute( "g_fDetailGrunge2" ); Default( 0 ); >;
	float g_fDetailGrunge3 < Attribute( "g_fDetailGrunge3" ); Default( 0 ); >;
	float g_fDetailGrunge4 < Attribute( "g_fDetailGrunge4" ); Default( 0 ); >;
	float g_fGrungeMax1 < Attribute( "g_fGrungeMax1" ); Default( 1 ); >;
	float g_fGrungeMax2 < Attribute( "g_fGrungeMax2" ); Default( 1 ); >;
	float g_fGrungeMax3 < Attribute( "g_fGrungeMax3" ); Default( 1 ); >;
	float g_fGrungeMax4 < Attribute( "g_fGrungeMax4" ); Default( 1 ); >;
	float g_fGrungeRoughnessBrightness1 < Attribute( "g_fGrungeRoughnessBrightness1" ); Default( 0.8 ); >;
	float g_fGrungeRoughnessBrightness2 < Attribute( "g_fGrungeRoughnessBrightness2" ); Default( 0.8 ); >;
	float g_fGrungeRoughnessBrightness3 < Attribute( "g_fGrungeRoughnessBrightness3" ); Default( 0.8 ); >;
	float g_fGrungeRoughnessBrightness4 < Attribute( "g_fGrungeRoughnessBrightness4" ); Default( 0.8 ); >;
	float g_fWearBleaching1 < Attribute( "g_fWearBleaching1" ); Default( 0.25 ); >;
	float g_fWearBleaching2 < Attribute( "g_fWearBleaching2" ); Default( 0.25 ); >;
	float g_fWearBleaching3 < Attribute( "g_fWearBleaching3" ); Default( 0.25 ); >;
	float g_fWearBleaching4 < Attribute( "g_fWearBleaching4" ); Default( 0.25 ); >;
	float g_fGrimeSaturation1 < Attribute( "g_fGrimeSaturation1" ); Default( 0 ); >;
	float g_fGrimeSaturation2 < Attribute( "g_fGrimeSaturation2" ); Default( 0 ); >;
	float g_fGrimeSaturation3 < Attribute( "g_fGrimeSaturation3" ); Default( 0 ); >;
	float g_fGrimeSaturation4 < Attribute( "g_fGrimeSaturation4" ); Default( 0 ); >;
	float g_fGrimeBrightness1 < Attribute( "g_fGrimeBrightness1" ); Default( 0 ); >;
	float g_fGrimeBrightness2 < Attribute( "g_fGrimeBrightness2" ); Default( 0 ); >;
	float g_fGrimeBrightness3 < Attribute( "g_fGrimeBrightness3" ); Default( 0 ); >;
	float g_fGrimeBrightness4 < Attribute( "g_fGrimeBrightness4" ); Default( 0 ); >;

	//
	// Layers: the layer mask's red, green and blue blend layers 2, 3 and 4 over layer 1, in turn.
	//
	float Layered( float3 m, float a1, float a2, float a3, float a4 )
	{
		return lerp( lerp( lerp( a1, a2, m.x ), a3, m.y ), a4, m.z );
	}

	float2 Layered( float3 m, float2 a1, float2 a2, float2 a3, float2 a4 )
	{
		return lerp( lerp( lerp( a1, a2, m.x ), a3, m.y ), a4, m.z );
	}

	float4 Layered( float3 m, float4 a1, float4 a2, float4 a3, float4 a4 )
	{
		return lerp( lerp( lerp( a1, a2, m.x ), a3, m.y ), a4, m.z );
	}

	// A layer parameter (g_fDetailScale for g_fDetailScale1 to 4) blended by the layer mask.
	#define LAYERED( m, name ) Layered( m, name##1, name##2, name##3, name##4 )

	//
	// Coordinates: each with its footprint, one output texel across and down, for filtering.
	//
	struct Coordinates
	{
		float2 Uv;
		float2 Dx;
		float2 Dy;
	};

	Coordinates TexelCoordinates( uint2 texel, float size )
	{
		Coordinates c;
		c.Uv = ( texel + 0.5 ) / size;
		c.Dx = float2( 1.0 / size, 0 );
		c.Dy = float2( 0, 1.0 / size );
		return c;
	}

	Coordinates Transformed( Coordinates c, float4 row0, float4 row1 )
	{
		Coordinates t;
		t.Uv = float2( dot( c.Uv, row0.xy ) + row0.w, dot( c.Uv, row1.xy ) + row1.w );
		t.Dx = float2( dot( c.Dx, row0.xy ), dot( c.Dx, row1.xy ) );
		t.Dy = float2( dot( c.Dy, row0.xy ), dot( c.Dy, row1.xy ) );
		return t;
	}

	Coordinates Scaled( Coordinates c, float scale )
	{
		Coordinates t;
		t.Uv = c.Uv * scale;
		t.Dx = c.Dx * scale;
		t.Dy = c.Dy * scale;
		return t;
	}

	Coordinates ScaledU( Coordinates c, float scale )
	{
		c.Uv.x *= scale;
		c.Dx.x *= scale;
		c.Dy.x *= scale;
		return c;
	}

	Coordinates Offset( Coordinates c, float2 offset )
	{
		c.Uv += offset;
		return c;
	}

	// A sample with a mip bias: the footprint grown by 2 to the bias.
	Coordinates Biased( Coordinates c, float bias )
	{
		c.Dx *= exp2( bias );
		c.Dy *= exp2( bias );
		return c;
	}

	float4 SampleAt( Texture2D t, SamplerState s, Coordinates c )
	{
		return t.SampleGrad( s, c.Uv, c.Dx, c.Dy );
	}

	//
	// The shader's tables and shared pieces.
	//
	// The palette regions' blur: a 3x3 box of taps (x, y in steps, weight), in the game's order.
	float3 PaletteTap( int i )
	{
		switch ( i )
		{
			case 0: return float3( 0, 0, 0.319728 );
			case 1: return float3( -1, -1, 0.0510204 );
			case 2: return float3( -1, 0, 0.119048 );
			case 3: return float3( -1, 1, 0.0510204 );
			case 4: return float3( 0, -1, 0.119048 );
			case 5: return float3( 0, 1, 0.119048 );
			case 6: return float3( 1, -1, 0.0510204 );
			case 7: return float3( 1, 0, 0.119048 );
			default: return float3( 1, 1, 0.0510204 );
		}
	}

	// The four diagonal taps the wear and paint edges are found by, in the game's order.
	float2 Corner( int i )
	{
		switch ( i )
		{
			case 0: return float2( -1, -1 );
			case 1: return float2( -1, 1 );
			case 2: return float2( 1, 1 );
			default: return float2( 1, -1 );
		}
	}

	float3 PaletteColor( int i )
	{
		switch ( clamp( i, 0, 7 ) )
		{
			case 0: return g_vColorTint1;
			case 1: return g_vColorTint2;
			case 2: return g_vColorTint3;
			case 3: return g_vColorTint4;
			case 4: return g_vColorTint5;
			case 5: return g_vColorTint6;
			case 6: return g_vColorTint7;
			default: return g_vColorTint8;
		}
	}

	// sRGB to linear (the palette and the pattern's colour), and back (the colour output here).
	float3 SrgbToLinear( float3 c )
	{
		return float3(
			c.x <= 0.04045 ? c.x * 0.0773993805 : pow( c.x * 0.947867334 + 0.0521327034, 2.4 ),
			c.y <= 0.04045 ? c.y * 0.0773993805 : pow( c.y * 0.947867334 + 0.0521327034, 2.4 ),
			c.z <= 0.04045 ? c.z * 0.0773993805 : pow( c.z * 0.947867334 + 0.0521327034, 2.4 ) );
	}

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

	// A hemi-octahedral normal (the glove's, detail and damage normals): x + y and x - y, biased by half a byte.
	float3 DecodeHemiOctahedral( float2 encoded )
	{
		float a = ( encoded.x + encoded.y ) - 1.00392163;
		float b = encoded.x - encoded.y;
		return normalize( float3( a, b, ( 1.0 - abs( a ) ) - abs( b ) ) );
	}

	float2 EncodeHemiOctahedral( float3 n )
	{
		float3 octahedral = n / ( abs( n.x ) + abs( n.y ) + abs( n.z ) );
		return float2( octahedral.x + octahedral.y, octahedral.x - octahedral.y ) * 0.5 + 0.5;
	}

	// The game's albedo level constants: float3( -x, -1.4427 * log( max( 0.0001, 1 - y ) ), 2 - z ) of
	// g_vTextileAlbedoLevels' ( -0.045, 0.6, 1 ) and g_vMetallicTextileAlbedoLevels' ( -0.08, 0.6, 1.08 ).
	float3 AlbedoLevels( float3 x )
	{
		return float3( -x.x, -1.4427 * log( max( 0.0001, 1.0 - x.y ) ), 2.0 - x.z );
	}

	float3 TextileAlbedoLevels() { return AlbedoLevels( float3( -0.045, 0.6, 1.0 ) ); }
	float3 MetallicTextileAlbedoLevels() { return AlbedoLevels( float3( -0.08, 0.6, 1.08 ) ); }

	// Saturation by the shader's own curve: above 0 the colour pushed toward the most saturated colour of a hue (from
	// the hue colour's direction away from grey) at the luminance given, by how far that hue is from grey; at or below
	// 0 the colour toward its own grey.
	float3 ShiftSaturation( float3 color, float3 hue, float luminance, float amount )
	{
		if ( amount > 0.0 )
		{
			float3 direction = normalize( hue * LuminanceOf( hue ) ) - 0.577;
			float3 saturated = saturate( normalize( direction ) * 2.0 + 1.0 );
			return lerp( color, saturated * ( luminance / LuminanceOf( saturated ) ), saturate( amount * pow( abs( dot( direction, float3( 0.577, 0.577, 0.577 ) ) ), 0.2 ) ) );
		}

		return lerp( color, LuminanceOf( color ), -amount );
	}

	//
	// What the shader works out per texel before its screen space derivatives: read at the texel and at its quad
	// neighbours.
	//
	struct Texel
	{
		Coordinates Uv;
		float3 Layers;			// the layer mask's red, green and blue
		float4 Surface;			// the surface properties
		float Curvature;		// their red to the layers' curvature power
		Coordinates DetailUv;
		float4 Detail;			// the layers' detail, red and green past each layer's black point
		float DamagedBrightness;	// the detail's red past layer 1's black point once more (the game's shader does both)
		float Brightness;		// its green, likewise
		Coordinates PatternUv;
		float Palette[8];		// how much of each palette region is around the texel
		float4 Pattern;
		float PatternMask;		// where the pattern replaces the palette
		float Paint;			// how much paint an indexed pattern lays, worn by the detail's blue
		float PaintCover;		// that, by the paint's thickness up to 1
		float SurfaceBrightness;	// the brightness where undamaged (half under paint)
	};

	int PatternMode()
	{
		return (int)round( g_nPatternMode );
	}

	float4 DetailTexel( Texture2D t, Coordinates uv, float blackPoint, float compensation )
	{
		float4 detail = SampleAt( t, g_sTrilinearWrap, uv );
		detail.xy = blackPoint + detail.xy * compensation;
		return detail;
	}

	Texel ReadTexel( Coordinates uv )
	{
		Texel t;
		t.Uv = uv;
		t.Layers = SampleAt( g_tLayerMask, g_sTrilinearWrap, uv ).xyz;
		t.Surface = SampleAt( g_tSurface, g_sTrilinearWrap, uv );
		t.Curvature = pow( t.Surface.x, LAYERED( t.Layers, g_fCurvaturePower ) );

		// The detail tiles by the layers' scale, on the glove's own coordinates.
		t.DetailUv = Scaled( uv, LAYERED( t.Layers, g_fDetailScale ) );
		t.Detail = Layered( t.Layers,
			DetailTexel( g_tDetail1, t.DetailUv, g_fDetailBlackPoint1, g_fDetailBlackPointCompensation1 ),
			DetailTexel( g_tDetail2, t.DetailUv, g_fDetailBlackPoint2, g_fDetailBlackPointCompensation2 ),
			DetailTexel( g_tDetail3, t.DetailUv, g_fDetailBlackPoint3, g_fDetailBlackPointCompensation3 ),
			DetailTexel( g_tDetail4, t.DetailUv, g_fDetailBlackPoint4, g_fDetailBlackPointCompensation4 ) );

		float2 detail = g_fDetailBlackPoint1 + t.Detail.xy * g_fDetailBlackPointCompensation1;
		t.DamagedBrightness = detail.x;
		t.Brightness = detail.y;

		// The pattern's coordinates: the glove's with u mirrored for the left hand, transformed, nudged by the detail's
		// brightness. The game's shader mirrors u back for negative texture coordinates (never: the composite's run
		// from 0 to 1).
		t.PatternUv = Transformed( ScaledU( uv, g_fFlipFixup ), g_vPatternTexCoordXform0, g_vPatternTexCoordXform1 );
		t.PatternUv = Offset( t.PatternUv, ( ( t.Brightness * 2.0 - 1.0 ) * ( 1.0 / 1024.0 ) ) * g_fPatternDetailInfluence );
		t.PatternUv = ScaledU( t.PatternUv, uv.Uv.x < 0.0 ? -1.0 : 1.0 );

		// The palette regions around the texel: the layer mask's alpha (the region, in eighths) at the blur's taps, steps
		// of 1/1024 by g_fColorMaskBlur, each tap's weight to its region.
		[unroll]
		for ( int k = 0; k < 8; k++ )
			t.Palette[k] = 0;

		for ( int i = 0; i < 9; i++ )
		{
			float3 tap = PaletteTap( i );
			float region = floor( SampleAt( g_tLayerMask, g_sPointWrap, Offset( uv, ( tap.xy * ( 1.0 / 1024.0 ) ) * g_fColorMaskBlur ) ).w * 8.0 );

			[unroll]
			for ( int k = 0; k < 8; k++ )
				t.Palette[k] += k == region ? tap.z : 0.0;
		}

		// The pattern: the unmasked modes everywhere, the others over their palette region. An indexed pattern's
		// channels are paint, worn thin by the detail's blue.
		t.Pattern = 0;
		t.PatternMask = 1;
		t.Paint = 0;
		t.PaintCover = 0;
		t.SurfaceBrightness = t.Brightness;

		[branch]
		if ( g_flPattern > 0.5 )
		{
			int mode = PatternMode();
			t.PatternMask = mode == 1 || mode == 3 ? 1.0 : t.Palette[clamp( (int)round( g_nPatternReplaceIndex ), 0, 7 )];
			t.Pattern = SampleAt( g_tPattern, g_sTrilinearWrap, t.PatternUv );

			if ( mode < 2 )
			{
				t.Paint = saturate( t.Pattern.x + t.Pattern.y + t.Pattern.z ) * t.PatternMask * saturate( g_fPaintThickness - t.Detail.z * pow( g_fWearProgress, 4.0 ) );
				t.PaintCover = t.Paint * min( 1.0, g_fPaintThickness );
				t.SurfaceBrightness = lerp( t.Brightness, 0.5, t.Paint );
			}
		}

		return t;
	}

	// The three values the game's shader takes screen space derivatives of.
	float DerivedBrightness( Texel t ) { return t.SurfaceBrightness * t.Curvature; }
	float DerivedDamagedBrightness( Texel t ) { return t.DamagedBrightness * t.Curvature; }
	float DerivedSurface( Texel t ) { return t.Surface.x + t.Surface.y + t.Surface.z; }

	// How strong an edge the derivatives make: small changes brighten, big ones (and none) don't.
	float EdgeOf( float dx, float dy )
	{
		return ( ( 1.0 - abs( dx ) ) * ( 1.0 - abs( dy ) ) ) * saturate( abs( dx ) + abs( dy ) );
	}

	//
	// The composite.
	//
	float4 Composite( uint2 texel, float size )
	{
		Texel t = ReadTexel( TexelCoordinates( texel, size ) );

		// The texel's neighbours across and down in its 2x2 quad (screen space derivatives are their differences).
		Texel across = ReadTexel( TexelCoordinates( texel ^ uint2( 1, 0 ), size ) );
		Texel down = ReadTexel( TexelCoordinates( texel ^ uint2( 0, 1 ), size ) );

		float3 m = t.Layers;
		float detailMetalness = LAYERED( m, g_fDetailMetalness );
		float2 damageLevels = LAYERED( m, g_vDamageLevels );
		float edgeWidth = LAYERED( m, g_fDamageNormalEdgeWidth );
		float detailGrunge = LAYERED( m, g_fDetailGrunge );

		// The detail's alpha as roughness: contrast about a curve, then brightness.
		float detailRough = 1.0 - t.Detail.w;
		float detailRoughness = saturate( ( LAYERED( m, g_fDetailRoughnessContrast ) * ( ( detailRough * detailRough ) * 0.85 - 0.35 ) + 0.5 ) * LAYERED( m, g_fDetailRoughnessBrightness ) );

		Coordinates grungeUv = Transformed( t.Uv, g_vGrungeTexCoordXform0, g_vGrungeTexCoordXform1 );
		float4 grunge = Layered( m,
			SampleAt( g_tGrunge1, g_sTrilinearWrap, grungeUv ),
			SampleAt( g_tGrunge2, g_sTrilinearWrap, grungeUv ),
			SampleAt( g_tGrunge3, g_sTrilinearWrap, grungeUv ),
			SampleAt( g_tGrunge4, g_sTrilinearWrap, grungeUv ) );
		float grungeLuminance = LuminanceOf( grunge.rgb );

		float4 noiseTexel = SampleAt( g_tNoise, g_sTrilinearWrap, Scaled( t.Uv, 2.0 ) );
		float3 gloveNormal = DecodeHemiOctahedral( SampleAt( g_tNormal, g_sTrilinearWrap, t.Uv ).xy );
		float3 detailNormal = DecodeHemiOctahedral( Layered( m,
			SampleAt( g_tDetailNormal1, g_sTrilinearWrap, t.DetailUv ).xy,
			SampleAt( g_tDetailNormal2, g_sTrilinearWrap, t.DetailUv ).xy,
			SampleAt( g_tDetailNormal3, g_sTrilinearWrap, t.DetailUv ).xy,
			SampleAt( g_tDetailNormal4, g_sTrilinearWrap, t.DetailUv ).xy ) );
		float3 damageNormal = DecodeHemiOctahedral( Layered( m,
			SampleAt( g_tDamageNormal1, g_sTrilinearWrap, t.DetailUv ).xy,
			SampleAt( g_tDamageNormal2, g_sTrilinearWrap, t.DetailUv ).xy,
			SampleAt( g_tDamageNormal3, g_sTrilinearWrap, t.DetailUv ).xy,
			SampleAt( g_tDamageNormal4, g_sTrilinearWrap, t.DetailUv ).xy ) );

		//
		// The pattern. Indexed: its red, green and blue pick palette colours and lay paint, raised (flattening the
		// detail's normal, half brightness), shadowed at its edges (a blurrier read), wearing the surface less by the
		// paint's durability. Bitmap: its colour.
		//
		bool pattern = g_flPattern > 0.5;
		int mode = PatternMode();
		float roughness = detailRoughness;
		float metalness = detailMetalness;
		float wearMask = t.Surface.w;
		float3 surfaceNormal = detailNormal;
		float patternMask = 1.0;
		float paintHeight = 1.0;
		float paintShadow = 1.0;
		float3 patternColor = 0.0;

		[branch]
		if ( pattern )
		{
			if ( mode < 2 )
			{
				float3 p = t.Pattern.rgb;

				[branch]
				if ( g_fPaintThickness > 0.0 )
				{
					float3 blurred = SampleAt( g_tPattern, g_sTrilinearWrap, Biased( t.PatternUv, 0.5 + 0.1 * g_fPaintThickness ) ).rgb;
					float shadow = lerp( pow( 1.0 - blurred.x, g_fPaintShadowPower ), 1.0, p.x ) * lerp( pow( 1.0 - blurred.y, g_fPaintShadowPower ), 1.0, p.y ) * lerp( pow( 1.0 - blurred.z, g_fPaintShadowPower ), 1.0, p.z );
					paintShadow = pow( lerp( 1.0, shadow, t.PatternMask ), 0.2 );
				}

				wearMask = t.Surface.w - t.PaintCover * g_fPaintDurability;
				surfaceNormal = lerp( detailNormal, float3( 0, 0, 1 ), t.PaintCover );
				paintHeight = ( p.x + p.y + p.z ) * g_fPaintThickness;
				patternColor = lerp( lerp( lerp(
					PaletteColor( (int)round( g_vPatternPaletteIndices.x ) - 1 ),
					PaletteColor( (int)round( g_vPatternPaletteIndices.y ) - 1 ), p.x ),
					PaletteColor( (int)round( g_vPatternPaletteIndices.z ) - 1 ), p.y ),
					PaletteColor( (int)round( g_vPatternPaletteIndices.w ) - 1 ), p.z );
				roughness = detailRoughness * lerp( 1.0, g_fPatternRoughnessBrightness, t.PaintCover );
			}
			else
			{
				patternColor = t.Pattern.rgb;
			}

			metalness = lerp( detailMetalness, g_fPatternMetalness, t.PatternMask );
			patternMask = t.PatternMask;
		}

		// The palette's colours by how much of each region is around, linearised.
		float3 palette = 0;

		[unroll]
		for ( int k = 0; k < 8; k++ )
			palette += PaletteColor( k ) * t.Palette[k];

		palette = SrgbToLinear( palette );

		//
		// Wear: the surface's wear mask by its curvature and the grunge, the layers' curvature boost, through the layers'
		// damage levels. Damage spreads further (four times the wear) by the detail's blue.
		//
		float wearBase = t.Curvature * wearMask;
		float curvatureBoost = LAYERED( m, g_fCurvatureWearBoost ) * t.Curvature;
		float wearValue = saturate( wearBase * grunge.w + curvatureBoost );
		float damage = smoothstep( damageLevels.x, damageLevels.y, wearValue * g_fWearProgress );
		float intact = 1.0 - damage;
		float damageSpread = saturate( damage + ( saturate( smoothstep( damageLevels.x, damageLevels.y, wearValue * ( g_fWearProgress * 4.0 ) ) ) * intact ) * t.Detail.z );

		// The base colour: the palette, the pattern's colour over its region where it isn't damaged.
		float3 baseColor = pattern ? lerp( palette, lerp( SrgbToLinear( patternColor ), palette, damageSpread ), patternMask ) : palette;
		float baseLuminance = LuminanceOf( baseColor );

		// The brightness the colour takes: the detail's (undamaged or damaged), brighter at the weave's edges where the
		// surface itself is flat.
		float edgeIntact = EdgeOf( DerivedBrightness( across ) - DerivedBrightness( t ), DerivedBrightness( down ) - DerivedBrightness( t ) );
		float edgeDamaged = EdgeOf( DerivedDamagedBrightness( across ) - DerivedDamagedBrightness( t ), DerivedDamagedBrightness( down ) - DerivedDamagedBrightness( t ) );
		float flatness = ( 1.0 - saturate( abs( DerivedSurface( across ) - DerivedSurface( t ) ) ) ) * ( 1.0 - saturate( abs( DerivedSurface( down ) - DerivedSurface( t ) ) ) );
		float brightness = min( 1.0, lerp( t.SurfaceBrightness, t.DamagedBrightness, damageSpread ) + ( lerp( edgeIntact, edgeDamaged, damage ) * flatness ) );

		// The base colour (brighter by curvature) at that brightness, held to three times its brightest channel.
		float3 boosted = max( baseColor, baseColor * ( 1.0 + t.Curvature * 0.5 ) );
		float target = LuminanceOf( brightness.xxx );
		float3 hue = normalize( max( boosted, 0.001 ) );
		float3 albedo = saturate( hue * min( target / LuminanceOf( hue ), ( 3.0 * target ) * Brightest( boosted ) ) );

		// Bleaching: wear where the surface's blue says (less under raised paint), the layers' damage bleaching where
		// damaged; toward the grunge's colour (saturated and brightened by the layers' grime settings).
		float bleach = lerp( ( ( g_fWearProgress * t.Surface.z ) * LAYERED( m, g_fWearBleaching ) ) * lerp( t.Detail.z, 0.0, saturate( paintHeight - 1.0 ) ), LAYERED( m, g_fDamageBleaching ), damage );
		float3 grime = ShiftSaturation( grunge.rgb, grunge.rgb, grungeLuminance, LAYERED( m, g_fGrimeSaturation ) );
		float3 grimed = ( albedo * grunge.rgb ) * 2.0;
		float3 color = lerp( lerp( albedo, grimed, ( detailGrunge * ( 1.0 + g_fWearProgress * detailGrunge ) ) * intact ), ( grime * ( 1.0 + LAYERED( m, g_fGrimeBrightness ) ) ) * brightness, bleach );

		// Damage: saturated toward the base colour's hue and brightened by the layers' settings.
		color = lerp( color, ShiftSaturation( color, baseColor, LuminanceOf( color ), LAYERED( m, g_fDamageSaturation ) ) * ( 1.0 + LAYERED( m, g_fDamageBrightness ) ), damage );

		// Dirt: the grunge multiplied in with wear where the surface is concave and occluded, less on bright colours.
		color = lerp( color, grunge.rgb * color, ( smoothstep( 0.45, 0.75, ( 1.0 - ( t.Surface.x * t.Surface.y ) * t.Surface.y ) * g_fWearProgress ) * ( 1.0 - baseLuminance * 0.5 ) ) * LAYERED( m, g_fGrungeMax ) );

		// Hue variation: two shifts of the colour (toward yellow-green and toward blue, each turned by the colour's
		// strongest channel) at half its luminance, by how dark the base is and how far the colour is from grey, mixed by
		// the noise and the occlusion.
		float halfLuminance = LuminanceOf( color ) * 0.5;
		float3 yellowHue = float3( 0.6, 1.0, 0.0 );
		float3 blueHue = float3( 0.2, 0.6, 1.0 );
		if ( color.x > color.y && color.x > color.z )
		{
			yellowHue += color.zxy;
			blueHue += color.zyx;
		}
		else if ( color.y > color.x && color.y > color.z )
		{
			yellowHue += color.zxy;
			blueHue += color.yzx;
		}

		float variation = pow( 1.0 - baseLuminance, 3.5 ) * 0.3 + ( length( color - halfLuminance ) * 0.15 + 0.05 );
		float occlusion = pow( t.Surface.y, 8.0 );
		float3 towardBlue = ShiftSaturation( color, blueHue, halfLuminance, variation );
		float3 towardYellow = ShiftSaturation( color, yellowHue, halfLuminance, variation );
		float3 finalColor = saturate( lerp( lerp( towardBlue, towardYellow, saturate( occlusion * noiseTexel.x ) ), color, saturate( noiseTexel.z * ( 0.5 + occlusion * 0.5 ) ) ) );

		//
		// The damage's edge: how the wear changes across four diagonal taps of the grunge (wider over raised paint), as a
		// slope for the normal and an edge for the metalness, roughness and occlusion.
		//
		float2 wearSlope = 0;
		float edgeSum = 0;
		for ( int i = 0; i < 4; i++ )
		{
			Coordinates tap = Offset( grungeUv, ( ( Corner( i ) * ( 1.0 / 1024.0 ) ) * edgeWidth ) * ( 1.0 + paintHeight ) );
			float tapWear = Layered( m,
				SampleAt( g_tGrunge1, g_sTrilinearWrap, tap ).w,
				SampleAt( g_tGrunge2, g_sTrilinearWrap, tap ).w,
				SampleAt( g_tGrunge3, g_sTrilinearWrap, tap ).w,
				SampleAt( g_tGrunge4, g_sTrilinearWrap, tap ).w );
			float difference = damage - smoothstep( damageLevels.x, damageLevels.y, saturate( tapWear * wearBase + curvatureBoost ) * g_fWearProgress );
			wearSlope -= Corner( i ) * difference;
			edgeSum += abs( difference );
		}

		float edge = edgeSum * 0.25;
		wearSlope *= edgeWidth * 0.25;

		// The paint's edge (indexed modes, pattern or not): how the paint's height changes across four diagonal taps of
		// the pattern, where undamaged, turned back from the pattern's rotation (u mirrored as the glove's).
		float2 paintSlope = 0;

		[branch]
		if ( mode < 2 )
		{
			float tapStep = ( ( 1.0 / 1024.0 ) * g_fPaintThickness ) * ( 1.0 - edge );
			for ( int i = 0; i < 4; i++ )
			{
				float3 p = SampleAt( g_tPattern, g_sTrilinearWrap, Offset( t.PatternUv, ( Corner( i ) * tapStep ) * 2.0 ) ).rgb;
				paintSlope -= ( ( Corner( i ) * ( paintHeight - ( p.x + p.y + p.z ) * g_fPaintThickness ) ) * ( saturate( intact ) * patternMask ) ) * ( 1.0 - min( min( p.x, p.z - min( p.x, p.y ) ), p.y - p.x ) ) * 0.5;
			}

			paintSlope = float2( dot( paintSlope, g_vPatternTexRotationXform0.xy ) * g_fFlipFixup, dot( paintSlope, g_vPatternTexRotationXform1.xy ) );

			// Mirrored for positive texture coordinates (always here).
			if ( t.Uv.Uv.x > 0.0 )
				paintSlope.x = -paintSlope.x;
		}

		//
		// The outputs' values.
		//
		// The normal: the glove's, plus the detail (or damage) normal's slope by the layers' contrast, the wear edge's
		// slope turned back from the grunge's rotation, and the paint's. (The game's shader flips its green and back.)
		float3 layerNormal = lerp( surfaceNormal, damageNormal, damage );
		float2 detailSlope = layerNormal.xy * LAYERED( m, g_fDetailNormalContrast );
		float2 edgeSlope = float2( -dot( wearSlope, g_vGrungeTexRotationXform0.xy ), dot( wearSlope, g_vGrungeTexRotationXform1.xy ) );
		float3 normal = normalize( float3( ( ( gloveNormal.xy + detailSlope ) - edgeSlope ) + paintSlope, gloveNormal.z ) );

		float finalMetalness = lerp( lerp( metalness, LAYERED( m, g_fDamageMetalness ), damage ), LAYERED( m, g_fDamageEdgeMetalness ), edge );
		float cloth = lerp( LAYERED( m, g_fDetailCloth ), LAYERED( m, g_fDamageCloth ), max( damageSpread, edge ) ) * ( 1.0 - finalMetalness );

		// Roughness: the detail's (times the layers' anisotropy with S_ANISOTROPIC_GLOSS: a negative scalar lowers the
		// first, a positive one the second), toward the grunge's where bleached, the detail's red's curve where damaged,
		// the layers' edge roughness at the damage's edge.
		float2 anisotropy = 1.0;
		if ( g_flAnisotropicGloss > 0.5 )
		{
			float scalar = LAYERED( m, g_fDetailAnisotropyScalar );
			anisotropy = float2( scalar < 0.0 ? 1.0 + scalar : 1.0, scalar > 0.0 ? 1.0 - scalar : 1.0 );
		}

		float damagedRough = 1.0 - t.DamagedBrightness;
		float damageRoughness = saturate( ( LAYERED( m, g_fDamageRoughnessContrast ) * ( ( damagedRough * damagedRough ) * 0.85 - 0.35 ) + 0.5 ) * LAYERED( m, g_fDamageRoughnessBrightness ) );
		float2 finalRoughness = lerp( lerp( anisotropy * lerp( min( 1.0, roughness ), ( 1.0 - grungeLuminance ) + LAYERED( m, g_fGrungeRoughnessBrightness ), bleach ), damageRoughness, damage ), LAYERED( m, g_fDamageEdgeRoughness ), edge );

		// Occlusion: the surface's, darker where the normal tilts, at the damage's edges and in the paint's shadow.
		float ambientOcclusion = ( ( t.Surface.y * pow( layerNormal.z, 0.2 ) ) * ( 1.0 - edgeSum * 0.05 ) ) * lerp( paintShadow, 1.0, damage );

		float4 result;
		int outputMode = (int)round( g_flOutputMode );

		[branch]
		if ( outputMode == 0 )
		{
			// The colour, fitted into a physically plausible albedo range by the wear: its hue at a brightness between the
			// darkest the levels allow (or the grimed albedo's luminance, if lower) and their brightest.
			float3 direction = normalize( max( 0.0003, finalColor ) );
			float3 levels = lerp( TextileAlbedoLevels(), MetallicTextileAlbedoLevels(), finalMetalness );
			float3 fitted = ( direction * lerp( min( levels.x, LuminanceOf( lerp( albedo, grimed, detailGrunge ) ) ), levels.z, saturate( pow( Brightest( finalColor ), levels.y ) ) ) ) / Brightest( direction );
			result = float4( lerp( finalColor, fitted, g_fWearProgress ), 1.0 );
		}
		else if ( outputMode == 1 )
		{
			result = float4( EncodeHemiOctahedral( normal ), finalRoughness.x, 1.0 );
		}
		else if ( outputMode == 2 )
		{
			result = float4( 0.0, finalMetalness, cloth, 1.0 );
		}
		else if ( outputMode == 3 )
		{
			result = float4( ambientOcclusion.xxx, 1.0 );
		}
		else
		{
			result = float4( finalRoughness, 0.0, 1.0 );
		}

		if ( cloth < 0.0 )
			result.w = g_flSheenScale;

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
