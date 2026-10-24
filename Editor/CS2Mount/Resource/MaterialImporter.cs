using System;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Loads a compiled CS2 material (vmat_c) as an s&amp;box <see cref="Material"/>.
/// </summary>
internal sealed class MaterialLoader( string filePath ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var source = new CompiledMaterial( Host.ReadFile( filePath ) );

		// Created materials get a plain name like the Source 1 mount's, not the mount:// path the loader serves them from.
		return new MaterialImporter( Host, source, $"{Host.Ident}/{RelativePath}" ).Import();
	}
}

/// <summary>
/// Builds an s&amp;box material from a CS2 material, on the mount's port of the game shader it names, with its parameters
/// translated the way that shader consumes them.
/// </summary>
internal sealed class MaterialImporter( CS2Mount mount, CompiledMaterial source, string name )
{
	public const string VertexLitGenericShader = "shaders/cs2_vertexlitgeneric.shader";
	public const string EffectsShader = "shaders/cs2_effects.shader";
	public const string StaticOverlayShader = "shaders/cs2_static_overlay.shader";
	public const string EnvironmentShader = "shaders/cs2_environment.shader";
	public const string LightmappedGenericShader = "shaders/cs2_lightmappedgeneric.shader";
	public const string FoliageShader = "shaders/cs2_foliage.shader";
	public const string ComplexShader = "shaders/cs2_complex.shader";
	public const string BlackUnlitShader = "shaders/cs2_black_unlit.shader";
	public const string GlassShader = "shaders/cs2_glass.shader";
	public const string CharacterShader = "shaders/cs2_character.shader";
	public const string WeaponShader = "shaders/cs2_weapon.shader";
	public const string GenericShader = "shaders/cs2_generic.shader";
	public const string UnlitGenericShader = "shaders/cs2_unlitgeneric.shader";
	public const string DecalModulateShader = "shaders/cs2_decalmodulate.shader";
	public const string Simple2WayBlendShader = "shaders/cs2_simple_2way_blend.shader";
	public const string WaterFancyShader = "shaders/cs2_water_fancy.shader";

	/// <summary>
	/// Every texture of each shader and what it reads when the material has none: the game shader's own default.
	/// s&amp;box only fills those in for compiled materials; a created one reads an unbound texture as the error
	/// checkerboard.
	/// </summary>
	private static readonly Dictionary<string, (string Texture, bool Srgb, Vector4 Value)[]> DefaultTextures = new()
	{
		[VertexLitGenericShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 0 )),
			("g_tTintMask", false, new( 1, 1, 1, 0 )),
			("g_tNormal", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tMetalness", false, new( 0, 0, 0, 0 )),
			("g_tAmbientOcclusion", false, new( 1, 1, 1, 0 )),
			("g_tDetail", false, new( 0.5f, 0.5f, 0.5f, 1 )),
			("g_tDetailMask", false, new( 1, 1, 1, 1 )),
			("g_tSelfIllumMask", true, new( 0, 0, 0, 0 )),
		],
		[EffectsShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tMask1", false, new( 1, 1, 1, 0 )),
			("g_tMask2", false, new( 1, 1, 1, 0 )),
			("g_tMask3", false, new( 1, 1, 1, 0 )),
		],
		[StaticOverlayShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tNormal", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tMetalness", false, new( 0, 0, 0, 0 )),
			("g_tAmbientOcclusion", false, new( 1, 1, 1, 0 )),
			("g_tSelfIllumMask", true, new( 0, 0, 0, 0 )),
		],
		[EnvironmentShader] =
		[
			("g_tColor1", true, new( 1, 1, 1, 1 )),
			("g_tHeight1", false, new( 0.5f, 1, 1, 0 )),
			("g_tNormal1", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tNormalDetail1", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tColor2", true, new( 1, 1, 1, 1 )),
			("g_tHeight2", false, new( 0.5f, 1, 1, 0 )),
			("g_tNormal2", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tNormalDetail2", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tColor3", true, new( 1, 1, 1, 1 )),
			("g_tHeight3", false, new( 0.5f, 1, 1, 0 )),
			("g_tNormal3", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tNormalDetail3", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tSharedColorOverlay", true, new( 0.5f, 0.5f, 0.5f, 1 )),
		],
		[LightmappedGenericShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tLayer1NormalRoughness", false, new( 0.5f, 0.5f, 1, 0 )),
			("g_tLayer1AmbientOcclusion", false, new( 1, 1, 1, 0 )),
			("g_tLayer1Detail", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tLayer2Color", true, new( 1, 1, 1, 1 )),
			("g_tLayer2NormalRoughness", false, new( 0.5f, 0.5f, 1, 0 )),
			("g_tLayer2AmbientOcclusion", false, new( 1, 1, 1, 0 )),
			("g_tLayer2Detail", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tBlendModulation", false, new( 0, 0.5f, 0, 0.5f )),
		],
		[FoliageShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tNormal", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tAmbientOcclusion", false, new( 1, 1, 1, 0 )),
			("g_tTintMask", false, new( 1, 1, 1, 0 )),
			("g_tTransmissiveColor", true, new( 1, 1, 1, 0 )),
		],
		[ComplexShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tNormal", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tAmbientOcclusion", false, new( 1, 1, 1, 0 )),
			("g_tTintMask", false, new( 1, 1, 1, 0 )),
			("g_tMetalness", false, new( 0, 0, 0, 0 )),
			("g_tSelfIllumMask", true, new( 0, 0, 0, 0 )),
			("g_tDetail", false, new( 0.5f, 0.5f, 0.5f, 1 )),
			("g_tDetailMask", false, new( 1, 1, 1, 1 )),
			("g_tNormalDetail", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tDecal", true, new( 0, 0, 0, 0 )),
			("g_tTransmissiveColor", true, new( 1, 1, 1, 0 )),
		],
		[GlassShader] =
		[
			("g_tGlassDust", true, new( 0, 0, 0, 0 )),
			("g_tGlassTintColor", true, new( 1, 1, 1, 0 )),
			("g_tNormal", false, new( 0.5f, 0.5f, 0.5f, 0 )),
		],
		[CharacterShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tNormal", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tAmbientOcclusion", false, new( 1, 1, 1, 0 )),
			("g_tMetalness", false, new( 0, 0, 0, 1 )),
			("g_tAnisoGloss", false, new( 0.5f, 0.5f, 0, 0 )),
			("g_tTintMask", false, new( 1, 1, 1, 0 )),
			("g_tSssMask", false, new( 1, 1, 1, 0 )),
			("g_tDiffuseFalloff", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tPatch0", true, new( 0, 0, 0, 0 )),
			("g_tPatch0Backing", true, new( 0, 0, 0, 0 )),
			("g_tPatch1", true, new( 0, 0, 0, 0 )),
			("g_tPatch1Backing", true, new( 0, 0, 0, 0 )),
			("g_tPatch2", true, new( 0, 0, 0, 0 )),
			("g_tPatch2Backing", true, new( 0, 0, 0, 0 )),
			("g_tEyeAlbedo1", true, new( 1, 1, 1, 0 )),
			("g_tEyeMask1", false, new( 0, 0, 0, 0 )),
		],
		[WeaponShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tNormal", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tAmbientOcclusion", false, new( 1, 1, 1, 0 )),
			("g_tMetalness", false, new( 0.5f, 0, 0, 0 )),
			("g_tTintMask", false, new( 1, 1, 1, 0 )),
			("g_tSelfIllumMask", true, new( 0, 0, 0, 0 )),
		],
		[GenericShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tNormal", false, new( 0.5f, 0.5f, 1, 0.5f )),
			("g_tRoughness", false, new( 0.5f, 0.5f, 0.5f, 0.5f )),
			("g_tSelfIllumMask", false, new( 1, 1, 1, 0 )),
		],
		[UnlitGenericShader] =
		[
			("g_tColor", true, new( 1, 1, 1, 1 )),
			("g_tColor2", true, new( 1, 1, 1, 1 )),
		],
		[DecalModulateShader] =
		[
			("g_tColor", false, new( 0.5f, 0.5f, 0.5f, 0 )),
		],
		[Simple2WayBlendShader] =
		[
			("g_tColorA", true, new( 1, 1, 1, 1 )),
			("g_tNormalA", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tColorB", true, new( 1, 1, 1, 1 )),
			("g_tNormalB", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tMask", false, new( 1, 1, 1, 0 )),
			("g_tTintMask", false, new( 1, 1, 1, 0 )),
		],
		[WaterFancyShader] =
		[
			("g_tWavesNormalHeight", false, new( 0.5f, 0.5f, 0.5f, 0 )),
			("g_tFoam", false, new( 0.5f, 0.5f, 0, 0 )),
			("g_tDebris", true, new( 1, 1, 1, 0 )),
			("g_tDebrisNormal", false, new( 0.5f, 0.5f, 0.5f, 0 )),
		],
	};

	/// <summary>
	/// Shaders not ported yet: these show their base layer on the vertex lit generic shader in the meantime. Their
	/// colour and normal textures by the names each shader gives them.
	/// </summary>
	private static readonly string[] StandInColors = ["g_tColor", "g_tColor1", "g_tLayer1Color", "g_tGlassTintColor"];
	private static readonly string[] StandInNormals = ["g_tNormal", "g_tNormal1", "g_tLayer1NormalRoughness"];

	private static readonly Dictionary<uint, Texture> SolidTextures = [];

	private Material _material;
	private readonly HashSet<string> _boundTextures = [];

	public Material Import()
	{
		if ( source.ShaderName == "sky.vfx" )
			return ImportSky();

		var shader = source.ShaderName switch
		{
			"csgo_effects.vfx" => EffectsShader,
			"csgo_static_overlay.vfx" => StaticOverlayShader,
			"csgo_environment.vfx" or "csgo_environment_blend.vfx" => EnvironmentShader,
			"csgo_lightmappedgeneric.vfx" => LightmappedGenericShader,
			"csgo_foliage.vfx" => FoliageShader,
			"csgo_complex.vfx" or "csgo_simple.vfx" => ComplexShader,
			"csgo_black_unlit.vfx" => BlackUnlitShader,
			"csgo_glass.vfx" => GlassShader,
			"csgo_character.vfx" => CharacterShader,
			"csgo_weapon.vfx" => WeaponShader,
			"generic.vfx" => GenericShader,
			"csgo_unlitgeneric.vfx" => UnlitGenericShader,
			"csgo_decalmodulate.vfx" => DecalModulateShader,
			"csgo_simple_2way_blend.vfx" => Simple2WayBlendShader,
			"csgo_water_fancy.vfx" => WaterFancyShader,
			_ => VertexLitGenericShader,
		};

		_material = Material.Create( name, shader, false )
			?? throw new InvalidOperationException( $"Couldn't create a material with {shader}." );

		switch ( source.ShaderName )
		{
			case "csgo_vertexlitgeneric.vfx":
				SetupVertexLitGeneric();
				break;

			case "csgo_effects.vfx":
				SetupEffects();
				break;

			case "csgo_static_overlay.vfx":
				SetupStaticOverlay();
				break;

			case "csgo_environment.vfx":
				SetupEnvironment( false );
				break;

			case "csgo_environment_blend.vfx":
				SetupEnvironment( true );
				break;

			case "csgo_lightmappedgeneric.vfx":
				SetupLightmappedGeneric();
				break;

			case "csgo_foliage.vfx":
				SetupFoliage();
				break;

			case "csgo_complex.vfx":
				SetupComplex( false );
				break;

			case "csgo_simple.vfx":
				SetupComplex( true );
				break;

			case "csgo_glass.vfx":
				SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );
				BindTexture( "g_tGlassDust", "g_tGlassDust" );
				BindTexture( "g_tGlassTintColor", "g_tGlassTintColor" );
				BindTexture( "g_tNormal", "g_tNormal" );
				_material.Set( "g_flTranslucencyRemap", GetVector2( "g_flTranslucencyRemap", 0 ) );
				_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
				_material.Set( "g_flReflectance", MaterialParameters.Reflectance );
				_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );
				_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
				break;

			case "csgo_black_unlit.vfx":
				SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );
				_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
				break;

			case "csgo_character.vfx":
				SetupCharacter();
				break;

			case "csgo_weapon.vfx":
				SetupWeapon();
				break;

			case "generic.vfx":
				SetupGeneric();
				break;

			case "csgo_unlitgeneric.vfx":
				SetupUnlitGeneric();
				break;

			case "csgo_decalmodulate.vfx":
				SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );
				BindTexture( "g_tColor", "g_tColor" );
				SetTexCoordXform();
				_material.Set( "g_flVertexColor", Combo( "F_VERTEX_COLOR" ) ? 1f : 0f );
				_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
				break;

			case "csgo_simple_2way_blend.vfx":
				SetupSimple2WayBlend();
				break;

			case "csgo_water_fancy.vfx":
				SetupWaterFancy();
				break;

			default:
				SetupStandIn();
				break;
		}

		// CS2's surface names mostly don't exist in s&box, so the closest s&box surface stands in.
		if ( source.StringAttributes.TryGetValue( "PhysicsSurfaceProperties", out var surface ) && mount.GetSurface( surface ) is { } mapped )
			_material.Attributes.Set( "PhysicsSurfaceProperties", mapped.ResourceName );

		BindDefaultTextures( shader );
		return _material;
	}

	/// <summary>
	/// csgo_vertexlitgeneric.vfx: its static combos as the port's features and uniforms, and its parameters with the
	/// expressions the game's shader evaluates on them.
	/// </summary>
	private void SetupVertexLitGeneric()
	{
		SetRenderStateFeatures();

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormal", "g_tNormal" );
		BindTexture( "g_tMetalness", "g_tMetalness" );
		BindTexture( "g_tAmbientOcclusion", "g_tAmbientOcclusion" );

		var tintMask = Combo( "F_TINT_MASK" );
		if ( tintMask )
			BindTexture( "g_tTintMask", "g_tTintMask" );

		_material.Set( "g_flTintMask", tintMask ? 1f : 0f );
		_material.Set( "g_flInverseBlendTint", Combo( "F_NOTINT" ) ? -1f : 1f - (tintMask ? 1f : 0f) );
		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );
		_material.Set( "g_flAmbientOcclusionScale", Float( "g_flAmbientOcclusionScale", 0 ) );
		_material.Set( "g_flSecondaryUv", Combo( "F_FORCE_UV2" ) || Combo( "F_DECAL_TEXTURE" ) ? 1f : 0f );
		_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );

		// The game's combo rules: direct specular only comes with indirect.
		var specularIndirect = Combo( "F_SPECULAR_INDIRECT" );
		_material.Set( "g_flSpecularIndirect", specularIndirect ? 1f : 0f );
		_material.Set( "g_flSpecularDirect", specularIndirect && Combo( "F_SPECULAR_DIRECT" ) ? 1f : 0f );

		SetTexCoordTransforms();

		if ( Combo( "F_DETAIL_TEXTURE" ) )
		{
			BindTexture( "g_tDetail", "g_tDetail" );
			BindTexture( "g_tDetailMask", "g_tDetailMask" );
			_material.Set( "g_flDetailBlendFactor", Float( "g_flDetailBlendFactor", 1 ) );
			_material.Set( "g_flDetailBlendToFull", Float( "g_flDetailBlendToFull", 0 ) );

			var (xform, offset) = MaterialParameters.DetailTexCoordXform( GetVector2( "g_vDetailTexCoordScale", 1 ), Float( "g_flDetailTexCoordRotation", 0 ), GetVector2( "g_vDetailTexCoordOffset", 0 ) );
			_material.Set( "g_vDetailTexCoordXform", xform );
			_material.Set( "g_vDetailTexCoordOffset", offset );
		}

		if ( Combo( "F_SELF_ILLUM" ) )
		{
			BindTexture( "g_tSelfIllumMask", "g_tSelfIllumMask" );
			_material.Set( "g_vSelfIllumTintScaled", MaterialParameters.SelfIllumTintScaled( Float( "g_flSelfIllumBrightness", 0 ), Float( "g_flSelfIllumScale", 1 ), GetVector3( "g_vSelfIllumTint", 1 ) ) );
			_material.Set( "g_vSelfIllumScrollSpeed", GetVector2( "g_vSelfIllumScrollSpeed", 0 ) );
			_material.Set( "g_flSelfIllumAlbedoFactor", Float( "g_flSelfIllumAlbedoFactor", 1 ) );
		}

		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flAntiAliasedEdgeStrength", Float( "g_flAntiAliasedEdgeStrength", 1 ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );
	}

	/// <summary>
	/// csgo_effects.vfx: unlit translucent effect meshes, their masks, fresnel, depth feathering and distance fade.
	/// </summary>
	private void SetupEffects()
	{
		SetFeature( "F_ADDITIVE_BLEND", Combo( "F_ADDITIVE_BLEND" ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tMask1", "g_tMask1" );
		BindTexture( "g_tMask2", "g_tMask2" );
		BindTexture( "g_tMask3", "g_tMask3" );

		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_vTexCoordScrollSpeed", GetVector2( "g_vTexCoordScrollSpeed", 0 ) );
		_material.Set( "g_flColorBoost", Float( "g_flColorBoost", 1 ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );

		for ( var mask = 1; mask <= 3; mask++ )
		{
			_material.Set( $"g_vMask{mask}Scale", GetVector2( $"g_vMask{mask}Scale", 1 ) );
			_material.Set( $"g_vMask{mask}PanSpeed", GetVector2( $"g_vMask{mask}PanSpeed", 0 ) );
		}

		_material.Set( "g_flFresnelExponent", Float( "g_flFresnelExponent", 0.001f ) );
		_material.Set( "g_flFresnelFalloff", Float( "g_flFresnelFalloff", 1 ) );
		_material.Set( "g_flFresnelMin", Float( "g_flFresnelMin", 0 ) );
		_material.Set( "g_flFresnelMax", Float( "g_flFresnelMax", 1 ) );

		_material.Set( "g_flDepthFeather", Combo( "F_DEPTH_FEATHER" ) ? 1f : 0f );
		_material.Set( "g_flFeatherDistance", Float( "g_flFeatherDistance", 1 ) );
		_material.Set( "g_flFeatherFalloff", Float( "g_flFeatherFalloff", 1 ) );

		_material.Set( "g_flFadeDistance", Float( "g_flFadeDistance", 1 ) );
		_material.Set( "g_flFadeFalloff", Float( "g_flFadeFalloff", 1 ) );
		_material.Set( "g_flFadeMin", Float( "g_flFadeMin", 0 ) );
		_material.Set( "g_flFadeMax", Float( "g_flFadeMax", 1 ) );

		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
		_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );
	}

	/// <summary>
	/// csgo_static_overlay.vfx: its blend mode, lit or unlit, and the material side of the lit overlays.
	/// </summary>
	private void SetupStaticOverlay()
	{
		_material.SetFeature( "F_BLEND_MODE", (int)Math.Clamp( source.GetInt( "F_BLEND_MODE" ), 0, 6 ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormal", "g_tNormal" );
		BindTexture( "g_tMetalness", "g_tMetalness" );
		BindTexture( "g_tAmbientOcclusion", "g_tAmbientOcclusion" );
		BindTexture( "g_tSelfIllumMask", "g_tSelfIllumMask" );

		_material.Set( "g_flLit", Combo( "F_LIT" ) ? 1f : 0f );
		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		SetTexCoordTransforms();

		SetColorAdjust( "g_vTextureColorAdjust", "g_tColor", "g_vTextureColorCorrectionTint", "g_fTextureColorContrast", "g_fTextureColorSaturation", "g_fTextureColorBrightness" );

		_material.Set( "g_fTextureNormalContrast", Float( "g_fTextureNormalContrast", 1 ) );
		_material.Set( "g_fTextureRoughnessContrast", Float( "g_fTextureRoughnessContrast", 1 ) );
		_material.Set( "g_fTextureRoughnessBrightness", Float( "g_fTextureRoughnessBrightness", 1 ) );

		_material.Set( "g_flAutomaticPBRColorFitting", Combo( "F_AUTOMATIC_PBR_COLOR_FITTING" ) ? 1f : 0f );
		_material.Set( "g_vAlbedoLevels", MaterialParameters.AlbedoLevels( -0.045f, 0.4f, 1.15f ) );
		_material.Set( "g_vMetallicAlbedoLevels", MaterialParameters.AlbedoLevels( -0.45f, 0.4f, 1.08f ) );
		_material.Set( "g_vDarkMetallicAlbedoLevels", MaterialParameters.AlbedoLevels( -0.1f, 0.4f, 1.08f ) );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );

		_material.Set( "g_vSelfIllumTintScaled", MaterialParameters.SelfIllumTintScaled( Float( "g_flSelfIllumBrightness", 0 ), Float( "g_flSelfIllumScale", 1 ), GetVector3( "g_vSelfIllumTint", 1 ) ) );
		_material.Set( "g_vSelfIllumScrollSpeed", GetVector2( "g_vSelfIllumScrollSpeed", 0 ) );
		_material.Set( "g_flSelfIllumAlbedoFactor", Float( "g_flSelfIllumAlbedoFactor", 1 ) );

		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flAntiAliasedEdgeStrength", Float( "g_flAntiAliasedEdgeStrength", 1 ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
	}

	/// <summary>
	/// csgo_environment.vfx (one layer) and csgo_environment_blend.vfx (up to three): each layer's coordinates and
	/// transforms, colour adjustment, and (for the blend shader) the settings csgo_environment bakes into its textures;
	/// the blend shader's blending, borders, bevels and shared colour overlay.
	/// </summary>
	private void SetupEnvironment( bool blend )
	{
		SetFeature( "F_ALPHA_TEST", Combo( "F_ALPHA_TEST" ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );
		SetFeature( "F_DO_NOT_CAST_SHADOWS", Combo( "F_DO_NOT_CAST_SHADOWS" ) );

		var layers = blend ? (Combo( "F_ENABLE_LAYER_3" ) ? 3 : 2) : 1;
		var detail = Combo( "F_DETAIL_NORMAL" );

		_material.Set( "g_flBlend", blend ? 1f : 0f );
		_material.Set( "g_flBlendPs", blend ? 1f : 0f );
		_material.Set( "g_flLayer3", layers == 3 ? 1f : 0f );
		_material.Set( "g_flLayer3Ps", layers == 3 ? 1f : 0f );
		_material.Set( "g_flUseNewBlending", Combo( "F_USE_NEW_BLENDING" ) ? 1f : 0f );
		_material.Set( "g_flBlendSoftnessDistanceModifierStrength", Float( "g_flBlendSoftnessDistanceModifierStrength", 1 ) );

		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );
		_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );

		var alphaReference = source.FloatParams.TryGetValue( "g_flAlphaTestReference1", out var reference1 ) ? reference1 : Float( "g_flAlphaTestReference", 0.5f );
		_material.Set( "g_flAlphaTestReference", alphaReference );
		_material.Set( "g_flAntiAliasedEdgeStrength", source.FloatParams.TryGetValue( "g_flAntiAliasedEdgeStrength1", out var edge1 ) ? edge1 : Float( "g_flAntiAliasedEdgeStrength", 1 ) );

		for ( var n = 1; n <= layers; n++ )
		{
			BindTexture( $"g_tColor{n}", $"g_tColor{n}" );
			BindTexture( $"g_tHeight{n}", $"g_tHeight{n}" );
			BindTexture( $"g_tNormal{n}", $"g_tNormal{n}" );

			var uvSet = (float)source.GetInt( $"g_nUVSet{n}", 1 );
			_material.Set( $"g_flUVSet{n}", uvSet );
			_material.Set( $"g_flUVSetPs{n}", uvSet );

			var scale = GetVector2( $"g_vTexCoordScale{n}", 1 );
			var rotation = Float( $"g_flTexCoordRotation{n}", 0 );
			var (row0, row1) = MaterialParameters.TexCoordXform( scale, rotation, GetVector2( $"g_vTexCoordCenter{n}", 0.5f ), GetVector2( $"g_vTexCoordOffset{n}", 0 ) );
			_material.Set( $"g_vTexCoordXform0_{n}", row0 );
			_material.Set( $"g_vTexCoordXform1_{n}", row1 );
			_material.Set( $"g_vTexCoordScale{n}", scale );
			_material.Set( $"g_vNormalXform_{n}", MaterialParameters.RotationXform( rotation ) );

			_material.Set( $"g_flDetailNormal{n}", detail ? 1f : 0f );
			if ( detail )
			{
				BindTexture( $"g_tNormalDetail{n}", $"g_tNormalDetail{n}" );

				var detailSet = (float)source.GetInt( $"g_nDetailUVSet{n}", -1 );
				_material.Set( $"g_flDetailUVSet{n}", detailSet );
				_material.Set( $"g_flDetailUVSetPs{n}", detailSet < 0 ? uvSet : detailSet );

				var detailScale = GetVector2( $"g_vDetailTexCoordScale{n}", 1 );
				var detailRotation = Float( $"g_flDetailTexCoordRotation{n}", 0 );
				var (detail0, detail1) = MaterialParameters.TexCoordXform( detailScale, detailRotation, GetVector2( $"g_vDetailTexCoordCenter{n}", 0.5f ), GetVector2( $"g_vDetailTexCoordOffset{n}", 0 ) );
				_material.Set( $"g_vDetailTexCoordXform0_{n}", detail0 );
				_material.Set( $"g_vDetailTexCoordXform1_{n}", detail1 );
				_material.Set( $"g_vDetailTexCoordScale{n}", detailScale );
				_material.Set( $"g_vDetailNormalXform_{n}", MaterialParameters.RotationXform( detailRotation ) );
				_material.Set( $"g_fDetailTextureNormalContrast{n}", Float( $"g_fDetailTextureNormalContrast{n}", 1 ) );
			}

			// csgo_environment bakes these into its textures; the blend shader applies them.
			_material.Set( $"g_fTextureNormalContrast{n}", blend ? Float( $"g_fTextureNormalContrast{n}", 1 ) : 1f );
			_material.Set( $"g_fTextureRoughnessContrast{n}", blend ? Float( $"g_fTextureRoughnessContrast{n}", 1 ) : 1f );
			_material.Set( $"g_fTextureRoughnessBrightness{n}", blend ? Float( $"g_fTextureRoughnessBrightness{n}", 1 ) : 1f );
			_material.Set( $"g_fTintMaskContrast{n}", blend ? Float( $"g_fTintMaskContrast{n}", 1 ) : 1f );
			_material.Set( $"g_fTintMaskBrightness{n}", blend ? Float( $"g_fTintMaskBrightness{n}", 1 ) : 1f );

			var levels = blend ? source.GetVector( $"g_vAmbientOcclusionLevels{n}", [0, 0.5f, 1, 0] ) : [0, 0.5f, 1, 0];
			_material.Set( $"g_vAmbientOcclusionLevels{n}", MaterialParameters.AlbedoLevels( levels[0], levels[1], levels[2] ) );

			_material.Set( $"g_flMetalness{n}", !blend || source.GetInt( $"g_bMetalness{n}", 1 ) != 0 ? 1f : 0f );
			_material.Set( $"g_flModelTint{n}", !blend || source.GetInt( $"g_bModelTint{n}", 1 ) != 0 ? 1f : 0f );
			_material.Set( $"g_flVertexColorMode{n}", blend ? source.GetInt( $"g_nVertexColorMode{n}", 0 ) : 0f );
			_material.Set( $"g_flHeightMapScale{n}", Float( $"g_flHeightMapScale{n}", 1 ) );
			_material.Set( $"g_flHeightMapZeroPoint{n}", Float( $"g_flHeightMapZeroPoint{n}", 0.5f ) );
			_material.Set( $"g_flSnowLayer{n}", source.GetInt( $"g_bSnowLayer{n}", 0 ) != 0 ? 1f : 0f );

			var adjustBase = blend && source.GetInt( $"g_nColorCorrectionMode{n}", 0 ) == 1;
			_material.Set( $"g_flAdjustBaseColor{n}", adjustBase ? 1f : 0f );
			if ( adjustBase )
				SetColorAdjust( $"g_vTextureAdjust{n}_", $"g_tColor{n}", null, $"g_fTextureColorContrast{n}", $"g_fTextureColorSaturation{n}", $"g_fTextureColorBrightness{n}" );

			SetColorAdjust( $"g_vTextureColorAdjust{n}_", $"g_tColor{n}", $"g_vTextureColorTint{n}", $"g_fTextureColorContrast{n}", $"g_fTextureColorSaturation{n}", $"g_fTextureColorBrightness{n}" );
		}

		if ( !blend )
			return;

		for ( var n = 2; n <= layers; n++ )
		{
			_material.Set( $"g_flBlendSoftness{n}", Float( $"g_flBlendSoftness{n}", 0.01f ) );

			// Blend by facing direction: the direction normalized as the game's expression does, and the smoothstep range
			// from its spread and falloff (ValveResourceFormat's).
			var facing = (float)source.GetInt( $"F_BLEND_BY_FACING_DIRECTION_{n}", 0 );
			_material.Set( $"g_flFacingMode{n}", facing );
			if ( facing > 0 )
			{
				var direction = GetVector3( $"g_vFacingDirection{n}", 0 );
				if ( direction.z == 0 )
					direction.z += 0.0001f;

				var spread = Float( $"g_flFacingDirectionMaskSpread{n}", 0.5f );
				var falloff = Float( $"g_vFacingDirectionMaskFalloff{n}", 0.1f );
				_material.Set( $"g_vFacingDirection{n}", direction.Normal );
				_material.Set( $"g_vFacingDirectionMinMax{n}", new Vector2( MathF.Max( 0, 1 - spread - falloff ), MathF.Min( 1, 1 - spread + 0.001f + falloff ) ) );
			}

			_material.Set( $"g_flColorOverlay{n}", Float( $"g_flColorOverlay{n}", 0 ) );
			_material.Set( $"g_flColorReplace{n}", Float( $"g_flColorReplace{n}", 1 ) );
			_material.Set( $"g_flNormalCombine{n}", Float( $"g_flNormalCombine{n}", 0 ) );
			_material.Set( $"g_flNormalReplace{n}", Float( $"g_flNormalReplace{n}", 1 ) );
			_material.Set( $"g_flRoughnessCombine{n}", Float( $"g_flRoughnessCombine{n}", 0 ) );
			_material.Set( $"g_flRoughnessReplace{n}", Float( $"g_flRoughnessReplace{n}", 1 ) );
			_material.Set( $"g_flAOCombine{n}", Float( $"g_flAOCombine{n}", 0 ) );
			_material.Set( $"g_flAOReplace{n}", Float( $"g_flAOReplace{n}", 1 ) );
			_material.Set( $"g_flUnderlyingHeightMapInfluence{n}", Float( $"g_flUnderlyingHeightMapInfluence{n}", 1 ) );
			_material.Set( $"g_flMaskWithHeight{n}", Float( $"g_flMaskWithHeight{n}", 0 ) );

			var effects = Combo( $"F_BLEND_EFFECTS_{n}" );
			_material.Set( $"g_flBlendEffects{n}", effects ? 1f : 0f );
			if ( !effects )
				continue;

			_material.Set( $"g_flBorderBlendMode{n}", (float)source.GetInt( $"F_BORDER_BLEND_MODE_{n}", 0 ) );
			_material.Set( $"g_flBorderRoughnessEnabled{n}", Combo( $"F_BORDER_ROUGHNESS_{n}" ) ? 1f : 0f );
			_material.Set( $"g_flBorderSpread{n}", Float( $"g_flBorderSpread{n}", 0.1f ) );
			_material.Set( $"g_flBorderSoftness{n}", Float( $"g_flBorderSoftness{n}", 0.1f ) );
			_material.Set( $"g_flBorderOffset{n}", Float( $"g_flBorderOffset{n}", 0 ) );
			_material.Set( $"g_vBorderTint{n}", MaterialParameters.SrgbToLinear( GetVector3( $"g_vBorderTint{n}", 1 ) ) );
			_material.Set( $"g_vBorderLayerAmount{n}", source.GetVector( $"g_vBorderLayerAmount{n}" ) is { } amount ? new Vector2( amount[0], amount[1] ) : new Vector2( 1, 0 ) );
			_material.Set( $"g_flBorderTintMask{n}", source.GetInt( $"g_bBorderTintMask{n}", 0 ) != 0 ? 1f : 0f );
			_material.Set( $"g_fBorderRoughness{n}", Float( $"g_fBorderRoughness{n}", 0.5f ) );
			_material.Set( $"g_flBevelSpread{n}", Float( $"g_flBevelSpread{n}", 0.1f ) );
			_material.Set( $"g_flBevelSoftness{n}", Float( $"g_flBevelSoftness{n}", 0.1f ) );
			_material.Set( $"g_flBevelOffset{n}", Float( $"g_flBevelOffset{n}", 0 ) );
			_material.Set( $"g_flBevelStrength{n}", Float( $"g_flBevelStrength{n}", 0 ) * 0.01f );
			_material.Set( $"g_flBevelCurve{n}", Float( $"g_flBevelCurve{n}", 0 ) );
			_material.Set( $"g_vBevelLayerAmount{n}", source.GetVector( $"g_vBevelLayerAmount{n}" ) is { } bevel ? new Vector2( bevel[0], bevel[1] ) : new Vector2( 1, 1 ) );
		}

		var overlay = Combo( "F_SHARED_COLOR_OVERLAY" );
		_material.Set( "g_flSharedColorOverlay", overlay ? 1f : 0f );
		if ( !overlay )
			return;

		BindTexture( "g_tSharedColorOverlay", "g_tSharedColorOverlay" );

		var overlaySet = (float)source.GetInt( "g_nColorOverlayUVSet", 2 );
		_material.Set( "g_flColorOverlayUVSet", overlaySet );
		_material.Set( "g_flColorOverlayUVSetPs", overlaySet );

		var overlayScale = GetVector2( "g_vOverlayTexCoordScale", 1 );
		var (overlay0, overlay1) = MaterialParameters.TexCoordXform( overlayScale, Float( "g_flOverlayTexCoordRotation", 0 ), GetVector2( "g_vOverlayTexCoordCenter", 0.5f ), GetVector2( "g_vOverlayTexCoordOffset", 0 ) );
		_material.Set( "g_vOverlayTexCoordXform0", overlay0 );
		_material.Set( "g_vOverlayTexCoordXform1", overlay1 );
		_material.Set( "g_vOverlayTexCoordScale", overlayScale );

		// The overlay mode picks the layers it applies to (0 all), as their strengths.
		var strengths = GetVector3( "g_vColorOverlayLayerStrengths", 1 );
		var mode = source.GetInt( "g_nColorOverlayMode", 0 );
		if ( mode != 0 )
			strengths = new Vector3( mode == 1 ? strengths.x : 0, mode == 2 ? strengths.y : 0, mode == 3 ? strengths.z : 0 );

		_material.Set( "g_vColorOverlayLayerStrengths", strengths );
		_material.Set( "g_vColorOverlayTintMaskStrengths", GetVector3( "g_vColorOverlayTintMaskStrengths", 0 ) );
		_material.Set( "g_flOverlayDarknessContrast", Float( "g_flOverlayDarknessContrast", 1 ) );
		_material.Set( "g_flOverlayBrightnessContrast", Float( "g_flOverlayBrightnessContrast", 1 ) );
	}

	/// <summary>
	/// csgo_lightmappedgeneric.vfx: its static combos as the port's features and uniforms; layer 2 and the blending,
	/// the detail textures, texture transforms and specular; and its parameters with the expressions the game's shader
	/// evaluates on them.
	/// </summary>
	private void SetupLightmappedGeneric()
	{
		SetRenderStateFeatures();

		// The game's combo rules: layers aren't translucent, alpha tested or overlaid.
		var layers = Combo( "F_LAYERS" ) && !Combo( "F_TRANSLUCENT" ) && !Combo( "F_ALPHA_TEST" ) && !Combo( "F_OVERLAY" );
		var detail = Math.Clamp( source.GetInt( "F_DETAILTEXTURE" ), 0, layers ? 2 : 1 );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tLayer1NormalRoughness", "g_tLayer1NormalRoughness" );
		BindTexture( "g_tLayer1AmbientOcclusion", "g_tLayer1AmbientOcclusion" );
		_material.Set( "g_vLayer1Tint", MaterialParameters.SrgbToLinear( GetVector3( "g_vLayer1Tint", 1 ) ) );

		_material.Set( "g_flLayers", layers ? 1f : 0f );
		_material.Set( "g_flFancyBlendingMode", layers ? (float)Math.Clamp( source.GetInt( "F_FANCY_BLENDING" ), 0, 3 ) : 0f );
		if ( layers )
		{
			BindTexture( "g_tLayer2Color", "g_tLayer2Color" );
			BindTexture( "g_tLayer2NormalRoughness", "g_tLayer2NormalRoughness" );
			BindTexture( "g_tLayer2AmbientOcclusion", "g_tLayer2AmbientOcclusion" );
			BindTexture( "g_tBlendModulation", "g_tBlendModulation" );
			_material.Set( "g_vLayer2Tint", MaterialParameters.SrgbToLinear( GetVector3( "g_vLayer2Tint", 1 ) ) );

			_material.Set( "g_flBlendSoftness", Float( "g_flBlendSoftness", 0.5f ) );
			_material.Set( "g_flLayerBorderStrength", Float( "g_flLayerBorderStrength", 0.5f ) );
			_material.Set( "g_flLayerBorderOffset", -Float( "g_flLayerBorderOffset", 0 ) );
			_material.Set( "g_flLayerBorderSoftness", Float( "g_flLayerBorderSoftness", 0.5f ) );
			_material.Set( "g_vLayerBorderTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vLayerBorderTint", 1 ) ) );
			_material.Set( "g_flBevelBlendWidth", Float( "g_flBevelBlendWidth", 4 ) );
			_material.Set( "g_flBevelBlendSharpness", Float( "g_flBevelBlendSharpness", 2 ) );
			_material.Set( "g_flBevelBlendStrength", Float( "g_flBevelBlendStrength", 0 ) );
		}

		_material.Set( "g_flDetailTexture", (float)detail );
		for ( var n = 1; n <= detail; n++ )
		{
			BindTexture( $"g_tLayer{n}Detail", $"g_tLayer{n}Detail" );
			_material.Set( $"g_vLayer{n}DetailScale", GetVector2( $"g_vLayer{n}DetailScale", 4 ) );
			_material.Set( $"g_vLayer{n}DetailTintAndBlend", source.GetVector( $"g_vLayer{n}DetailTintAndBlend" ) is { } tint ? new Vector4( tint[0], tint[1], tint[2], tint[3] ) : Vector4.One );
		}

		// Without F_TEXTURETRANSFORMS the game ignores the transform parameters a material may still have.
		if ( Combo( "F_TEXTURETRANSFORMS" ) )
		{
			SetLayerTexCoordXform( "g_vLayer1TexCoord", "Layer1TexCoord" );
			SetLayerTexCoordXform( "g_vLayer1NormalTexCoord", "Layer1NormalTexCoord" );
			if ( layers )
			{
				SetLayerTexCoordXform( "g_vLayer2TexCoord", "Layer2TexCoord" );
				SetLayerTexCoordXform( "g_vLayer2NormalTexCoord", "Layer2NormalTexCoord" );
				SetLayerTexCoordXform( "g_vBlendModulateTexCoord", "BlendModulateTexCoord" );
			}
		}

		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		_material.Set( "g_flVertexColorOpacityScale", Float( "g_flVertexColorOpacityScale", 1 ) );

		_material.Set( "g_flMetalness", Float( "g_flMetalness", 0 ) );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );
		_material.Set( "g_flNoSpecularAtFullRoughness", Combo( "F_NO_SPECULAR_AT_FULL_ROUGHNESS" ) ? 1f : 0f );

		// The game's combo rules: direct specular only comes with indirect.
		var specularIndirect = Combo( "F_SPECULAR_INDIRECT" );
		_material.Set( "g_flSpecularIndirect", specularIndirect ? 1f : 0f );
		_material.Set( "g_flSpecularDirect", specularIndirect && Combo( "F_SPECULAR_DIRECT" ) ? 1f : 0f );

		// Overlays are translucent or alpha tested, never layered; their alpha test reference defaults lower.
		var overlay = Combo( "F_OVERLAY" ) && (Combo( "F_TRANSLUCENT" ) || Combo( "F_ALPHA_TEST" ));
		_material.Set( "g_flOverlay", overlay ? 1f : 0f );
		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", Combo( "F_ALPHA_TEST" ) ? 0.5f : 0.01f ) );
		_material.Set( "g_flAntiAliasedEdgeStrength", Float( "g_flAntiAliasedEdgeStrength", 1 ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );

		_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
	}

	/// <summary>
	/// csgo_foliage.vfx: alpha testing and two sided rendering as the port's features, the tint mask, colour adjustment,
	/// the painted vertex colour's ambient occlusion and height gradient, and transmission.
	/// </summary>
	private void SetupFoliage()
	{
		SetFeature( "F_ALPHA_TEST", Combo( "F_ALPHA_TEST" ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );
		SetFeature( "F_DO_NOT_CAST_SHADOWS", Combo( "F_DO_NOT_CAST_SHADOWS" ) );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormal", "g_tNormal" );
		BindTexture( "g_tAmbientOcclusion", "g_tAmbientOcclusion" );

		var tintMask = Combo( "F_TINT_MASK" );
		if ( tintMask )
			BindTexture( "g_tTintMask", "g_tTintMask" );
		_material.Set( "g_flTintMask", tintMask ? 1f : 0f );

		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );

		// The game's texture coordinate transform here is a constant identity; only the scroll applies.
		_material.Set( "g_vTexCoordScrollSpeed", GetVector2( "g_vTexCoordScrollSpeed", 0 ) );

		SetColorAdjust( "g_vTextureColorAdjust", "g_tColor", "g_vTextureColorCorrectionTint", "g_fTextureColorContrast", "g_fTextureColorSaturation", "g_fTextureColorBrightness" );

		_material.Set( "g_fTextureNormalContrast", Float( "g_fTextureNormalContrast", 1 ) );
		_material.Set( "g_fTextureRoughnessContrast", Float( "g_fTextureRoughnessContrast", 1 ) );
		_material.Set( "g_fTextureRoughnessBrightness", Float( "g_fTextureRoughnessBrightness", 1 ) );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );

		_material.Set( "g_flVertexAmbientOcclusionAmount", Float( "g_flVertexAmbientOcclusionAmount", 0 ) );
		_material.Set( "g_flVertexAmbientOcclusionPower", Float( "g_flVertexAmbientOcclusionPower", 1 ) );
		_material.Set( "g_flHeightGradientAmount", Float( "g_flHeightGradientAmount", 0 ) );
		_material.Set( "g_flHeightGradientPower", Float( "g_flHeightGradientPower", 1 ) );

		// The game's feature rules: albedo for transmission and its shadows' toggle only apply with transmission.
		var transmissive = Combo( "F_TRANSMISSIVE_BACKFACE_NDOTL" );
		var useAlbedo = transmissive && Combo( "F_USE_ALBEDO_FOR_TRANSMISSIVE" );
		var colorCorrection = source.GetInt( "g_bApplyColorCorrectionToTransmissiveColor", 0 ) != 0;
		_material.Set( "g_flTransmissive", transmissive ? 1f : 0f );
		_material.Set( "g_flUseAlbedoForTransmissive", useAlbedo ? 1f : 0f );
		_material.Set( "g_flTransmissiveColorCorrection", colorCorrection ? 1f : 0f );
		_material.Set( "g_flTransmissiveShadows", transmissive && Combo( "F_DISABLE_TRANSMISSIVE_SHADOWS" ) ? 0f : 1f );
		if ( transmissive && !useAlbedo )
		{
			BindTexture( "g_tTransmissiveColor", "g_tTransmissiveColor" );
			if ( colorCorrection )
				SetColorAdjust( "g_vTransmissiveColorAdjust", "g_tTransmissiveColor", "g_vTextureColorCorrectionTint", "g_fTextureColorContrast", "g_fTextureColorSaturation", "g_fTextureColorBrightness" );
		}

		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flGrazingAngleMask", Float( "g_flGrazingAngleMask", 0 ) );
		_material.Set( "g_flAlphaBoostStrength", Float( "g_flAlphaBoostStrength", 3 ) );
		_material.Set( "g_flAlphaBoostDistance", Float( "g_flAlphaBoostDistance", 20000 ) );

		_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
	}

	/// <summary>
	/// csgo_complex.vfx: its render state combos as the port's features, and as uniforms its tint mask, metalness source,
	/// second coordinates per texture, detail mode, decal, self illumination, transmission, vertex paint and depth bias.
	/// csgo_simple.vfx is the same shader cut down to metalness, vertex paint and two sided rendering (decompiled, its
	/// variants match complex's): the other F_* values its materials carry aren't features of it, so they're ignored.
	/// </summary>
	private void SetupComplex( bool simple )
	{
		bool Feature( string feature ) => (!simple || feature is "F_METALNESS_TEXTURE" or "F_PAINT_VERTEX_COLORS" or "F_RENDER_BACKFACES"
			or "F_DONT_FLIP_BACKFACE_NORMALS" or "F_DO_NOT_CAST_SHADOWS") && Combo( feature );

		var translucent = Feature( "F_TRANSLUCENT" );
		SetFeature( "F_TRANSLUCENT", translucent );
		SetFeature( "F_ALPHA_TEST", Feature( "F_ALPHA_TEST" ) && !translucent );
		SetFeature( "F_ADDITIVE_BLEND", translucent && Feature( "F_ADDITIVE_BLEND" ) );
		SetFeature( "F_RENDER_BACKFACES", Feature( "F_RENDER_BACKFACES" ) );
		SetFeature( "F_DO_NOT_CAST_SHADOWS", Feature( "F_DO_NOT_CAST_SHADOWS" ) );

		var alphaTest = Feature( "F_ALPHA_TEST" ) && !Feature( "F_TRANSLUCENT" );
		var opacityInAlpha = alphaTest || Feature( "F_TRANSLUCENT" );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormal", "g_tNormal" );
		BindTexture( "g_tAmbientOcclusion", "g_tAmbientOcclusion" );

		var tintMask = Feature( "F_TINT_MASK" );
		if ( tintMask )
			BindTexture( "g_tTintMask", "g_tTintMask" );
		_material.Set( "g_flTintMask", tintMask ? 1f : 0f );

		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		_material.Set( "g_flPaintVertexColors", Feature( "F_PAINT_VERTEX_COLORS" ) ? 1f : 0f );
		_material.Set( "g_flDepthBias", Feature( "F_DEPTH_BIAS" ) ? 1f : 0f );

		var (row0, row1) = MaterialParameters.TexCoordXform( GetVector2( "g_vTexCoordScale", 1 ), Float( "g_flTexCoordRotation", 0 ), GetVector2( "g_vTexCoordCenter", 0.5f ), GetVector2( "g_vTexCoordOffset", 0 ) );
		_material.Set( "g_vTexCoordXform0", row0 );
		_material.Set( "g_vTexCoordXform1", row1 );
		_material.Set( "g_vTexCoordScrollSpeed", GetVector2( "g_vTexCoordScrollSpeed", 0 ) );

		// F_METALNESS_TEXTURE puts metalness in the colour's alpha, or in its own texture when the alpha is opacity.
		var metalnessSource = !Feature( "F_METALNESS_TEXTURE" ) ? 0f : opacityInAlpha ? 2f : 1f;
		if ( metalnessSource == 2f )
			BindTexture( "g_tMetalness", "g_tMetalness" );
		_material.Set( "g_flMetalnessSource", metalnessSource );
		_material.Set( "g_flMetalness", Float( "g_flMetalness", 0 ) );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );

		// Each texture's choice of second coordinates only applies with F_SECONDARY_UV.
		var secondary = Feature( "F_SECONDARY_UV" );
		float Secondary( string parameter, int fallback ) => secondary && source.GetInt( parameter, fallback ) != 0 ? 1f : 0f;
		_material.Set( "g_flSecondaryUvForTintMask", Secondary( "g_bUseSecondaryUvForTintMask", 0 ) );
		_material.Set( "g_flSecondaryUvForAmbientOcclusion", Secondary( "g_bUseSecondaryUvForAmbientOcclusion", 1 ) );
		_material.Set( "g_flSecondaryUvForDetailMask", Secondary( "g_bUseSecondaryUvForDetailMask", 1 ) );
		_material.Set( "g_flSecondaryUvForDetail", Secondary( "g_bUseSecondaryUvForDetailTexture", 1 ) );
		_material.Set( "g_flSecondaryUvForDecal", Secondary( "g_bUseSecondaryUvForDecal", 1 ) );
		_material.Set( "g_flSecondaryUvForSelfIllum", Secondary( "g_bUseSecondaryUvForSelfIllum", 0 ) );

		var detail = simple ? 0 : Math.Clamp( source.GetInt( "F_DETAIL_TEXTURE" ), 0, 4 );
		_material.Set( "g_flDetailTexture", (float)detail );
		if ( detail > 0 )
		{
			BindTexture( "g_tDetailMask", "g_tDetailMask" );
			if ( detail != 3 )
				BindTexture( "g_tDetail", "g_tDetail" );
			if ( detail >= 3 )
				BindTexture( "g_tNormalDetail", "g_tNormalDetail" );

			_material.Set( "g_flDetailBlendFactor", Float( "g_flDetailBlendFactor", 1 ) );
			_material.Set( "g_flDetailBlendToFull", Float( "g_flDetailBlendToFull", 0 ) );

			var strength = Float( "g_flDetailNormalStrength", 1 );
			_material.Set( "g_vDetailNormalStrengthOffsetScale", new Vector4( 0, 0, 1 - strength, strength ) );

			var (xform, offset) = MaterialParameters.DetailTexCoordXform( GetVector2( "g_vDetailTexCoordScale", 1 ), Float( "g_flDetailTexCoordRotation", 0 ), GetVector2( "g_vDetailTexCoordOffset", 0 ) );
			_material.Set( "g_vDetailTexCoordXform", xform );
			_material.Set( "g_vDetailTexCoordOffset", offset );
		}

		var decal = Feature( "F_DECAL_TEXTURE" );
		_material.Set( "g_flDecalTexture", decal ? 1f : 0f );
		_material.Set( "g_flDecalBlendMode", Feature( "F_DECAL_BLEND_MODE" ) ? 1f : 0f );
		if ( decal )
			BindTexture( "g_tDecal", "g_tDecal" );

		if ( Feature( "F_SELF_ILLUM" ) )
		{
			BindTexture( "g_tSelfIllumMask", "g_tSelfIllumMask" );
			_material.Set( "g_vSelfIllumTintScaled", MaterialParameters.SelfIllumTintScaled( Float( "g_flSelfIllumBrightness", 0 ), Float( "g_flSelfIllumScale", 1 ), GetVector3( "g_vSelfIllumTint", 1 ) ) );
			_material.Set( "g_vSelfIllumScrollSpeed", GetVector2( "g_vSelfIllumScrollSpeed", 0 ) );
			_material.Set( "g_flSelfIllumAlbedoFactor", Float( "g_flSelfIllumAlbedoFactor", 1 ) );
		}

		// The game's feature rules: albedo for transmission and its shadows' toggle only apply with transmission.
		var transmissive = Feature( "F_TRANSMISSIVE_BACKFACE_NDOTL" );
		var useAlbedo = transmissive && Feature( "F_USE_ALBEDO_FOR_TRANSMISSIVE" );
		_material.Set( "g_flTransmissive", transmissive ? 1f : 0f );
		_material.Set( "g_flUseAlbedoForTransmissive", useAlbedo ? 1f : 0f );
		_material.Set( "g_flTransmissiveShadows", transmissive && Feature( "F_DISABLE_TRANSMISSIVE_SHADOWS" ) ? 0f : 1f );
		if ( transmissive && !useAlbedo )
			BindTexture( "g_tTransmissiveColor", "g_tTransmissiveColor" );

		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flAntiAliasedEdgeStrength", Float( "g_flAntiAliasedEdgeStrength", 1 ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );

		_material.Set( "g_flFlipBackfaceNormals", Feature( "F_RENDER_BACKFACES" ) && !Feature( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
	}

	/// <summary>
	/// csgo_character.vfx: render state and the eyeballs as the port's features; the tint mask, anisotropic gloss
	/// (spherically projected or not), cloth, skin, patches, eyeballs and translucent distance contrast as uniforms.
	/// </summary>
	private void SetupCharacter()
	{
		SetRenderStateFeatures();

		// The game's combo rules: eyeballs aren't translucent, alpha tested or patched.
		var translucent = Combo( "F_TRANSLUCENT" );
		var eyes = Combo( "F_EYEBALLS" ) && !translucent && !Combo( "F_ALPHA_TEST" ) && !Combo( "F_PATCHES" );
		SetFeature( "F_EYEBALLS", eyes );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormal", "g_tNormal" );
		BindTexture( "g_tAmbientOcclusion", "g_tAmbientOcclusion" );
		BindTexture( "g_tMetalness", "g_tMetalness" );

		var tintMask = Combo( "F_TINT_MASK" );
		if ( tintMask )
			BindTexture( "g_tTintMask", "g_tTintMask" );
		_material.Set( "g_flTintMask", tintMask ? 1f : 0f );
		_material.Set( "g_flSecondaryUvForTintMask", tintMask && source.GetInt( "g_bUseSecondaryUvForTintMask", 0 ) != 0 ? 1f : 0f );

		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		var (row0, row1) = MaterialParameters.TexCoordXform( GetVector2( "g_vTexCoordScale", 1 ), Float( "g_flTexCoordRotation", 0 ), GetVector2( "g_vTexCoordCenter", 0.5f ), GetVector2( "g_vTexCoordOffset", 0 ) );
		_material.Set( "g_vTexCoordXform0", row0 );
		_material.Set( "g_vTexCoordXform1", row1 );
		_material.Set( "g_vTexCoordScrollSpeed", GetVector2( "g_vTexCoordScrollSpeed", 0 ) );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );

		// The game's combo rules: spherical projection is a child of anisotropic gloss.
		var anisotropic = Combo( "F_ANISOTROPIC_GLOSS" );
		var spherical = anisotropic && Combo( "F_SPHERICAL_PROJECTED_ANISOTROPIC_TANGENTS" );
		if ( anisotropic )
			BindTexture( "g_tAnisoGloss", "g_tAnisoGloss" );
		_material.Set( "g_flAnisotropicGloss", anisotropic ? 1f : 0f );
		_material.Set( "g_flSphericalAnisotropy", spherical ? 1f : 0f );
		_material.Set( "g_flSphericalAnisotropyPs", spherical ? 1f : 0f );
		_material.Set( "g_vSphericalAnisotropyPole", GetVector3( "g_vSphericalAnisotropyPole", 0 ) );
		_material.Set( "g_vSphericalAnisotropyAngle", Float( "g_vSphericalAnisotropyAngle", 0 ) / 90f - 1f );

		_material.Set( "g_flClothShading", Combo( "F_CLOTH_SHADING" ) ? 1f : 0f );
		_material.Set( "g_flSheenTintColor", MaterialParameters.SrgbToLinear( GetVector3( "g_flSheenTintColor", 1 ) ) );
		_material.Set( "g_flSheenScale", Float( "g_flSheenScale", 0.667f ) );
		_material.Set( "g_flAmbientOcclusionMasking", Float( "g_flAmbientOcclusionMasking", 0.4f ) );

		var skin = Combo( "F_SUBSURFACE_SCATTERING" );
		_material.Set( "g_flSubsurfaceScattering", skin ? 1f : 0f );
		if ( skin )
		{
			BindTexture( "g_tSssMask", "g_tSssMask" );
			BindTexture( "g_tDiffuseFalloff", "g_tDiffuseFalloff" );
			_material.Set( "g_vNormalSoftness", source.GetVector( "g_vNormalSoftness" ) is { } softness ? new Vector3( softness[0], softness[1], softness[2] ) : new Vector3( 1, 0.3f, 0 ) );
			_material.Set( "g_flCurvatureScale", Float( "g_flCurvatureScale", 1 ) );
		}

		// Patches: each one's switch, then scale, rotation, squash, backing scale, and offset.
		var patches = Combo( "F_PATCHES" ) && !eyes;
		var enabled = new float[3];
		var offsets = new Vector2[3];
		for ( var n = 0; n < 3; n++ )
		{
			var on = patches && source.GetInt( $"g_bEnablePatch{n}", 0 ) != 0;
			enabled[n] = on ? 1f : 0f;
			offsets[n] = GetVector2( $"g_vPatch{n}Offset", 0 );
			_material.Set( $"g_vPatch{n}", new Vector4( Float( $"g_flPatch{n}Scale", 0 ), Float( $"g_flPatch{n}Rotation", 0 ), Float( $"g_flPatch{n}Squash", 1 ), Float( $"g_flPatch{n}BackingScale", 0 ) ) );
			if ( on )
			{
				BindTexture( $"g_tPatch{n}", $"g_tPatch{n}" );
				BindTexture( $"g_tPatch{n}Backing", $"g_tPatch{n}Backing" );
			}
		}

		_material.Set( "g_vPatchEnable", new Vector4( enabled[0], enabled[1], enabled[2], 0 ) );
		_material.Set( "g_vPatchOffset01", new Vector4( offsets[0].x, offsets[0].y, offsets[1].x, offsets[1].y ) );
		_material.Set( "g_vPatchOffset2", offsets[2] );

		if ( eyes )
		{
			BindTexture( "g_tEyeAlbedo1", "g_tEyeAlbedo1" );
			BindTexture( "g_tEyeMask1", "g_tEyeMask1" );
			var iris = Float( "g_flEyeIrisSize1", 1 );
			_material.Set( "g_flEyeBallRadius1", Float( "g_flEyeBallRadius1", 0.6f ) );
			_material.Set( "g_flEyeIrisSize1", iris );
			_material.Set( "g_flEyePupilSize1", Float( "g_flEyePupilSize1", 0.15f ) * iris );
			_material.Set( "g_flEyeHueShift1", Float( "g_flEyeHueShift1", 0 ) );
			_material.Set( "g_flEyeSaturation1", Float( "g_flEyeSaturation1", 1 ) );
			_material.Set( "g_flEyeBallWalleyeL1", Float( "g_flEyeBallWalleyeL1", 0 ) );
			_material.Set( "g_flEyeBallWalleyeR1", Float( "g_flEyeBallWalleyeR1", 0 ) );
			_material.Set( "g_flPointAtEyeTarget1", source.GetInt( "g_bPointAtEyeTarget1", 1 ) != 0 ? 1f : 0f );
		}

		// g_fDistanceContrastExposure's expression: as set when translucent, else one less it (the opaque boost isn't
		// ported).
		var exposure = Float( "g_fDistanceContrastExposure", 0.6f );
		_material.Set( "g_flTranslucentDistanceContrast", translucent && Combo( "F_DISTANCE_CONTRAST_ADJUSTMENT" ) ? 1f : 0f );
		_material.Set( "g_fDistanceContrastExposure", translucent ? exposure : 1 - exposure );

		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flAntiAliasedEdgeStrength", Float( "g_flAntiAliasedEdgeStrength", 1 ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );
		_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
	}

	/// <summary>
	/// csgo_weapon.vfx: render state as the port's features; the tint mask, metalness remap and transition, pearlescence
	/// and self illumination as uniforms. Stickers (the paint kit phase) aren't read.
	/// </summary>
	private void SetupWeapon()
	{
		SetRenderStateFeatures();

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormal", "g_tNormal" );
		BindTexture( "g_tAmbientOcclusion", "g_tAmbientOcclusion" );
		BindTexture( "g_tMetalness", "g_tMetalness" );

		var tintMask = Combo( "F_TINT_MASK" );
		if ( tintMask )
			BindTexture( "g_tTintMask", "g_tTintMask" );
		_material.Set( "g_flTintMask", tintMask ? 1f : 0f );

		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		var (row0, row1) = MaterialParameters.TexCoordXform( GetVector2( "g_vTexCoordScale", 1 ), Float( "g_flTexCoordRotation", 0 ), GetVector2( "g_vTexCoordCenter", 0.5f ), GetVector2( "g_vTexCoordOffset", 0 ) );
		_material.Set( "g_vTexCoordXform0", row0 );
		_material.Set( "g_vTexCoordXform1", row1 );
		_material.Set( "g_vTexCoordScrollSpeed", GetVector2( "g_vTexCoordScrollSpeed", 0 ) );

		_material.Set( "g_vMetalnessRemapRange", GetVector2( "g_vMetalnessRemapRange", 0 ) );
		_material.Set( "g_flMetalnessTransitionBias", Float( "g_flMetalnessTransitionBias", 2 ) );
		_material.Set( "g_flPearlescentScale", Float( "g_flPearlescentScale", 0 ) );
		_material.Set( "g_flZBias", Float( "g_flZBias", 0 ) );
		_material.Set( "g_flStatTrak", Combo( "F_STATTRAK" ) ? 1f : 0f );

		if ( Combo( "F_SELF_ILLUM" ) )
		{
			BindTexture( "g_tSelfIllumMask", "g_tSelfIllumMask" );
			_material.Set( "g_vSelfIllumTintScaled", MaterialParameters.SelfIllumTintScaled( Float( "g_flSelfIllumBrightness", 0 ), Float( "g_flSelfIllumScale", 1 ), GetVector3( "g_vSelfIllumTint", 1 ) ) );
			_material.Set( "g_vSelfIllumScrollSpeed", GetVector2( "g_vSelfIllumScrollSpeed", 0 ) );
			_material.Set( "g_flSelfIllumAlbedoFactor", Float( "g_flSelfIllumAlbedoFactor", 1 ) );
		}

		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flAntiAliasedEdgeStrength", Float( "g_flAntiAliasedEdgeStrength", 1 ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );
		_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
	}

	/// <summary>
	/// Source 2's core generic.vfx: render state as the port's features, unlit and self illumination as uniforms, its
	/// coordinate scales, bump strength, and the roughness range as its expression scales and biases it. Its colour and
	/// self illumination tints have no linearizing expression; they're used as stored.
	/// </summary>
	private void SetupGeneric()
	{
		var translucent = Combo( "F_TRANSLUCENT" );
		SetFeature( "F_TRANSLUCENT", translucent );
		SetFeature( "F_ALPHA_TEST", Combo( "F_ALPHA_TEST" ) && !translucent );
		SetFeature( "F_ADDITIVE_BLEND", translucent && Combo( "F_ADDITIVE_BLEND" ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		BindTexture( "g_tColor", "g_tColor" );
		BindTexture( "g_tNormal", "g_tNormal" );
		BindTexture( "g_tRoughness", "g_tRoughness" );

		_material.Set( "g_vColorTint", GetVector3( "g_vColorTint", 1 ) );
		_material.Set( "g_vTexCoordScale", GetVector2( "g_vTexCoordScale", 1 ) );
		_material.Set( "g_vTexCoordOffset", GetVector2( "g_vTexCoordOffset", 0 ) );
		_material.Set( "g_vNormalTexCoordScale", GetVector2( "g_vNormalTexCoordScale", 1 ) );
		_material.Set( "g_flBumpStrength", Float( "g_flBumpStrength", 1 ) );

		// The game's combo rules: self illumination is lit only.
		var unlit = Combo( "F_UNLIT" );
		_material.Set( "g_flUnlit", unlit ? 1f : 0f );

		var gloss = GetVector2( "g_vGlossinessRange", 0 );
		_material.Set( "g_vRoughnessScaleAndBias", new Vector2( MathF.Max( 1 - gloss.x, 1 - gloss.y ) - (1 - gloss.y), 1 - gloss.y ) );

		var selfIllum = Combo( "F_SELF_ILLUM" ) && !unlit;
		_material.Set( "g_flSelfIllum", selfIllum ? 1f : 0f );
		if ( selfIllum )
		{
			BindTexture( "g_tSelfIllumMask", "g_tSelfIllumMask" );
			_material.Set( "g_vSelfIllumTintScaled", GetVector3( "g_vSelfIllumTint", 1 ) * Float( "g_flSelfIllumScale", 1 ) );
		}

		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );
	}

	/// <summary>
	/// csgo_unlitgeneric.vfx: its blend mode as the port's feature; the second texture, vertex colour and tints as
	/// uniforms.
	/// </summary>
	private void SetupUnlitGeneric()
	{
		_material.SetFeature( "F_BLEND_MODE", (int)Math.Clamp( source.GetInt( "F_BLEND_MODE" ), 0, 6 ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		BindTexture( "g_tColor", "g_tColor" );
		SetTexCoordXform();
		_material.Set( "g_vTexCoordScrollSpeed", GetVector2( "g_vTexCoordScrollSpeed", 0 ) );

		var two = Combo( "F_TWOTEXTURE" );
		_material.Set( "g_flTwoTexture", two ? 1f : 0f );
		_material.Set( "g_flTwoTexturePs", two ? 1f : 0f );
		if ( two )
		{
			BindTexture( "g_tColor2", "g_tColor2" );

			// Its expression scales the center, with 3.1415927 for pi.
			var scale = GetVector2( "g_vTex2CoordScale", 1 );
			var center = GetVector2( "g_vTex2CoordCenter", 0.5f );
			var offset = GetVector2( "g_vTex2CoordOffset", 0 );
			var angle = Float( "g_flTex2CoordRotation", 0 ) * 3.1415927f / 180f;
			var cos = MathF.Cos( angle );
			var sin = MathF.Sin( angle );
			_material.Set( "g_vTex2CoordXform0", new Vector4( cos * scale.x, -sin * scale.y, 0, center.x + offset.x + sin * scale.y * center.y - cos * scale.x * center.x ) );
			_material.Set( "g_vTex2CoordXform1", new Vector4( sin * scale.x, cos * scale.y, 0, center.y + offset.y + sin * scale.x * center.x - cos * scale.y * center.y ) );
		}

		_material.Set( "g_flVertexColor", Combo( "F_VERTEX_COLOR" ) ? 1f : 0f );
		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
	}

	/// <summary>
	/// csgo_simple_2way_blend.vfx: two materials blended by a mask and the painted blend value; its static combos as
	/// uniforms.
	/// </summary>
	private void SetupSimple2WayBlend()
	{
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		BindTexture( "g_tColorA", "g_tColorA" );
		BindTexture( "g_tNormalA", "g_tNormalA" );
		BindTexture( "g_tColorB", "g_tColorB" );
		BindTexture( "g_tNormalB", "g_tNormalB" );
		BindTexture( "g_tMask", "g_tMask" );

		var tintMasks = Combo( "F_ENABLE_TINT_MASKS" );
		if ( tintMasks )
			BindTexture( "g_tTintMask", "g_tTintMask" );

		_material.Set( "g_flTintMasks", tintMasks ? 1f : 0f );
		_material.Set( "g_flAmbientOcclusion", Combo( "F_ENABLE_AMBIENT_OCCLUSION" ) ? 1f : 0f );
		_material.Set( "g_flPaintVertexColors", Combo( "F_PAINT_VERTEX_COLORS" ) ? 1f : 0f );

		SetTexCoordXform();
		_material.Set( "g_vTexCoordScrollSpeed", GetVector2( "g_vTexCoordScrollSpeed", 0 ) );
		_material.Set( "g_vTexCoordScale2", GetVector2( "g_vTexCoordScale2", 1 ) );

		_material.Set( "g_vColorTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vColorTint", 1 ) ) );
		_material.Set( "g_flModelTintAmount", Float( "g_flModelTintAmount", 1 ) );
		_material.Set( "g_flMetalnessA", Float( "g_flMetalnessA", 0 ) );
		_material.Set( "g_flMetalnessB", Float( "g_flMetalnessB", 0 ) );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
	}

	/// <summary>
	/// csgo_water_fancy.vfx: its textures, its static combos as uniforms, and its parameters (the colours its expressions
	/// linearize, linearized; the roughness its expression makes from the glossiness).
	/// </summary>
	private void SetupWaterFancy()
	{
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );

		BindTexture( "g_tWavesNormalHeight", "g_tWavesNormalHeight" );
		BindTexture( "g_tFoam", "g_tFoam" );
		BindTexture( "g_tDebris", "g_tDebris" );
		BindTexture( "g_tDebrisNormal", "g_tDebrisNormal" );
		_material.Set( "g_flHasLowEndCube", BindTexture( "g_tLowEndCubeMap", "g_tLowEndCubeMap" ) ? 1f : 0f );

		_material.Set( "g_flRefraction", Combo( "F_REFRACTION" ) ? 1f : 0f );
		_material.Set( "g_flCaustics", Combo( "F_CAUSTICS" ) ? 1f : 0f );
		_material.Set( "g_flFlipBackfaceNormals", Combo( "F_RENDER_BACKFACES" ) && !Combo( "F_DONT_FLIP_BACKFACE_NORMALS" ) ? 1f : 0f );
		_material.Set( "g_flFogEnabled", source.GetInt( "g_bFogEnabled", 1 ) != 0 ? 1f : 0f );
		_material.Set( "g_flTriplanarCaustics", source.GetInt( "g_bUseTriplanarCaustics", 0 ) != 0 ? 1f : 0f );
		_material.Set( "g_flWaveIterations", (float)source.GetInt( "g_nWaveIterations", 3 ) );

		var roughness = Math.Max( 0.01f, 1 - Float( "g_flGlossiness", 0.75f ) );
		_material.Set( "g_vRoughness", new Vector2( roughness, roughness ) );

		_material.Set( "g_vDebrisTint", MaterialParameters.SrgbToLinear( GetVector3( "g_vDebrisTint", 0.7f ) ) );
		_material.Set( "g_vFoamColor", MaterialParameters.SrgbToLinear( GetVector3( "g_vFoamColor", 0.7f ) ) );
		_material.Set( "g_vWaterFogColor", MaterialParameters.SrgbToLinear( GetVector3( "g_vWaterFogColor", 0.5f ) ) );
		_material.Set( "g_vWaterDecayColor", GetVector3( "g_vWaterDecayColor", 1 ) );
		_material.Set( "g_vCausticsTint", GetVector3( "g_vCausticsTint", 0.5f ) );
		_material.Set( "g_vWaveScale", GetVector2( "g_vWaveScale", 15 ) );

		var mapMin = source.GetVector( "g_vMapUVMin", [-10272, -10199, 0, 0] );
		var mapMax = source.GetVector( "g_vMapUVMax", [10241, 10241, 0, 0] );
		_material.Set( "g_vMapUVMin", new Vector2( mapMin[0], mapMin[1] ) );
		_material.Set( "g_vMapUVMax", new Vector2( mapMax[0], mapMax[1] ) );

		foreach ( var (parameter, fallback) in WaterFloats )
			_material.Set( parameter, Float( parameter, fallback ) );
	}

	/// <summary>
	/// csgo_water_fancy's float parameters and the game's defaults for them.
	/// </summary>
	private static readonly (string Parameter, float Fallback)[] WaterFloats =
	[
		("g_flWaterPlaneOffset", 0), ("g_flSkyBoxFadeRange", 0.05f), ("g_flLowEndCubeMapIntensity", 1),
		("g_flWaterRoughnessMin", 0.25f), ("g_flWaterRoughnessMax", 1), ("g_flFoamMin", 0), ("g_flFoamMax", 1),
		("g_flDebrisMin", 0), ("g_flDebrisMax", 1), ("g_flDebrisReflectance", 0.1f), ("g_flDebrisOilyness", 0.1f),
		("g_flDebrisNormalStrength", 1), ("g_flDebrisEdgeSharpness", 10), ("g_flDebrisScale", 1), ("g_flDebrisWobble", 1),
		("g_flFoamScale", 1), ("g_flFoamWobble", 1), ("g_flWavesHeightOffset", 1), ("g_flWavesSharpness", 0.5f),
		("g_flFresnelExponent", 5), ("g_flWavesNormalStrength", 1), ("g_flWavesNormalJitter", 0.1f),
		("g_flWaterInitialDirection", 1.5f), ("g_flWavesSpeed", 1), ("g_flLowFreqWeight", 0.5f), ("g_flMedFreqWeight", 0.5f),
		("g_flHighFreqWeight", 0.5f), ("g_flWavesPhaseOffset", 0.4f), ("g_flEdgeHardness", 10), ("g_flEdgeShapeEffect", 1),
		("g_flRefractionLimit", 0.1f), ("g_flWaterFogStrength", 0.5f), ("g_flWaterDecayStrength", 8), ("g_flWaterMaxDepth", 100),
		("g_flWaterFogShadowStrength", 0.5f), ("g_flUnderwaterDarkening", 0.6f), ("g_flSpecularPower", 300),
		("g_flSpecularNormalMultiple", 2), ("g_flSpecularBloomBoostStrength", 100), ("g_flSpecularBloomBoostThreshold", 0.7f),
		("g_flCausticUVScaleMultiple", 2.5f), ("g_flCausticDistortion", 0.5f), ("g_flCausticsStrength", 40),
		("g_flCausticSharpness", 0.5f), ("g_flCausticDepthFallOffDistance", 100), ("g_flCausticShadowCutOff", 0.2f),
		("g_flReflectance", 0.2f), ("g_flReflectionDistanceEffect", 0.5f), ("g_flEnvironmentMapBrightness", 1),
	];

	/// <summary>
	/// The colour texture coordinate transform (g_vTexCoordXform0/1) alone.
	/// </summary>
	private void SetTexCoordXform()
	{
		var (row0, row1) = MaterialParameters.TexCoordXform( GetVector2( "g_vTexCoordScale", 1 ), Float( "g_flTexCoordRotation", 0 ), GetVector2( "g_vTexCoordCenter", 0.5f ), GetVector2( "g_vTexCoordOffset", 0 ) );
		_material.Set( "g_vTexCoordXform0", row0 );
		_material.Set( "g_vTexCoordXform1", row1 );
	}

	/// <summary>
	/// One of csgo_lightmappedgeneric's texture coordinate transforms, {uniform}Xform0 and 1, from the material's
	/// g_v{name}Scale, g_fl{name}Rotation, g_v{name}Center and g_v{name}Offset. All of them use the expression that
	/// scales the center, as <see cref="MaterialParameters.NormalTexCoordXform"/> evaluates it.
	/// </summary>
	private void SetLayerTexCoordXform( string uniform, string name )
	{
		var (row0, row1) = MaterialParameters.NormalTexCoordXform(
			GetVector2( $"g_v{name}Scale", 1 ), Float( $"g_fl{name}Rotation", 0 ), GetVector2( $"g_v{name}Center", 0.5f ), GetVector2( $"g_v{name}Offset", 0 ) );

		_material.Set( $"{uniform}Xform0", row0 );
		_material.Set( $"{uniform}Xform1", row1 );
	}

	/// <summary>
	/// A colour adjust matrix as the game's expression builds it, MatrixMultiply( MatrixColorTint2( tint, 1 ),
	/// MatrixColorCorrect2( contrast, saturation, brightness, TextureAverageColor( texture ) ) ), as three rows.
	/// </summary>
	private void SetColorAdjust( string uniform, string texture, string tint, string contrast, string saturation, string brightness )
	{
		var average = new System.Numerics.Vector3( 1 );
		if ( source.GetTexture( texture ) is { Length: > 0 } path )
		{
			try
			{
				var reflectivity = mount.ReadTextureHeader( CompiledWorld.NormalizePath( path ) ).Reflectivity;
				average = new System.Numerics.Vector3( reflectivity[0], reflectivity[1], reflectivity[2] );
			}
			catch ( Exception e ) when ( e is System.IO.IOException or System.IO.InvalidDataException or NotSupportedException )
			{
				// Without its texture the pivot is white, as the game's default texture would give.
			}
		}

		var tintColor = tint is null ? Vector3.One : GetVector3( tint, 1 );
		var correct = ColorMatrices.ColorCorrect( new System.Numerics.Vector3( Float( contrast, 1 ), Float( saturation, 1 ), Float( brightness, 1 ) ), average );
		var matrix = ColorMatrices.Tint( new System.Numerics.Vector3( tintColor.x, tintColor.y, tintColor.z ), 1, 1 ) * correct;
		var rows = ColorMatrices.Rows( matrix );

		for ( var row = 0; row < 3; row++ )
			_material.Set( $"{uniform}{row}", new Vector4( rows[row][0], rows[row][1], rows[row][2], rows[row][3] ) );
	}

	/// <summary>
	/// A material whose shader isn't ported yet: its base colour and normal on the vertex lit generic shader, untinted,
	/// so maps show their shapes and surfaces until the real port replaces this. Diffuse only: without the shader's own
	/// roughness and metalness, its specular would be made up.
	/// </summary>
	private void SetupStandIn()
	{
		SetRenderStateFeatures();

		foreach ( var parameter in StandInColors )
		{
			if ( BindTexture( "g_tColor", parameter ) )
				break;
		}

		foreach ( var parameter in StandInNormals )
		{
			if ( BindTexture( "g_tNormal", parameter ) )
				break;
		}

		_material.Set( "g_flInverseBlendTint", -1f );
		_material.Set( "g_flReflectance", MaterialParameters.Reflectance );
		_material.Set( "g_flAlphaTestReference", Float( "g_flAlphaTestReference", 0.5f ) );
		_material.Set( "g_flOpacityScale", Float( "g_flOpacityScale", 1 ) );
	}

	/// <summary>
	/// The combos that change render state, which stay features of the port: blending, cut-outs, two sided rendering.
	/// </summary>
	private void SetRenderStateFeatures()
	{
		var translucent = Combo( "F_TRANSLUCENT" );

		SetFeature( "F_TRANSLUCENT", translucent );
		SetFeature( "F_ALPHA_TEST", Combo( "F_ALPHA_TEST" ) && !translucent );
		SetFeature( "F_ADDITIVE_BLEND", translucent && Combo( "F_ADDITIVE_BLEND" ) );
		SetFeature( "F_RENDER_BACKFACES", Combo( "F_RENDER_BACKFACES" ) );
		SetFeature( "F_DO_NOT_CAST_SHADOWS", Combo( "F_DO_NOT_CAST_SHADOWS" ) );
	}

	/// <summary>
	/// The colour and normal map texture coordinate transforms (g_vTexCoordXform0/1, g_vNormalTexCoordXform0/1) and the
	/// colour's scroll speed.
	/// </summary>
	private void SetTexCoordTransforms()
	{
		var (row0, row1) = MaterialParameters.TexCoordXform(
			GetVector2( "g_vTexCoordScale", 1 ), Float( "g_flTexCoordRotation", 0 ), GetVector2( "g_vTexCoordCenter", 0.5f ), GetVector2( "g_vTexCoordOffset", 0 ) );

		_material.Set( "g_vTexCoordXform0", row0 );
		_material.Set( "g_vTexCoordXform1", row1 );
		_material.Set( "g_vTexCoordScrollSpeed", GetVector2( "g_vTexCoordScrollSpeed", 0 ) );

		var (normal0, normal1) = MaterialParameters.NormalTexCoordXform(
			GetVector2( "g_vNormalTexCoordScale", 1 ), Float( "g_flNormalTexCoordRotation", 0 ), GetVector2( "g_vNormalTexCoordCenter", 0.5f ), GetVector2( "g_vNormalTexCoordOffset", 0 ) );

		_material.Set( "g_vNormalTexCoordXform0", normal0 );
		_material.Set( "g_vNormalTexCoordXform1", normal1 );
	}

	/// <summary>
	/// Binds the shader's default to each of its textures the material left unbound.
	/// </summary>
	private void BindDefaultTextures( string shader )
	{
		if ( !DefaultTextures.TryGetValue( shader, out var textures ) )
			return;

		foreach ( var (texture, srgb, value) in textures )
		{
			if ( !_boundTextures.Contains( texture ) )
				_material.Set( texture, SolidTexture( value, srgb ) );
		}
	}

	/// <summary>
	/// A 1x1 texture that reads as <paramref name="value"/>, sRGB encoded for a texture read as sRGB.
	/// </summary>
	private static Texture SolidTexture( Vector4 value, bool srgb )
	{
		byte Channel( float x, bool encode ) => (byte)MathF.Round( Math.Clamp( encode ? ColorMatrices.LinearToSrgb( x ) : x, 0f, 1f ) * 255f );

		var pixel = new[] { Channel( value.x, srgb ), Channel( value.y, srgb ), Channel( value.z, srgb ), Channel( value.w, false ) };
		var key = BitConverter.ToUInt32( pixel );

		lock ( SolidTextures )
		{
			if ( !SolidTextures.TryGetValue( key, out var texture ) || !texture.IsValid() )
				SolidTextures[key] = texture = Texture.Create( 1, 1 ).WithName( $"cs2_solid_{key:x8}" ).WithData( pixel ).Finish();

			return texture;
		}
	}

	/// <summary>
	/// sky.vfx, skybox materials: an HDR cubemap shown at an exposure bias. s&amp;box's own sky shader draws the same
	/// thing, taking the bias as a tint.
	/// </summary>
	private Material ImportSky()
	{
		const string SkyShader = "shaders/sky.shader";

		var material = Material.Create( name, SkyShader, false )
			?? throw new InvalidOperationException( $"Couldn't create a material with {SkyShader}." );

		if ( mount.LoadTexture( source.GetTexture( "g_tSkyTexture" ) ) is { } texture )
			material.Set( "SkyTexture", texture );

		var exposure = MathF.Pow( 2, source.GetFloat( "g_flBrightnessExposureBias" ) + source.GetFloat( "g_flRenderOnlyExposureBias" ) );
		material.Set( "SkyTint", new Color( exposure, exposure, exposure ) );

		return material;
	}

	private bool BindTexture( string shaderTexture, string parameter )
	{
		if ( parameter is null || mount.LoadTexture( source.GetTexture( parameter ) ) is not { } texture )
			return false;

		_material.Set( shaderTexture, texture );
		_boundTextures.Add( shaderTexture );
		return true;
	}

	private bool Combo( string feature ) => source.GetInt( feature ) != 0;

	private void SetFeature( string feature, bool enabled ) => _material.SetFeature( feature, enabled ? 1 : 0 );

	private float Float( string parameter, float fallback ) => source.FloatParams.TryGetValue( parameter, out var value ) ? value : fallback;

	private Vector2 GetVector2( string parameter, float fallback )
	{
		var value = source.GetVector( parameter, [fallback, fallback, 0, 0] );
		return new Vector2( value[0], value[1] );
	}

	private Vector3 GetVector3( string parameter, float fallback )
	{
		var value = source.GetVector( parameter, [fallback, fallback, fallback, 0] );
		return new Vector3( value[0], value[1], value[2] );
	}
}
