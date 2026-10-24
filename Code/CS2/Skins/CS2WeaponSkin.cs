using System;

namespace CS2;

/// <summary>
/// Paints CS2 weapon finishes onto mounted weapon models, as the game's composite material system does: the paint
/// kit's composite material (vcompmat) runs on each of the model's materials it applies to (<see cref="CS2CompositeProgram"/>),
/// its passes are drawn on the GPU by <c>shaders/cs2_customweapon.shader</c> (the port of csgo_customweapon), and the
/// weapon material is copied with the generated textures and the paint's normal map, pearlescence and ambient
/// occlusion.
/// Finishes are cached by composite material, weapon material, float, seed and size, shared between the renderers
/// showing them (a viewmodel and a third person weapon), and their textures freed a while after nothing shows them
/// (textures freed while a frame or upload still used them crashed the GPU before; see CS2MapLighting).
/// </summary>
public static class CS2WeaponSkin
{
	/// <summary>
	/// The largest texture a finish is generated at. The game's vcompmats ask for 4096 (whether the game lowers that
	/// by its texture settings isn't traced); 2048 keeps a finish to about 45 MB of video memory.
	/// </summary>
	public static int MaxResolution { get; set; } = 2048;

	/// <summary>
	/// The most mips the compositor writes (a 4096 texture's).
	/// </summary>
	private const int MaxMips = 13;

	/// <summary>
	/// How long a finish nothing shows any more is kept before its textures are freed.
	/// </summary>
	private const float RetirementSeconds = 5f;

	// Made on first use, not with the class: a shader compiling while the code hot-loads isn't there yet.
	private static ComputeShader _compositor;
	private static ComputeShader Compositor => _compositor ??= new ComputeShader( "shaders/cs2_customweapon.shader" );

	// The glove paints' shader (csgo_customglove), for the passes that render one.
	private static ComputeShader _gloveCompositor;
	private static ComputeShader GloveCompositor => _gloveCompositor ??= new ComputeShader( "shaders/cs2_customglove.shader" );

	private sealed class Finish
	{
		public string Key;
		public Material Material;
		public List<Texture> Textures = [];
		public int Users;
		public RealTimeSince Unused;
	}

	private sealed class Applied
	{
		public Model Model;
		public List<int> Slots = [];
		public List<Finish> Finishes = [];
		public bool ChangedBodyGroup;
		public string BodyGroup;
		public int DefaultChoice;
	}

	private static readonly Dictionary<string, Finish> Finishes = new();
	private static readonly Dictionary<ModelRenderer, Applied> AppliedTo = new();

	/// <summary>
	/// The mounted textures generation reads, kept for the session (as CS2MapLighting keeps lighting textures): they're
	/// shared between finishes and dropping them while an upload or a dispatch used them is what crashed the GPU before.
	/// </summary>
	private static readonly Dictionary<string, Texture> InputTextures = new( StringComparer.OrdinalIgnoreCase );
	private static readonly Dictionary<uint, Texture> SolidTextures = new();

	/// <summary>
	/// Paints a weapon renderer's model with a paint kit (items_game name, e.g. <c>cu_ak47_asiimov</c>) at a float
	/// (held to the kit's range) and pattern seed; null or empty puts the weapon's own materials back. Shows the weapon's
	/// CS:GO model for kits made for it (use_legacy_model) and the CS2 one otherwise. Works the same on a viewmodel and
	/// a third person weapon. Returns whether a finish was applied. Call on the main thread.
	/// </summary>
	public static bool Apply( ModelRenderer weapon, string paintKitName, float wear, int seed )
	{
		ReleaseRetired();

		if ( !weapon.IsValid() )
			return false;

		Restore( weapon );

		if ( string.IsNullOrWhiteSpace( paintKitName ) || weapon.Model is not { IsError: false } model )
			return false;

		if ( CS2PaintKits.Find( paintKitName ) is not { } kit )
		{
			Log.Warning( $"CS2: no paint kit '{paintKitName}'." );
			return false;
		}

		var composite = CS2CompositeMaterial.Find( kit.CompositeMaterial );
		var skinModel = CS2SkinModel.Find( ModelPath( model ) );
		if ( composite is null || skinModel is null )
		{
			Log.Warning( $"CS2: can't paint {ModelPath( model ) ?? model.Name} with {kit.Name} ({(composite is null ? "no composite material" : "not a mounted weapon model")})." );
			return false;
		}

		wear = kit.ClampWear( wear );
		var applied = new Applied { Model = model };

		// The CS:GO model for kits made for it, the CS2 one otherwise.
		var legacy = kit.LegacyModel && skinModel.LegacyBodyGroup is not null;
		if ( skinModel.LegacyBodyGroup is not null )
		{
			weapon.SetCS2BodyGroup( skinModel.LegacyBodyGroup, legacy ? skinModel.LegacyChoiceIndex : skinModel.DefaultChoiceIndex );
			applied.ChangedBodyGroup = legacy;
			applied.BodyGroup = skinModel.LegacyBodyGroup;
			applied.DefaultChoice = skinModel.DefaultChoiceIndex;
		}

		for ( var slot = 0; slot < weapon.Materials.Count; slot++ )
		{
			var path = MaterialPath( weapon.Materials.GetOriginal( slot ) );
			if ( path is null || skinModel.FindMaterial( path ) is not { } target || !skinModel.IsShown( target, legacy ) || !composite.Matches( target.Material ) )
				continue;

			if ( GetFinish( composite, target, weapon.Materials.GetOriginal( slot ), wear, seed ) is not { } finish )
				continue;

			weapon.Materials.SetOverride( slot, finish.Material );
			applied.Slots.Add( slot );
			applied.Finishes.Add( finish );
		}

		AppliedTo[weapon] = applied;
		return applied.Finishes.Count > 0;
	}

	/// <summary>
	/// Whether <see cref="Apply"/> shows a renderer's CS:GO model (a use_legacy_model kit on a weapon that has both).
	/// </summary>
	public static bool ShowsLegacyModel( ModelRenderer weapon ) => weapon is not null && AppliedTo.TryGetValue( weapon, out var applied ) && applied.ChangedBodyGroup;

	/// <summary>
	/// Puts back what <see cref="Apply"/> changed on a renderer: its materials and body group.
	/// </summary>
	public static void Restore( ModelRenderer weapon )
	{
		if ( weapon is null || !AppliedTo.Remove( weapon, out var applied ) )
			return;

		if ( weapon.IsValid() )
		{
			// Overrides are kept by slot across model changes: cleared whatever the model is now.
			foreach ( var slot in applied.Slots )
				weapon.Materials.SetOverride( slot, null );

			// Back to the default model (a model without the body group ignores it).
			if ( applied.ChangedBodyGroup )
				weapon.SetCS2BodyGroup( applied.BodyGroup, applied.DefaultChoice );
		}

		foreach ( var finish in applied.Finishes )
			Release( finish );
	}

	/// <summary>
	/// Stops reusing every finish generated so far (e.g. after the compositor shader changed): renderers keep what they
	/// show until painted again, and the textures are freed once nothing shows them.
	/// </summary>
	public static void Invalidate()
	{
		foreach ( var finish in Finishes.Values.ToArray() )
		{
			Finishes.Remove( finish.Key );
			finish.Key = $"{finish.Key}|invalidated{Guid.NewGuid()}";
			Finishes[finish.Key] = finish;
		}
	}

	/// <summary>
	/// The finish of one weapon material, from the cache or generated.
	/// </summary>
	private static Finish GetFinish( CS2CompositeMaterial composite, CS2SkinModelMaterial target, Material original, float wear, int seed )
	{
		var resolution = Math.Max( 16, MaxResolution );
		var key = $"{composite.Path}|{target.Material.Path}|{wear:R}|{seed}|{resolution}";

		if ( Finishes.TryGetValue( key, out var cached ) && cached.Textures.Count > 0 )
		{
			cached.Users++;
			return cached;
		}

		CS2CompositeResult result;
		try
		{
			result = CS2CompositeProgram.Run( composite, target.Material, target.AttributeMaterials, seed, wear );
		}
		catch ( Exception e )
		{
			Log.Warning( $"CS2: {composite.Path} on {target.Material.Path}: {e.Message}" );
			return null;
		}

		var finish = new Finish { Key = key, Users = 1 };
		var material = original.CreateCopy( $"cs2skin/{composite.Path}/{target.Material.Path}/{wear:0.######}/{seed}" );

		foreach ( var pass in result.Passes )
		{
			if ( Generate( pass, Math.Min( resolution, Math.Max( 16, pass.Resolution ) ) ) is { } texture )
			{
				finish.Textures.Add( texture );
				material.Set( pass.TargetParameter, texture );
			}
		}

		ApplyParameters( material, result.Material, target.Material );
		finish.Material = material;
		Finishes[key] = finish;
		return finish;
	}

	/// <summary>
	/// The final material's other changes its shader has: for weapons (cs2_weapon) the paint's normal map, ambient
	/// occlusion and the like, and its pearlescence; for gloves (cs2_character) cloth shading, anisotropic gloss and the
	/// sheen. Glitter and iridescence (csgo_weapon's F_GLITTER, F_IRIDESCENCE) aren't ported.
	/// </summary>
	private static void ApplyParameters( Material material, Dictionary<string, CS2CompositeValue> values, CS2MaterialParams target )
	{
		foreach ( var (name, value) in values )
		{
			if ( target.Values.TryGetValue( name, out var original ) && original == value )
				continue;

			if ( value.IsText )
			{
				if ( value.Text.StartsWith( CS2CompositeProgram.GeneratedPrefix, StringComparison.Ordinal ) )
					continue;

				if ( name is "g_tColor" or "g_tNormal" or "g_tAmbientOcclusion" or "g_tMetalness" or "g_tTintMask" or "g_tSelfIllumMask" or "g_tAnisoGloss" && LoadInput( value.Text ) is { } texture )
					material.Set( name, texture );

				continue;
			}

			switch ( name )
			{
				case "g_flPearlescentScale":
				case "g_flMetalnessTransitionBias":
				case "g_flModelTintAmount":
					material.Set( name, value.X );
					break;

				case "g_vMetalnessRemapRange":
					material.Set( name, new Vector2( value.Number.x, value.Number.y ) );
					break;

				case "g_vColorTint":
					material.Set( name, SrgbToLinear( new Vector3( value.Number.x, value.Number.y, value.Number.z ) ) );
					break;

				// cs2_character takes these combos as uniforms (the mount's MaterialImporter.SetupCharacter).
				case "F_CLOTH_SHADING":
					material.Set( "g_flClothShading", value.X != 0 ? 1f : 0f );
					break;

				case "F_ANISOTROPIC_GLOSS":
					material.Set( "g_flAnisotropicGloss", value.X != 0 ? 1f : 0f );
					break;

				case "g_flSheenScale":
					material.Set( name, value.X );
					break;
			}
		}
	}

	/// <summary>
	/// Generates one pass's texture: each mip level composited at its own size by the compute shader.
	/// </summary>
	private static Texture Generate( CS2CompositePass pass, int resolution )
	{
		var size = 1 << Math.Min( MaxMips - 1, (int)MathF.Floor( MathF.Log2( resolution ) ) );
		var mips = (int)MathF.Log2( size ) + 1;

		var texture = Texture.Create( size, size, ImageFormat.RGBA8888 )
			.WithUAVBinding()
			.WithMips( mips )
			.WithName( $"cs2skin_{pass.TargetParameter}" )
			.Finish();

		if ( texture is null || !texture.IsValid() )
			return null;

		// The pass renders its paint material's shader: csgo_customweapon for weapons, csgo_customglove for gloves.
		var glove = pass.Values.TryGetValue( "Shader", out var shader ) && shader.IsText && shader.Text.StartsWith( "csgo_customglove", StringComparison.OrdinalIgnoreCase );

		var attributes = new RenderAttributes();
		if ( glove )
			CS2CustomGloveParameters.Set( attributes, pass.Values, LoadInput, Solid );
		else
			CS2CustomWeaponParameters.Set( attributes, pass.Values, LoadInput, Solid );

		// The weapon and character shaders read their colour as sRGB.
		attributes.Set( "CS2CompositeSrgbOutput", pass.TargetParameter is "g_tColor" or "g_tSelfIllumMask" ? 1f : 0f );

		// Every mip in one dispatch (the shader has a binding per mip; the unused ones are bound to the last), so a finish
		// is two submissions, not one per mip: standalone submissions take a slot each of the global descriptor ring.
		for ( var mip = 0; mip < MaxMips; mip++ )
			attributes.Set( $"CS2CompositeOutput{mip}", texture, Math.Min( mip, mips - 1 ) );

		attributes.Set( "CS2CompositeOutputSize", (float)size );
		attributes.Set( "CS2CompositeOutputMips", (float)mips );
		(glove ? GloveCompositor : Compositor).DispatchWithAttributes( attributes, size, size, 1 );

		return texture;
	}

	private static void Release( Finish finish )
	{
		if ( --finish.Users <= 0 )
			finish.Unused = 0;

		_ = ReleaseRetiredLater();
	}

	private static async System.Threading.Tasks.Task ReleaseRetiredLater()
	{
		await GameTask.DelayRealtimeSeconds( RetirementSeconds + 1f );
		ReleaseRetired();
	}

	/// <summary>
	/// Frees the finishes nothing has shown for a while, and forgets renderers that are gone (their finishes go the
	/// same way).
	/// </summary>
	private static void ReleaseRetired()
	{
		foreach ( var renderer in AppliedTo.Keys.Where( x => !x.IsValid() ).ToArray() )
			Restore( renderer );

		foreach ( var finish in Finishes.Values.Where( x => x.Users <= 0 && x.Unused > RetirementSeconds ).ToArray() )
		{
			Finishes.Remove( finish.Key );
			foreach ( var texture in finish.Textures )
				texture.Dispose();

			finish.Textures.Clear();
		}
	}

	/// <summary>
	/// A mounted texture by its game path, kept for the session.
	/// </summary>
	private static Texture LoadInput( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( path.EndsWith( "_c" ) )
			path = path[..^2];

		if ( InputTextures.TryGetValue( path, out var cached ) )
			return cached;

		var texture = Texture.Load( $"mount://cs2/{path}", false );
		if ( texture is { IsValid: true, IsError: false } )
			return InputTextures[path] = texture;

		InputTextures[path] = null;
		return null;
	}

	/// <summary>
	/// A 1x1 texture of a colour, its bytes as given (a texture read as sRGB decodes them, as the game's defaults are).
	/// </summary>
	private static Texture Solid( Vector4 value )
	{
		byte Channel( float x ) => (byte)MathF.Round( Math.Clamp( x, 0f, 1f ) * 255f );
		var pixel = new[] { Channel( value.x ), Channel( value.y ), Channel( value.z ), Channel( value.w ) };
		var key = BitConverter.ToUInt32( pixel );

		if ( !SolidTextures.TryGetValue( key, out var texture ) || !texture.IsValid() )
			SolidTextures[key] = texture = Texture.Create( 1, 1 ).WithName( $"cs2skin_solid_{key:x8}" ).WithData( pixel ).Finish();

		return texture;
	}

	private static float SrgbToLinear( float x ) => x <= 0.04045f ? x * 0.077399380505f : MathF.Pow( x * 0.947867334f + 0.052132703f, 2.4f );

	private static Vector3 SrgbToLinear( Vector3 x ) => new( SrgbToLinear( x.x ), SrgbToLinear( x.y ), SrgbToLinear( x.z ) );

	/// <summary>
	/// A mounted model's game path, e.g. <c>weapons/models/ak47/weapon_rif_ak47.vmdl</c>.
	/// </summary>
	private static string ModelPath( Model model )
	{
		var path = model.ResourcePath is { Length: > 0 } resource ? resource : model.Name;
		return GamePath( path );
	}

	/// <summary>
	/// A material the mount made's game path (the mount names them cs2/path).
	/// </summary>
	private static string MaterialPath( Material material )
	{
		if ( material is null )
			return null;

		if ( material.ResourcePath is { Length: > 0 } resource && resource.StartsWith( "mount://cs2/", StringComparison.OrdinalIgnoreCase ) )
			return GamePath( resource );

		return material.Name is { } name && name.StartsWith( "cs2/", StringComparison.OrdinalIgnoreCase ) ? GamePath( name[4..] ) : null;
	}

	private static string GamePath( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( path.StartsWith( "mount://cs2/", StringComparison.OrdinalIgnoreCase ) )
			path = path["mount://cs2/".Length..];

		if ( path.EndsWith( "_c" ) )
			path = path[..^2];

		return path;
	}
}
