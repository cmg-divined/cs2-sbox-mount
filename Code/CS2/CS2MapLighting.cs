using System;
using System.Runtime.InteropServices;

namespace CS2;

/// <summary>
/// A mounted CS2 map's lighting, as its shaders light it: the baked lightmaps, the light probe volumes, the cubemaps,
/// the sun, the stationary and dynamic lights, and the fog. The mount builds it from the map's compiled lighting; on
/// enable it is handed to every CS2 shader in the scene as scene wide render attributes, which those shaders light
/// with the way the game's forward shaders do (cs2_lighting.hlsl).
/// </summary>
/// <remarks>
/// Everything is in the map's own space. This GameObject's transform places the map, so the map can be moved:
/// the shaders bring what they light into the map's space first.
/// </remarks>
[Title( "CS2 Map Lighting" ), Category( "Counter-Strike 2" ), Icon( "wb_sunny" )]
public sealed class CS2MapLighting : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// Lights only the renderers beside it (under its parent: a 3D skybox's world, lit by its own lighting), through
	/// their own render attributes, rather than every CS2 shader in the scene. The fog stays the scene's: the skybox is
	/// fogged by the map's, as seen from its sky camera.
	/// </summary>
	[Property] public bool Scoped { get; set; }

	/// <summary>
	/// Every texture the lighting reads, by mounted path. The other properties refer to them by index in this list.
	/// </summary>
	[Property, Group( "Textures" )]
	public List<string> Textures { get; set; } = [];

	/// <summary>
	/// The indirect light the map compiler baked (irradiance.vtex), and the light of static lights.
	/// </summary>
	[Property, Group( "Lightmaps" )] public int LightmapIrradiance { get; set; } = -1;

	/// <summary>
	/// How much of each stationary light's direct light each lightmap texel doesn't see, one light per channel
	/// (direct_light_shadows.vtex).
	/// </summary>
	[Property, Group( "Lightmaps" )] public int LightmapShadows { get; set; } = -1;

	/// <summary>
	/// The mips below the lightmaps' top ones, for lightmaps the mount serves in two parts (too large to create
	/// whole); -1 when the textures above hold them all.
	/// </summary>
	[Property, Group( "Lightmaps" )] public int LightmapIrradianceLowerMips { get; set; } = -1;
	[Property, Group( "Lightmaps" )] public int LightmapShadowsLowerMips { get; set; } = -1;

	/// <summary>
	/// Which way the baked indirect light comes from (directional_irradiance.vtex), which the high shader quality turns
	/// the irradiance towards the normal map's normal with; and its lower mips, as above.
	/// </summary>
	[Property, Group( "Lightmaps" )] public int LightmapDirectional { get; set; } = -1;
	[Property, Group( "Lightmaps" )] public int LightmapDirectionalLowerMips { get; set; } = -1;

	[Property, Group( "Lightmaps" )] public Vector2 LightmapUvScale { get; set; } = Vector2.One;

	/// <summary>
	/// The light probe volumes, in the order they claim a point: earlier volumes first.
	/// </summary>
	[Property, Group( "Probes" )] public List<CS2ProbeVolume> ProbeVolumes { get; set; } = [];

	/// <summary>
	/// The cube array the cubemap volumes reflect from. The game reflects one of its cubes per view.
	/// </summary>
	[Property, Group( "Cubemaps" )] public int EnvironmentMaps { get; set; } = -1;
	[Property, Group( "Cubemaps" )] public int EnvironmentMapMips { get; set; } = 1;

	/// <summary>
	/// The cubemap volumes, which pick the cube a view reflects by where its camera is.
	/// </summary>
	[Property, Group( "Cubemaps" )] public List<CS2CubemapVolume> CubemapVolumes { get; set; } = [];

	/// <summary>
	/// How the high shader quality blends between the cubemap volumes: one per volume, in the same order.
	/// </summary>
	[Property, Group( "Cubemaps" )] public List<CS2CubemapBlend> CubemapBlends { get; set; } = [];

	/// <summary>
	/// What lights probe lit surfaces outside every probe volume, per colour channel: a first order harmonic,
	/// (normal, 1) dotted with it.
	/// </summary>
	[Property, Group( "Ambient" )] public Vector4 AmbientRed { get; set; }
	[Property, Group( "Ambient" )] public Vector4 AmbientGreen { get; set; }
	[Property, Group( "Ambient" )] public Vector4 AmbientBlue { get; set; }

	/// <summary>
	/// A constant the game's shaders add to the direct diffuse light everywhere. Where the engine takes it from isn't
	/// known yet (unverified), so it is zero.
	/// </summary>
	[Property, Group( "Ambient" )] public Vector3 ConstantAmbient { get; set; }

	/// <summary>
	/// The sun's direction from the surface to it. Zero when the map has no realtime sun.
	/// </summary>
	[Property, Group( "Sun" )] public Vector3 SunDirection { get; set; }

	/// <summary>
	/// Linear colour times brightness.
	/// </summary>
	[Property, Group( "Sun" )] public Vector3 SunColor { get; set; }

	/// <summary>
	/// Which channel of the lightmap and probe shadows holds the sun.
	/// </summary>
	[Property, Group( "Sun" )] public Vector4 SunShadowMask { get; set; }

	/// <summary>
	/// No surface takes the sun's specular sharper than this roughness.
	/// </summary>
	[Property, Group( "Sun" )] public float SunMinRoughness { get; set; }

	/// <summary>
	/// The stationary and dynamic lights, as the game hands them to its shaders. Omni lights are several faces.
	/// </summary>
	[Property, Group( "Lights" )] public List<CS2BarnLight> Lights { get; set; } = [];

	/// <summary>
	/// env_gradient_fog: bias and scale of distance (x, z) and height (y, w), as the shaders take them.
	/// </summary>
	[Property, Group( "Fog" )] public bool GradientFog { get; set; }
	[Property, Group( "Fog" )] public Vector4 GradientFogBiasAndScale { get; set; }
	[Property, Group( "Fog" )] public Vector2 GradientFogExponents { get; set; } = Vector2.One;

	/// <summary>
	/// Linear colour times strength, and the most the fog covers.
	/// </summary>
	[Property, Group( "Fog" )] public Vector4 GradientFogColor { get; set; }

	/// <summary>
	/// Squared distance and height the gradient fog starts at.
	/// </summary>
	[Property, Group( "Fog" )] public Vector2 GradientFogCulling { get; set; }

	/// <summary>
	/// env_cubemap_fog: fog that fades the distance into the sky's cubemap.
	/// </summary>
	[Property, Group( "Fog" )] public bool CubemapFog { get; set; }

	/// <summary>
	/// Distance offset and scale, the mip bias, and the distance exponent.
	/// </summary>
	[Property, Group( "Fog" )] public Vector4 CubemapFogDistance { get; set; }

	/// <summary>
	/// Height offset, scale and exponent, and the mip the fog is sharpest at.
	/// </summary>
	[Property, Group( "Fog" )] public Vector4 CubemapFogHeight { get; set; }

	/// <summary>
	/// The squared distance the fog starts at, its exposure (linear), and the most it covers.
	/// </summary>
	[Property, Group( "Fog" )] public Vector4 CubemapFogParameters { get; set; }

	[Property, Group( "Fog" )] public int CubemapFogTexture { get; set; } = -1;

	/// <summary>
	/// Rotation of the fog's sky, from world space to the cubemap's.
	/// </summary>
	[Property, Group( "Fog" )] public Angles CubemapFogRotation { get; set; }

	private readonly List<Texture> _textures = [];
	private readonly List<string> _texturePaths = [];

	/// <summary>
	/// Lighting textures, shared by every map lighting using them, with how many use each. A texture released while
	/// its upload is still queued faults the GPU, so one no lighting uses any more is kept a while before it's let go
	/// (2026-10-20: dropping freshly loaded lighting textures crashed the GPU twice).
	/// </summary>
	private static readonly Dictionary<string, (Texture Texture, int Users)> LoadedTextures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Textures no lighting uses any more, and buffers a lighting replaced, kept until frames and uploads that may
	/// still use them are done.
	/// </summary>
	private static readonly List<(RealTimeSince Retired, string Texture, GpuBuffer Buffer)> Retired = [];

	/// <summary>
	/// How long a retired texture or buffer is kept before it's let go.
	/// </summary>
	private const float RetirementSeconds = 5f;

	private GpuBuffer<CS2ProbeVolume.Gpu> _probes;
	private GpuBuffer<CS2CubemapVolume> _cubemaps;
	private GpuBuffer<CS2CubemapBlend> _cubemapBlends;
	private GpuBuffer<CS2BarnLight> _lights;

	/// <summary>
	/// The table the character, weapon and high quality shaders take their specular's scale and bias from (a texture
	/// array; they read its second layer). Bound by index, so a shader reload can't drop it; loaded once and kept.
	/// </summary>
	private const string BrdfLookupPath = "mount://cs2/textures/dev/brdf_lut.vtex";
	private static Texture _brdfLookup;
	private GpuBuffer<uint> _grid;

	/// <summary>
	/// The most cells the volume and light grid has; its cells grow until it fits.
	/// </summary>
	private const int MaxGridCells = 196608;

	/// <summary>
	/// The grid's lists: probe volumes, lights, cubemap volumes.
	/// </summary>
	private const int GridLists = 3;

	private bool _applied;
	private Transform? _mapTransform;

	/// <summary>
	/// Where the lighting was handed: the scene's render attributes, or (scoped) its renderers'.
	/// </summary>
	private AttributeTargets _targets;

	// Applied on the first update rather than on enable: the mount builds map scenes in a scene that is never
	// updated, and loading the lightmaps there would be wasted.
	protected override void OnEnabled() => _applied = false;

	protected override void OnUpdate()
	{
		ReleaseRetired();

		if ( !_applied )
		{
			_applied = true;
			Apply();
		}

		if ( _mapTransform != WorldTransform )
			SetMapTransform();
	}

	protected override void OnDisabled()
	{
		_applied = false;
		Clear();

		// What this retired is let go on update, and if no map lighting is left to update, after the wait regardless.
		_ = ReleaseRetiredLater();
	}

	private static async System.Threading.Tasks.Task ReleaseRetiredLater()
	{
		await GameTask.DelayRealtimeSeconds( RetirementSeconds + 1f );
		ReleaseRetired();
	}

	protected override void OnValidate() => _applied = false;

	/// <summary>
	/// Loads the textures, uploads the volumes and lights, and sets the scene's render attributes (scoped, its
	/// renderers').
	/// </summary>
	public void Apply()
	{
		Clear();

		if ( Scene?.RenderAttributes is null )
			return;

		var attributes = _targets = new AttributeTargets( Scoped
			? (GameObject.Parent ?? GameObject).Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ).Select( x => x.Attributes ).ToList()
			: [Scene.RenderAttributes] );

		foreach ( var path in Textures )
		{
			_textures.Add( AcquireTexture( path ) );
			_texturePaths.Add( path );
		}

		attributes.Set( "CS2Lighting", 1 );
		SetMapTransform();

		attributes.Set( "CS2LightmapIrradiance", TextureIndex( LightmapIrradiance ) );
		attributes.Set( "CS2LightmapShadows", TextureIndex( LightmapShadows ) );
		attributes.Set( "CS2LightmapIrradianceLowerMips", TextureIndex( LightmapIrradianceLowerMips ) );
		attributes.Set( "CS2LightmapShadowsLowerMips", TextureIndex( LightmapShadowsLowerMips ) );
		attributes.Set( "CS2LightmapDirectional", TextureIndex( LightmapDirectional ) );
		attributes.Set( "CS2LightmapDirectionalLowerMips", TextureIndex( LightmapDirectionalLowerMips ) );
		attributes.Set( "CS2LightmapUvScale", LightmapUvScale );

		if ( _brdfLookup is not { IsValid: true } )
			_brdfLookup = AcquireTexture( BrdfLookupPath );
		attributes.Set( "CS2BRDFLookup", _brdfLookup is { IsValid: true, IsError: false } ? _brdfLookup.Index : 0 );

		_probes = Upload( "CS2ProbeVolumes", ProbeVolumes.Select( x => x.ToGpu( TextureIndex ) ).ToList() );
		attributes.Set( "CS2ProbeVolumes", _probes );
		attributes.Set( "CS2ProbeVolumeCount", ProbeVolumes.Count );

		attributes.Set( "CS2EnvironmentMaps", TextureIndex( EnvironmentMaps ) );
		attributes.Set( "CS2EnvironmentMapMips", (float)Math.Max( EnvironmentMapMips - 1, 0 ) );
		_cubemaps = Upload( "CS2CubemapVolumes", CubemapVolumes );
		attributes.Set( "CS2CubemapVolumes", _cubemaps );
		attributes.Set( "CS2CubemapVolumeCount", CubemapVolumes.Count );

		// Without one blend per volume, the shaders keep to the single cube per view.
		var blends = CubemapBlends.Count == CubemapVolumes.Count ? CubemapBlends : new List<CS2CubemapBlend>();
		_cubemapBlends = Upload( "CS2CubemapBlend", blends );
		attributes.Set( "CS2CubemapBlend", _cubemapBlends );
		attributes.Set( "CS2CubemapBlendCount", blends.Count );

		attributes.Set( "CS2AmbientR", AmbientRed );
		attributes.Set( "CS2AmbientG", AmbientGreen );
		attributes.Set( "CS2AmbientB", AmbientBlue );
		attributes.Set( "CS2ConstantAmbient", ConstantAmbient );

		attributes.Set( "CS2SunDirection", SunDirection );
		attributes.Set( "CS2SunColor", SunColor );
		attributes.Set( "CS2SunShadowMask", SunShadowMask );
		attributes.Set( "CS2SunMinRoughness", SunMinRoughness );

		_lights = Upload( "CS2BarnLights", Lights.Select( WithCookie ).ToList() );
		attributes.Set( "CS2BarnLights", _lights );
		attributes.Set( "CS2BarnLightCount", Lights.Count );

		BuildGrid();

		if ( Scoped )
			return;

		attributes.Set( "CS2GradientFog", GradientFog ? 1 : 0 );
		attributes.Set( "CS2GradientFogBiasAndScale", GradientFogBiasAndScale );
		attributes.Set( "CS2GradientFogExponents", GradientFogExponents );
		attributes.Set( "CS2GradientFogColor", GradientFogColor );
		attributes.Set( "CS2GradientFogCulling", GradientFogCulling );

		var cubemapFogTexture = TextureIndex( CubemapFogTexture );
		attributes.Set( "CS2CubemapFog", CubemapFog && cubemapFogTexture != 0 ? 1 : 0 );
		attributes.Set( "CS2CubemapFogDistance", CubemapFogDistance );
		attributes.Set( "CS2CubemapFogHeight", CubemapFogHeight );
		attributes.Set( "CS2CubemapFogParameters", CubemapFogParameters );
		attributes.Set( "CS2CubemapFogTexture", cubemapFogTexture );

		// Rows of the rotation from world space into the sky's: the sky's axes.
		var rotation = CubemapFogRotation.ToRotation();
		attributes.Set( "CS2CubemapFogRotation0", rotation.Forward );
		attributes.Set( "CS2CubemapFogRotation1", rotation.Left );
		attributes.Set( "CS2CubemapFogRotation2", rotation.Up );
	}

	/// <summary>
	/// The transform from world space into the map's, as three rows.
	/// </summary>
	private void SetMapTransform()
	{
		var transform = WorldTransform;
		_mapTransform = transform;

		if ( _targets is null )
			return;

		Vector3[] axes = [transform.Rotation.Forward, transform.Rotation.Left, transform.Rotation.Up];
		var scale = transform.Scale;

		for ( var i = 0; i < 3; i++ )
		{
			var row = axes[i] / scale[i];
			_targets.Set( $"CS2WorldToMap{i}", new Vector4( row, -Vector3.Dot( row, transform.Position ) ) );
		}
	}

	/// <summary>
	/// Takes the lighting back out of the scene, leaving CS2 shaders lit by s&amp;box's own lights.
	/// </summary>
	public void Clear()
	{
		_targets?.Set( "CS2Lighting", 0 );
		_targets?.Set( "CS2GridSize", Vector4.Zero );
		_targets = null;
		_mapTransform = null;

		Retire( _probes );
		Retire( _cubemaps );
		Retire( _cubemapBlends );
		Retire( _lights );
		Retire( _grid );
		_probes = null;
		_cubemaps = null;
		_cubemapBlends = null;
		_lights = null;
		_grid = null;
		foreach ( var path in _texturePaths )
			ReleaseTexture( path );

		_textures.Clear();
		_texturePaths.Clear();
	}

	/// <summary>
	/// The grid the shaders find the volumes and lights around a point with (cs2_lighting.hlsl), rather than visiting
	/// every one of them per pixel, as the game's clustered culling does. A uniform grid over the map, each cell
	/// listing the probe volumes, the lights and the cubemap volumes whose reach overlaps it, in their own order, so
	/// the shading is the same.
	/// </summary>
	private void BuildGrid()
	{
		var attributes = _targets;
		attributes.Set( "CS2GridSize", Vector4.Zero );

		List<BBox?>[] lists =
		[
			ProbeVolumes.Select( x => ReachOfBox( x.WorldToBox0, x.WorldToBox1, x.WorldToBox2, x.BoxMin, x.BoxMax ) ).ToList(),
			Lights.Select( LightReach ).ToList(),
			CubemapVolumes.Select( CubemapReach ).ToList(),
		];

		BBox? union = null;
		foreach ( var reach in lists.SelectMany( x => x ) )
		{
			if ( reach is { } box )
				union = union?.AddBBox( box ) ?? box;
		}

		// Nothing with a limited reach: visiting everything is as good.
		if ( union is not { } grid )
			return;

		var size = grid.Size;
		var cellSize = MathF.Max( 256f, MathF.Max( size.x, MathF.Max( size.y, size.z ) ) / 128f );
		int nx, ny, nz;

		while ( true )
		{
			nx = Math.Max( 1, (int)MathF.Ceiling( size.x / cellSize ) );
			ny = Math.Max( 1, (int)MathF.Ceiling( size.y / cellSize ) );
			nz = Math.Max( 1, (int)MathF.Ceiling( size.z / cellSize ) );
			if ( (long)nx * ny * nz <= MaxGridCells )
				break;

			cellSize *= 1.25f;
		}

		var cells = nx * ny * nz;

		void ForEachCell( BBox? reach, Action<int> action )
		{
			if ( reach is not { } box )
			{
				for ( var cell = 0; cell < cells; cell++ )
					action( cell );

				return;
			}

			int Clamp( float value, int count ) => Math.Clamp( (int)MathF.Floor( value / cellSize ), 0, count - 1 );

			var min = box.Mins - grid.Mins;
			var max = box.Maxs - grid.Mins;

			for ( var z = Clamp( min.z, nz ); z <= Clamp( max.z, nz ); z++ )
			for ( var y = Clamp( min.y, ny ); y <= Clamp( max.y, ny ); y++ )
			for ( var x = Clamp( min.x, nx ); x <= Clamp( max.x, nx ); x++ )
				action( (z * ny + y) * nx + x );
		}

		// Count, then lay the lists out after the cells' headers (first and count of each list), and fill them.
		var counts = new int[cells * GridLists];
		for ( var list = 0; list < GridLists; list++ )
		{
			var index = list;
			foreach ( var reach in lists[list] )
				ForEachCell( reach, cell => counts[cell * GridLists + index]++ );
		}

		var header = GridLists * 2;
		var data = new uint[cells * header + counts.Sum()];
		var cursors = new int[cells * GridLists];
		var next = cells * header;

		for ( var cell = 0; cell < cells; cell++ )
		{
			for ( var list = 0; list < GridLists; list++ )
			{
				data[cell * header + list * 2] = (uint)next;
				data[cell * header + list * 2 + 1] = (uint)counts[cell * GridLists + list];
				cursors[cell * GridLists + list] = next;
				next += counts[cell * GridLists + list];
			}
		}

		for ( var list = 0; list < GridLists; list++ )
		{
			for ( var item = 0; item < lists[list].Count; item++ )
			{
				var index = list;
				var value = (uint)item;
				ForEachCell( lists[list][item], cell => data[cursors[cell * GridLists + index]++] = value );
			}
		}

		_grid = new GpuBuffer<uint>( data.Length, debugName: "CS2Grid" );
		_grid.SetData( data.AsSpan() );

		attributes.Set( "CS2Grid", _grid );
		attributes.Set( "CS2GridOrigin", new Vector4( grid.Mins, 1f / cellSize ) );
		attributes.Set( "CS2GridSize", new Vector4( nx, ny, nz, 1 ) );
	}

	/// <summary>
	/// Where a cubemap volume claims the camera: its box, or the box around its sphere.
	/// </summary>
	private static BBox? CubemapReach( CS2CubemapVolume volume )
	{
		if ( volume.BoxMax.w != 0 )
			return BBox.FromPositionAndSize( (Vector3)volume.Center, volume.BoxMax.x * 2 + 1 );

		return ReachOfBox( volume.WorldToLocal0, volume.WorldToLocal1, volume.WorldToLocal2, volume.BoxMin, volume.BoxMax );
	}

	/// <summary>
	/// Where a light can light: the box it's limited to, -1 to 1 through its illumination transform.
	/// </summary>
	private static BBox? LightReach( CS2BarnLight light )
		=> ReachOfBox( light.Illumination0, light.Illumination1, light.Illumination2, new Vector3( -1 ), new Vector3( 1 ) );

	/// <summary>
	/// The map space box around a box given in a local space by the rows of the map to local transform, or null
	/// when the transform can't be inverted.
	/// </summary>
	private static BBox? ReachOfBox( Vector4 row0, Vector4 row1, Vector4 row2, Vector3 min, Vector3 max )
	{
		var matrix = new System.Numerics.Matrix4x4(
			row0.x, row1.x, row2.x, 0,
			row0.y, row1.y, row2.y, 0,
			row0.z, row1.z, row2.z, 0,
			row0.w, row1.w, row2.w, 1 );

		if ( !System.Numerics.Matrix4x4.Invert( matrix, out var inverse ) )
			return null;

		var reachMin = new Vector3( float.MaxValue );
		var reachMax = new Vector3( float.MinValue );

		for ( var corner = 0; corner < 8; corner++ )
		{
			var local = new System.Numerics.Vector3(
				(corner & 1) != 0 ? max.x : min.x,
				(corner & 2) != 0 ? max.y : min.y,
				(corner & 4) != 0 ? max.z : min.z );

			Vector3 point = System.Numerics.Vector3.Transform( local, inverse );
			reachMin = Vector3.Min( reachMin, point );
			reachMax = Vector3.Max( reachMax, point );
		}

		if ( !float.IsFinite( reachMin.x + reachMin.y + reachMin.z + reachMax.x + reachMax.y + reachMax.z ) )
			return null;

		// A little margin for rounding.
		return new BBox( reachMin - 1, reachMax + 1 );
	}

	/// <summary>
	/// A light's cookie is a texture slot in its colour's w, plus one, negative for a spherical cookie. The shaders
	/// take the texture's bindless index there instead.
	/// </summary>
	private CS2BarnLight WithCookie( CS2BarnLight light )
	{
		var color = light.Color;
		if ( color.w == 0 )
			return light;

		var index = TextureIndex( (int)MathF.Abs( color.w ) - 1 );
		light.Color = new Vector4( color.x, color.y, color.z, index == 0 ? 0 : MathF.Sign( color.w ) * index );

		// Without its texture the light keeps a plain barn shape.
		if ( index == 0 )
			light.Shape = new Vector4( 1, 1, 0, 0 );

		return light;
	}

	private static Texture AcquireTexture( string path )
	{
		if ( string.IsNullOrEmpty( path ) )
			return null;

		if ( LoadedTextures.TryGetValue( path, out var loaded ) && loaded.Texture is { IsValid: true } )
		{
			LoadedTextures[path] = (loaded.Texture, loaded.Users + 1);
			return loaded.Texture;
		}

		var texture = Texture.Load( path, false );
		LoadedTextures[path] = (texture, 1);
		return texture;
	}

	private static void ReleaseTexture( string path )
	{
		if ( string.IsNullOrEmpty( path ) || !LoadedTextures.TryGetValue( path, out var loaded ) )
			return;

		LoadedTextures[path] = (loaded.Texture, loaded.Users - 1);
		if ( loaded.Users <= 1 )
			Retired.Add( (0, path, null) );
	}

	private static void Retire( GpuBuffer buffer )
	{
		if ( buffer is not null )
			Retired.Add( (0, null, buffer) );
	}

	/// <summary>
	/// Lets go of what has been retired long enough: textures nothing took up again in the meantime, and buffers.
	/// </summary>
	private static void ReleaseRetired()
	{
		for ( var i = Retired.Count - 1; i >= 0; i-- )
		{
			var (retired, path, buffer) = Retired[i];
			if ( retired < RetirementSeconds )
				continue;

			Retired.RemoveAt( i );
			buffer?.Dispose();

			if ( path is not null && LoadedTextures.TryGetValue( path, out var loaded ) && loaded.Users <= 0 )
				LoadedTextures.Remove( path );
		}
	}

	private int TextureIndex( int slot )
	{
		return slot >= 0 && slot < _textures.Count && _textures[slot] is { IsValid: true } texture ? texture.Index : 0;
	}

	private static GpuBuffer<T> Upload<T>( string name, List<T> items ) where T : unmanaged
	{
		// An empty buffer still has to exist for the shaders to bind.
		var buffer = new GpuBuffer<T>( Math.Max( items.Count, 1 ), debugName: name );
		if ( items.Count > 0 )
			buffer.SetData( items );

		return buffer;
	}

	/// <summary>
	/// Render attributes set together: the scene's, or a scoped lighting's renderers'.
	/// </summary>
	private sealed class AttributeTargets( List<RenderAttributes> targets )
	{
		public void Set( string name, int value ) => targets.ForEach( x => x.Set( name, value ) );
		public void Set( string name, float value ) => targets.ForEach( x => x.Set( name, value ) );
		public void Set( string name, Vector2 value ) => targets.ForEach( x => x.Set( name, value ) );
		public void Set( string name, Vector3 value ) => targets.ForEach( x => x.Set( name, value ) );
		public void Set( string name, Vector4 value ) => targets.ForEach( x => x.Set( name, value ) );
		public void Set( string name, GpuBuffer value ) => targets.ForEach( x => x.Set( name, value ) );
	}
}

/// <summary>
/// A light probe volume (env_combined_light_probe_volume, env_light_probe_volume): an ambient cube and the baked
/// shadows of the stationary lights per voxel, in a part of the map's probe atlas.
/// </summary>
public struct CS2ProbeVolume
{
	/// <summary>
	/// Rows of the transform from world space to the volume's own, where its box is.
	/// </summary>
	public Vector4 WorldToBox0 { get; set; }
	public Vector4 WorldToBox1 { get; set; }
	public Vector4 WorldToBox2 { get; set; }

	public Vector3 BoxMin { get; set; }
	public Vector3 BoxMax { get; set; }

	/// <summary>
	/// Inverse widths of the fade at the box's edges, per axis.
	/// </summary>
	public Vector3 Fade { get; set; }

	/// <summary>
	/// Rows of the transform from world space to 0-1 across the volume.
	/// </summary>
	public Vector4 WorldToAtlas0 { get; set; }
	public Vector4 WorldToAtlas1 { get; set; }
	public Vector4 WorldToAtlas2 { get; set; }

	/// <summary>
	/// Where samples are clamped to, so they don't blend with the neighbouring volumes in the atlas.
	/// </summary>
	public Vector3 BorderMin { get; set; }
	public Vector3 BorderMax { get; set; }

	public Vector3 AtlasScale { get; set; }
	public Vector3 AtlasOffset { get; set; }

	/// <summary>
	/// Multiplies the volume's irradiance.
	/// </summary>
	public Vector3 Tint { get; set; }

	/// <summary>
	/// Indices in <see cref="CS2MapLighting.Textures"/>: the ambient cubes, and the direct light shadows.
	/// </summary>
	public int Irradiance { get; set; }
	public int Shadows { get; set; }

	[StructLayout( LayoutKind.Sequential )]
	internal struct Gpu
	{
		public Vector4 WorldToBox0;
		public Vector4 WorldToBox1;
		public Vector4 WorldToBox2;
		public Vector4 BoxMin;
		public Vector4 BoxMax;
		public Vector4 Fade;
		public Vector4 WorldToAtlas0;
		public Vector4 WorldToAtlas1;
		public Vector4 WorldToAtlas2;
		public Vector4 BorderMin;
		public Vector4 BorderMax;
		public Vector4 AtlasScale;
		public Vector4 AtlasOffset;
		public Vector4 Tint;
	}

	internal Gpu ToGpu( Func<int, int> textureIndex ) => new()
	{
		WorldToBox0 = WorldToBox0,
		WorldToBox1 = WorldToBox1,
		WorldToBox2 = WorldToBox2,
		BoxMin = new Vector4( BoxMin, 0 ),
		BoxMax = new Vector4( BoxMax, 0 ),
		Fade = new Vector4( Fade, 0 ),
		WorldToAtlas0 = WorldToAtlas0,
		WorldToAtlas1 = WorldToAtlas1,
		WorldToAtlas2 = WorldToAtlas2,
		BorderMin = new Vector4( BorderMin, textureIndex( Irradiance ) ),
		BorderMax = new Vector4( BorderMax, textureIndex( Shadows ) ),
		AtlasScale = new Vector4( AtlasScale, 0 ),
		AtlasOffset = new Vector4( AtlasOffset, 0 ),
		Tint = new Vector4( Tint, 0 ),
	};
}

/// <summary>
/// A cubemap volume (env_combined_light_probe_volume, env_cubemap_box, env_cubemap), laid out for the shaders.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct CS2CubemapVolume
{
	/// <summary>
	/// Rows of the volume's world to local transform.
	/// </summary>
	public Vector4 WorldToLocal0 { get; set; }
	public Vector4 WorldToLocal1 { get; set; }
	public Vector4 WorldToLocal2 { get; set; }

	/// <summary>
	/// The box in local space; w is the cube's slice of the cube array.
	/// </summary>
	public Vector4 BoxMin { get; set; }

	/// <summary>
	/// w is 1 for a sphere, whose radius is x.
	/// </summary>
	public Vector4 BoxMax { get; set; }

	/// <summary>
	/// The volume's origin; w is its indoor level, the higher the more it claims the cameras inside it.
	/// </summary>
	public Vector4 Center { get; set; }
}

/// <summary>
/// How a cubemap volume blends with the others at the high shader quality, laid out for the shaders.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct CS2CubemapBlend
{
	/// <summary>
	/// One over the widths of the fade at the volume's edges, per axis (its edge_fade_dists, plus 0.02).
	/// </summary>
	public Vector4 InvEdgeWidth { get; set; }

	/// <summary>
	/// Multiplies the reflection; 1, as nothing in the map data sets it (unverified).
	/// </summary>
	public Vector4 Tint { get; set; }

	/// <summary>
	/// The cube's radiance reduced to luma in its constant and linear bands, ready to dot with (normal, 1): how bright
	/// the reflection is relative to the probes' light where the cube was baked. Positive along every normal, as the
	/// game keeps it (the reflection goes negative otherwise).
	/// </summary>
	public Vector4 NormalizationSH { get; set; }
}

/// <summary>
/// One face of a light2 light (light_barn, light_omni2, light_rect), laid out the way cs2_lighting.hlsl takes it.
/// </summary>
[StructLayout( LayoutKind.Sequential )]
public struct CS2BarnLight
{
	/// <summary>
	/// Rows of the world to light clip space projection: inside the light is -1 to 1 across and 0 to 1 deep.
	/// </summary>
	public Vector4 Frustum0 { get; set; }
	public Vector4 Frustum1 { get; set; }
	public Vector4 Frustum2 { get; set; }
	public Vector4 Frustum3 { get; set; }

	/// <summary>
	/// Rows of the transform into the box the light is limited to, where inside is -1 to 1.
	/// </summary>
	public Vector4 Illumination0 { get; set; }
	public Vector4 Illumination1 { get; set; }
	public Vector4 Illumination2 { get; set; }

	/// <summary>
	/// The point the light falls off from, with the squared distance it is at full brightness in w. A w of zero
	/// makes xyz the direction of a light with parallel rays.
	/// </summary>
	public Vector4 Position { get; set; }

	/// <summary>
	/// Distance fade bias and scale, and the near and far skirts.
	/// </summary>
	public Vector4 DistanceFade { get; set; }

	/// <summary>
	/// Linear colour times intensity; w selects a cookie, 0 for none.
	/// </summary>
	public Vector4 Color { get; set; }

	public Vector4 Orientation { get; set; }

	/// <summary>
	/// Cone fade bias and scale, and in z the half length of the luminaire.
	/// </summary>
	public Vector4 AngleFade { get; set; }

	/// <summary>
	/// The softness and roundness of a barn light's shape, or the cookie's offset and scale.
	/// </summary>
	public Vector4 Shape { get; set; }

	/// <summary>
	/// Which channel of the baked shadows is this light's.
	/// </summary>
	public Vector4 BakedShadowMask { get; set; }

	/// <summary>
	/// Minimum roughness; the rest is unused.
	/// </summary>
	public Vector4 Parameters { get; set; }
}
