using CS2;
using System;
using Numerics = System.Numerics;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// The map's lighting, laid out for the CS2 shaders (cs2_lighting.hlsl), which light as the game's forward shaders
/// do: csgo_vertexlitgeneric's lightmap and light probe variants, decompiled. Light faces, volumes and their constants
/// follow the Deadlock mount and ValveResourceFormat's renderer where the game's engine code wasn't read.
/// </summary>
internal sealed partial class MapLoader
{
	/// <summary>
	/// How far in front of its luminaire a light2 light is as bright as its brightness says.
	/// </summary>
	private const float LinearBrightnessDistance = 100f;

	/// <summary>
	/// Volumes reach this much past their box, so faces lying on it are inside.
	/// </summary>
	private const float VolumeBoundsExtend = 0.02f;

	/// <summary>
	/// The lightmap layout the shaders read: version 8 lightmaps of the CS2 game version (irradiance and the direct
	/// light shadows).
	/// </summary>
	private const int LightmapVersion = 8;
	private const int LightmapGameVersion = 2;

	private void BuildLighting( CompiledWorld world, IReadOnlyList<MapEntity> entities, GameObject parent )
	{
		var gameObject = new GameObject( true, "Lighting" );
		if ( parent is not null )
			gameObject.SetParent( parent, false );

		var lighting = gameObject.Components.Create<CS2MapLighting>();
		lighting.Scoped = _skybox;
		var textures = new List<string>();

		int AddMountTexture( string path )
		{
			var mountPath = $"mount://{Host.Ident}/{path}";
			var index = textures.IndexOf( mountPath );
			if ( index >= 0 )
				return index;

			textures.Add( mountPath );
			return textures.Count - 1;
		}

		int AddTexture( string path )
		{
			if ( string.IsNullOrWhiteSpace( path ) )
				return -1;

			path = CompiledWorld.NormalizePath( path );
			return Host.FileExists( path + "_c" ) ? AddMountTexture( path ) : -1;
		}

		// A lightmap with mips below its top one is taken in two parts the mount serves (CS2Mount.LightmapPart),
		// which the shaders filter across as one.
		(int Top, int Lower) AddLightmap( string path )
		{
			path = CompiledWorld.NormalizePath( path );
			if ( !Host.FileExists( path + "_c" ) )
				return (-1, -1);

			if ( Host.ReadTextureHeader( path ).MipCount <= 1 )
				return (AddMountTexture( path ), -1);

			return (AddMountTexture( CS2Mount.LightmapPart( path, true ) ), AddMountTexture( CS2Mount.LightmapPart( path, false ) ));
		}

		if ( world.LightingInfo is { } info && info.GetBool( "m_bHasLightmaps" ) )
		{
			var version = info.GetInt32( "m_nLightmapVersionNumber" );
			var gameVersion = info.GetInt32( "m_nLightmapGameVersionNumber" );
			if ( version != LightmapVersion || gameVersion != LightmapGameVersion )
				Log.Warning( $"{mapName}: lightmap version {version} (game version {gameVersion}); the CS2 shaders read version {LightmapVersion} (game version {LightmapGameVersion})." );

			if ( info.GetBool( "m_bBakedShadowsGamma20" ) )
				Log.Warning( $"{mapName}: the baked shadows are stored gamma 2.0, which the CS2 shaders don't decode yet." );

			foreach ( var path in info.GetStringArray( "m_lightMaps" ) )
			{
				switch ( System.IO.Path.GetFileNameWithoutExtension( path ) )
				{
					case "irradiance":
						(lighting.LightmapIrradiance, lighting.LightmapIrradianceLowerMips) = AddLightmap( path );
						break;

					case "direct_light_shadows":
						(lighting.LightmapShadows, lighting.LightmapShadowsLowerMips) = AddLightmap( path );
						break;

					case "directional_irradiance":
						(lighting.LightmapDirectional, lighting.LightmapDirectionalLowerMips) = AddLightmap( path );
						break;
				}
			}

			var uvScale = info.GetFloatArray( "m_vLightmapUvScale" );
			if ( uvScale.Length >= 2 )
				lighting.LightmapUvScale = new Vector2( uvScale[0], uvScale[1] );
		}

		AddProbeVolumes( lighting, entities, AddTexture );
		AddCubemapVolumes( lighting, entities, AddTexture );
		AddSunLighting( lighting, entities );

		lighting.Lights = [];
		foreach ( var entity in entities )
		{
			if ( entity.Classname.ToLowerInvariant() is "light_barn" or "light_omni2" or "light_rect" && IsRealtime( entity ) && entity.GetBool( "enabled", true ) )
				lighting.Lights.AddRange( ComputeLightFaces( entity, AddTexture ) );
		}

		AddFog( lighting, entities, AddTexture );
		lighting.Textures = textures;
	}

	/// <summary>
	/// The light probe volumes with a probe atlas, most indoor first, then the smaller first (ValveResourceFormat's
	/// order; the game's isn't known), as earlier volumes claim a point before later ones.
	/// </summary>
	private void AddProbeVolumes( CS2MapLighting lighting, IReadOnlyList<MapEntity> entities, Func<string, int> addTexture )
	{
		var probes = new List<(int Indoor, float Size, CS2ProbeVolume Volume)>();
		var atlases = new Dictionary<string, CompiledTexture>( StringComparer.OrdinalIgnoreCase );

		foreach ( var entity in entities )
		{
			if ( entity.Classname.ToLowerInvariant() is not ("env_combined_light_probe_volume" or "env_light_probe_volume") )
				continue;

			if ( entity.GetBool( "StartDisabled" ) || !entity.Has( "light_probe_atlas_x" ) )
				continue;

			var irradiance = addTexture( entity.GetString( "lightprobetexture" ) );
			var shadowsPath = entity.GetString( "lightprobetexture_dlshd" );
			var shadows = addTexture( shadowsPath );
			if ( irradiance < 0 || shadows < 0 )
				continue;

			shadowsPath = CompiledWorld.NormalizePath( shadowsPath );
			if ( !atlases.TryGetValue( shadowsPath, out var atlas ) )
				atlases[shadowsPath] = atlas = Host.ReadTextureHeader( shadowsPath );

			var transform = ReadTransform( entity );
			var origin = transform.Position;
			Vector3[] axes = [transform.Rotation.Forward, transform.Rotation.Left, transform.Rotation.Up];

			var mins = ReadVector( entity, "box_mins", -64 );
			var maxs = ReadVector( entity, "box_maxs", 64 );
			var boxSize = maxs - mins;
			var fade = ReadVector( entity, "edge_fade_dists", 0 );

			// The atlas is laid out in the shadows texture's voxels; the irradiance stacks six of it in depth.
			var dims = new Vector3( atlas.Width, atlas.Height, atlas.Depth );
			var size = ReadVector( entity, "light_probe_size", 1, "x", "y", "z" );
			var offset = ReadVector( entity, "light_probe_atlas", 0, "x", "y", "z" );

			probes.Add( (entity.GetInt( "indoor_outdoor_level" ), size.LengthSquared, new CS2ProbeVolume
			{
				WorldToBox0 = new Vector4( axes[0], -Vector3.Dot( axes[0], origin ) ),
				WorldToBox1 = new Vector4( axes[1], -Vector3.Dot( axes[1], origin ) ),
				WorldToBox2 = new Vector4( axes[2], -Vector3.Dot( axes[2], origin ) ),
				BoxMin = mins - VolumeBoundsExtend,
				BoxMax = maxs + VolumeBoundsExtend,
				Fade = new Vector3( 1 / (fade.x + VolumeBoundsExtend), 1 / (fade.y + VolumeBoundsExtend), 1 / (fade.z + VolumeBoundsExtend) ),

				// World space to 0-1 across the box.
				WorldToAtlas0 = new Vector4( axes[0] / boxSize.x, (-Vector3.Dot( axes[0], origin ) - mins.x) / boxSize.x ),
				WorldToAtlas1 = new Vector4( axes[1] / boxSize.y, (-Vector3.Dot( axes[1], origin ) - mins.y) / boxSize.y ),
				WorldToAtlas2 = new Vector4( axes[2] / boxSize.z, (-Vector3.Dot( axes[2], origin ) - mins.z) / boxSize.z ),

				// Half a voxel in from the edges, depth in sixths like the ambient cube lookups.
				BorderMin = new Vector3( 0.5f / size.x, 0.5f / size.y, 0.5f / size.z / 6f ),
				BorderMax = new Vector3( 1 - 0.5f / size.x, 1 - 0.5f / size.y, (1 - 0.5f / size.z) / 6f ),
				AtlasScale = size / dims,
				AtlasOffset = offset / dims,
				Tint = Vector3.One,
				Irradiance = irradiance,
				Shadows = shadows,
			}) );
		}

		lighting.ProbeVolumes = probes.OrderByDescending( x => x.Indoor ).ThenBy( x => x.Size ).Select( x => x.Volume ).ToList();
	}

	/// <summary>
	/// The cubemap volumes of the map's cube array, most indoor first, then by slice (ValveResourceFormat's order; the
	/// game's isn't known). The low shader quality reflects a single cube per view (slice 0 of a per view cube array,
	/// in the decompiled shaders), picked by the camera's place among these volumes; the high one blends the volumes
	/// around each point, with what <see cref="CS2CubemapBlend"/> holds for each.
	/// </summary>
	private void AddCubemapVolumes( CS2MapLighting lighting, IReadOnlyList<MapEntity> entities, Func<string, int> addTexture )
	{
		string environmentMaps = null;
		float[] radiance = null;
		var volumes = new List<(int Indoor, int Slice, CS2CubemapVolume Volume, CS2CubemapBlend Blend)>();

		foreach ( var entity in entities )
		{
			var classname = entity.Classname.ToLowerInvariant();
			if ( classname is not ("env_combined_light_probe_volume" or "env_cubemap_box" or "env_cubemap") )
				continue;

			if ( entity.GetBool( "StartDisabled" ) )
				continue;

			// A custom cubemap is a texture of the mapper's choosing, not part of the baked array.
			var cubemapPath = entity.GetString( "cubemaptexture" );
			if ( string.IsNullOrEmpty( cubemapPath ) || !string.IsNullOrEmpty( entity.GetString( "customcubemaptexture" ) ) )
				continue;

			cubemapPath = CompiledWorld.NormalizePath( cubemapPath );
			if ( !Host.FileExists( cubemapPath + "_c" ) )
				continue;

			if ( environmentMaps is null )
			{
				var cubemap = Host.ReadTextureHeader( cubemapPath );
				if ( !cubemap.IsCube )
					continue;

				environmentMaps = cubemapPath;
				radiance = cubemap.RadianceCoefficients;
				lighting.EnvironmentMaps = addTexture( cubemapPath );
				lighting.EnvironmentMapMips = cubemap.MipCount;
			}
			else if ( !environmentMaps.Equals( cubemapPath, StringComparison.OrdinalIgnoreCase ) )
			{
				Log.Warning( $"{mapName}: a cubemap volume reflects '{cubemapPath}', but the map's cubemaps are in '{environmentMaps}'." );
				continue;
			}

			var transform = ReadTransform( entity );
			var origin = transform.Position;
			Vector3[] axes = [transform.Rotation.Forward, transform.Rotation.Left, transform.Rotation.Up];
			var isSphere = classname == "env_cubemap";
			var slice = entity.GetInt( "array_index" );

			Vector3 mins, maxs;
			if ( isSphere )
			{
				var radius = entity.GetFloat( "influenceradius", 512 );
				mins = -radius;
				maxs = radius;
			}
			else
			{
				mins = ReadVector( entity, "box_mins", -64 );
				maxs = ReadVector( entity, "box_maxs", 64 );
			}

			var indoor = entity.GetInt( "indoor_outdoor_level" );
			var fade = ReadVector( entity, "edge_fade_dists", 0 );

			var volume = new CS2CubemapVolume
			{
				WorldToLocal0 = new Vector4( axes[0], -Vector3.Dot( axes[0], origin ) ),
				WorldToLocal1 = new Vector4( axes[1], -Vector3.Dot( axes[1], origin ) ),
				WorldToLocal2 = new Vector4( axes[2], -Vector3.Dot( axes[2], origin ) ),
				BoxMin = new Vector4( mins, slice ),
				BoxMax = new Vector4( maxs, isSphere ? 1 : 0 ),
				Center = new Vector4( origin, indoor ),
			};

			// Spheres have no edge fade distances, so their edge is the near hard one 0.02 gives.
			var blend = new CS2CubemapBlend
			{
				InvEdgeWidth = new Vector4( 1 / (fade.x + VolumeBoundsExtend), 1 / (fade.y + VolumeBoundsExtend), 1 / (fade.z + VolumeBoundsExtend), 0 ),
				Tint = Vector4.One,
				NormalizationSH = NormalizationSH( radiance, slice, axes ),
			};

			volumes.Add( (indoor, slice, volume, blend) );
		}

		var ordered = volumes.OrderByDescending( x => x.Indoor ).ThenBy( x => x.Slice ).ToList();
		lighting.CubemapVolumes = ordered.Select( x => x.Volume ).ToList();
		lighting.CubemapBlends = ordered.Select( x => x.Blend ).ToList();
	}

	/// <summary>
	/// One cube's baked radiance (second order harmonics, 27 floats per cube, channels planar) reduced to its constant
	/// and linear bands' luma (times the tint, 1), folded with the basis and cosine lobe constants so the shaders dot it
	/// with (normal, 1), as client.dll builds it (sub_180231270 in the build of 2026-10-21): the linear band, stored
	/// y, z, x with the Condon-Shortley sign, is ( -x, -y, z ) in the cube's space, turned by its rotation; and it is
	/// kept positive along every normal, its linear part no longer than 0.99 of its constant, (0, 0, 0, 0.01) when the
	/// constant isn't positive. The same when the cube has no coefficients (no game equivalent).
	/// </summary>
	private static Vector4 NormalizationSH( float[] radiance, int slice, Vector3[] axes )
	{
		const int perChannel = 9;
		const int perCube = perChannel * 3;
		const float constantBand = 0.282095f;
		const float linearBand = 0.325735f;

		var offset = slice * perCube;
		if ( radiance is null || slice < 0 || offset + perCube > radiance.Length )
			return new Vector4( 0, 0, 0, 0.01f );

		var luma = new float[4];
		for ( var band = 0; band < 4; band++ )
		{
			var rgb = new Vector3( radiance[offset + band], radiance[offset + perChannel + band], radiance[offset + perChannel * 2 + band] );
			luma[band] = Vector3.Dot( rgb, new Vector3( 0.2126f, 0.7152f, 0.0722f ) );
		}

		var constant = luma[0] * constantBand;
		if ( constant <= 0 )
			return new Vector4( 0, 0, 0, 0.01f );

		var linear = (axes[0] * -luma[3] + axes[1] * -luma[1] + axes[2] * luma[2]) * linearBand;
		var longest = constant * 0.99f;
		if ( linear.Length > longest )
			linear *= longest / linear.Length;

		return new Vector4( linear, constant );
	}

	/// <summary>
	/// The sun, and the sky light that lights probe lit surfaces outside every probe volume.
	/// </summary>
	private static void AddSunLighting( CS2MapLighting lighting, IReadOnlyList<MapEntity> entities )
	{
		var sun = entities.FirstOrDefault( x => x.Classname.Equals( "light_environment", StringComparison.OrdinalIgnoreCase ) );
		if ( sun is null )
			return;

		var forward = ReadTransform( sun ).Rotation.Forward;
		var color = SrgbToLinear( ReadColor( sun, "color", [255, 255, 255] ) ) * sun.GetFloat( "brightness", 1 ) * sun.GetFloat( "brightnessscale", 1 );

		if ( IsRealtime( sun ) && sun.GetBool( "enabled", true ) )
		{
			lighting.SunDirection = -forward;
			lighting.SunColor = color;
			lighting.SunShadowMask = BakedShadowMask( sun );
			lighting.SunMinRoughness = sun.GetFloat( "minroughness" );
		}

		// Sky from above, and the sun and sky bouncing off the ground from below, as the Deadlock mount lights what
		// its volumes leave uncovered (unverified for CS2, whose maps' probe volumes cover their play space).
		var sky = SrgbToLinear( ReadColor( sun, "skycolor", [255, 255, 255] ) ) * sun.GetFloat( "skyintensity", 1 );
		var bounce = SrgbToLinear( ReadColor( sun, "skyambientbounce", [0, 0, 0] ) );
		var sunDown = forward.z;
		var sunBounce = color * bounce * 0.5f;
		var skyBounce = sky * bounce * 0.5f;

		var vertical = sky * 0.5f - skyBounce + sunBounce * sunDown;
		var constant = sky * 0.5f + skyBounce + sunBounce * MathF.Abs( sunDown );

		lighting.AmbientRed = new Vector4( 0, 0, vertical.x, constant.x );
		lighting.AmbientGreen = new Vector4( 0, 0, vertical.y, constant.y );
		lighting.AmbientBlue = new Vector4( 0, 0, vertical.z, constant.z );
	}

	/// <summary>
	/// The channel of the lightmap shadows that holds a stationary light, or nothing for one that bakes none.
	/// </summary>
	private static Vector4 BakedShadowMask( MapEntity entity ) => entity.GetInt( "bakedshadowindex", entity.GetInt( "bakelightindex", -1 ) ) switch
	{
		0 => new Vector4( 1, 0, 0, 0 ),
		1 => new Vector4( 0, 1, 0, 0 ),
		2 => new Vector4( 0, 0, 1, 0 ),
		3 => new Vector4( 0, 0, 0, 1 ),
		_ => Vector4.Zero,
	};

	/// <summary>
	/// A light2 light as the faces the game lights with: one frustum for a barn light, one to six for an omni or
	/// rect light depending on its cone, or a single box around everything an unshadowed one reaches.
	/// </summary>
	/// <remarks>
	/// The game renders shadow maps for these lights each frame, which the port doesn't (yet): they are shadowed by
	/// their baked shadow channel only, which covers the world but not props or anything that moves.
	/// </remarks>
	private List<CS2BarnLight> ComputeLightFaces( MapEntity entity, Func<string, int> addTexture )
	{
		var classname = entity.Classname.ToLowerInvariant();
		var range = entity.GetFloat( "range", 512 );
		if ( range <= 0.0001f )
			return [];

		var transform = ReadTransform( entity );
		var rotation = transform.Rotation;
		var origin = transform.Position;
		var forward = rotation.Forward;
		var left = rotation.Left;
		var up = rotation.Up;

		var color = SrgbToLinear( entity.GetInt( "colormode" ) != 0 ? ColorTemperature( entity.GetFloat( "colortemperature", 6600 ) ) : ReadColor( entity, "color", [255, 255, 255] ) );
		var intensity = MathF.Pow( 2, entity.GetFloat( "brightness" ) ) * entity.GetFloat( "brightnessscale", 1 );
		var castShadows = entity.GetInt( "castshadows", 1 );
		var skirt = entity.GetFloat( "skirt", 0.1f );
		var sizeParams = ReadVector( entity, "size_params", 0 );
		var orientation = new Vector4( rotation.x, rotation.y, rotation.z, rotation.w );
		var bakedShadowMask = BakedShadowMask( entity );
		var parameters = new Vector4( entity.GetFloat( "minroughness" ), 0, 0, 0 );

		var (illumination, _) = LightObb( entity, castShadows, -1 );

		if ( classname == "light_barn" )
		{
			var softX = entity.GetFloat( "soft_x", 0.25f );
			var softY = entity.GetFloat( "soft_y", 0.25f );
			var skirtNear = entity.GetFloat( "skirt_near", 0.05f );
			var shape = entity.GetFloat( "shape" );
			var shear = entity.GetVector( "shear", 2, [0, 0] );

			// ValveResourceFormat's "right" for barn lights: up cross forward.
			var right = Vector3.Cross( up, forward ).Normal;

			Numerics.Matrix4x4 worldToFrustum;
			Vector4 position;
			float skirtNearScale, skirtFarScale;

			if ( sizeParams.z <= 0 )
			{
				// Parallel rays through a box.
				var eye = origin;
				var view = Numerics.Matrix4x4.CreateLookAtLeftHanded( eye, eye + forward, up );
				var shearMatrix = Numerics.Matrix4x4.Identity;
				shearMatrix.M31 = -shear[0] / range;
				shearMatrix.M32 = -shear[1] / range;
				var projection = Numerics.Matrix4x4.CreateOrthographicOffCenterLeftHanded( -sizeParams.x, sizeParams.x, -sizeParams.y, sizeParams.y, 0, range );

				worldToFrustum = view * shearMatrix * projection;
				skirtNearScale = skirtNear > 0 ? 1 / skirtNear : 0;
				skirtFarScale = skirt > 0 ? 1 / skirt : 0;

				var direction = (range * forward + shear[0] * right + shear[1] * up).Normal;
				position = new Vector4( -direction, 0 );
			}
			else
			{
				var near = 1 / sizeParams.z;
				var far = near + range;
				var centerX = shear[0] / range * near;
				var centerY = shear[1] / range * near;
				var eye = origin - near * forward - centerX * right - centerY * up;

				var projection = Numerics.Matrix4x4.CreatePerspectiveOffCenterLeftHanded( centerX - sizeParams.x, centerX + sizeParams.x, centerY - sizeParams.y, centerY + sizeParams.y, near, far );
				var view = Numerics.Matrix4x4.CreateLookAtLeftHanded( eye, eye + forward, up );
				worldToFrustum = view * projection;

				skirtNearScale = skirtNear > 0 ? (near + skirtNear * range) / (far * skirtNear) : 0;
				skirtFarScale = skirt > 0 ? (near + (1 - skirt) * range) / (near * skirt) : 0;

				position = new Vector4( eye, centerX * centerX + centerY * centerY + near * near );
			}

			var falloffDistSq = position.w;
			var scale = falloffDistSq > 0 ? (MathF.Sqrt( falloffDistSq ) + LinearBrightnessDistance) * (MathF.Sqrt( falloffDistSq ) + LinearBrightnessDistance) / falloffDistSq : 1;

			var cookie = addTexture( entity.GetString( "lightcookie" ) );
			var shapeParameters = cookie >= 0
				? new Vector4( 0, 0, 1, 1 )
				: new Vector4( 1 - softX, 1 - softY, shape > 0 ? MathF.Min( shape, 1 ) * 0.875f + 0.125f : 0, 0 );

			return
			[
				new CS2BarnLight
				{
					Frustum0 = Column( worldToFrustum, 0 ),
					Frustum1 = Column( worldToFrustum, 1 ),
					Frustum2 = Column( worldToFrustum, 2 ),
					Frustum3 = Column( worldToFrustum, 3 ),
					Illumination0 = illumination[0],
					Illumination1 = illumination[1],
					Illumination2 = illumination[2],
					Position = position,
					DistanceFade = new Vector4( 1, 0, skirtNearScale, skirtFarScale ),
					Color = new Vector4( color * intensity * scale, cookie >= 0 ? cookie + 1 : 0 ),
					Orientation = orientation,
					AngleFade = new Vector4( 1, 0, 0, 0 ),
					Shape = shapeParameters,
					BakedShadowMask = bakedShadowMask,
					Parameters = parameters,
				},
			];
		}

		// Omni and rect lights. Rect lights light the hemisphere in front of them.
		var outerAngle = classname == "light_rect" ? 90 : entity.GetFloat( "outer_angle", 180 );
		var innerAngle = classname == "light_rect" ? 0 : entity.GetFloat( "inner_angle" );
		var luminaireShape = classname == "light_rect" ? -1 : entity.GetInt( "shape" );
		var angle = MathF.Max( 1, MathF.Min( outerAngle, 180 ) ) * MathF.PI / 180;
		var heightOffset = luminaireShape is 1 or 2 ? sizeParams.y : 0;
		var sideRange = angle < MathF.PI / 2 ? MathF.Max( range - heightOffset, 0 ) * MathF.Sin( angle ) + heightOffset : range;

		var omniCookie = addTexture( entity.GetString( "lightcookie" ) );
		var (fadeOffset, fadeRate) = DistanceFade( range, skirt );
		var (angleBias, angleScale) = AngleFade( outerAngle, innerAngle );
		var luminaireLength = luminaireShape switch
		{
			1 or 2 => sizeParams.y,
			0 => entity.GetFloat( "luminaire_size" ),
			_ => 0,
		};

		var omniColor = new Vector4( color * intensity * LinearBrightnessDistance * LinearBrightnessDistance, omniCookie >= 0 ? -(omniCookie + 1) : 0 );

		CS2BarnLight Face( Numerics.Matrix4x4 worldToFrustum, Vector4[] faceIllumination ) => new()
		{
			Frustum0 = Column( worldToFrustum, 0 ),
			Frustum1 = Column( worldToFrustum, 1 ),
			Frustum2 = Column( worldToFrustum, 2 ),
			Frustum3 = Column( worldToFrustum, 3 ),
			Illumination0 = faceIllumination[0],
			Illumination1 = faceIllumination[1],
			Illumination2 = faceIllumination[2],
			Position = new Vector4( origin, 1 ),
			DistanceFade = new Vector4( fadeOffset, fadeRate, 0, 0 ),
			Color = omniColor,
			Orientation = orientation,
			AngleFade = new Vector4( angleBias, angleScale, luminaireLength, 0 ),
			Shape = omniCookie >= 0 ? new Vector4( 0, 0, 1, 1 ) : new Vector4( 1, 1, 0, 0 ),
			BakedShadowMask = bakedShadowMask,
			Parameters = parameters,
		};

		// The game splits shadowed omni lights into faces to render a shadow map each; with no shadow maps to render,
		// one box around everything the light reaches lights the same.
		var back = angle > MathF.PI / 2 ? -MathF.Cos( angle ) * range : 0;
		var boxView = Numerics.Matrix4x4.CreateLookAtLeftHanded( origin, origin + forward, up );
		var boxProjection = Numerics.Matrix4x4.CreateOrthographicOffCenterLeftHanded( -sideRange, sideRange, -sideRange, sideRange, -back, range );

		return [Face( boxView * boxProjection, illumination )];
	}

	/// <summary>
	/// The box the compiler found a light can reach, as rows of the transform into it (inside is -1 to 1). Face -1 is
	/// the whole light, other faces the omni light's sub frusta. Rows of zero accept everything.
	/// </summary>
	private static (Vector4[] Rows, bool Valid) LightObb( MapEntity entity, int castShadows, int face )
	{
		var rows = new Vector4[3];
		var stationary = entity.GetInt( "bakedshadowindex", -1 ) is >= 0 and <= 3;

		if ( entity.GetInt( "precomputedfieldsvalid" ) == 0 || !stationary && castShadows <= 0 )
			return (rows, false);

		var suffix = face >= 0 ? face.ToString( System.Globalization.CultureInfo.InvariantCulture ) : "";
		if ( face >= 0 && entity.GetInt( "precomputedsubfrusta" ) <= face )
			return (rows, false);

		var center = ReadVector( entity, $"precomputedobborigin{suffix}", 0 );
		var extent = ReadVector( entity, $"precomputedobbextent{suffix}", 0 );
		var angles = entity.GetVector( $"precomputedobbangles{suffix}", 3, [0, 0, 0] );
		if ( extent.x <= 0 || extent.y <= 0 || extent.z <= 0 )
			return (rows, false);

		var rotation = Rotation.From( angles[0], angles[1], angles[2] );
		Vector3[] axes = [rotation.Forward, rotation.Left, rotation.Up];

		// Shrunk a touch, so the box's own faces are inside.
		const float shrink = 0.999f;

		for ( var i = 0; i < 3; i++ )
		{
			var inverse = shrink / extent[i];
			rows[i] = new Vector4( axes[i] * inverse, -Vector3.Dot( center, axes[i] ) * inverse );
		}

		return (rows, true);
	}

	/// <summary>
	/// Distance fade of an omni light's skirt: saturate( offset + rate * distance ).
	/// </summary>
	private static (float Offset, float Rate) DistanceFade( float range, float skirt )
	{
		if ( skirt <= 0 )
			return (1, 0);

		if ( skirt >= 1 )
			return (1, -1 / range);

		return (1 / skirt, -1 / (range * skirt));
	}

	/// <summary>
	/// Cone fade: saturate( bias + scale * cos ), from full at the inner angle to nothing at the outer.
	/// </summary>
	private static (float Bias, float Scale) AngleFade( float outerAngle, float innerAngle )
	{
		if ( outerAngle >= 180 )
			return (1, 0);

		if ( outerAngle <= 0 )
			return (0, 0);

		var cosOuter = MathF.Min( MathF.Cos( outerAngle * MathF.PI / 180 ), 1 - 1e-7f );
		var cosInner = innerAngle > 0 ? MathF.Cos( Math.Clamp( innerAngle, 0, outerAngle - 0.001f ) * MathF.PI / 180 ) : 1;

		var denominator = cosOuter - cosInner;
		if ( denominator >= 0 )
			denominator = -1e-7f;

		return (cosOuter / denominator, 1 / denominator);
	}

	/// <summary>
	/// env_gradient_fog and env_cubemap_fog, as ValveResourceFormat's renderer turns their keys into the fog
	/// constants the game's shaders read (the formulas the shaders apply them with are from the decompiled shaders).
	/// </summary>
	private void AddFog( CS2MapLighting lighting, IReadOnlyList<MapEntity> entities, Func<string, int> addTexture )
	{
		foreach ( var entity in entities )
		{
			if ( entity.GetBool( "StartDisabled" ) )
				continue;

			switch ( entity.Classname.ToLowerInvariant() )
			{
				case "env_gradient_fog":
					AddGradientFog( lighting, entity );
					break;

				case "env_cubemap_fog":
					AddCubemapFog( lighting, entity, entities, addTexture );
					break;
			}
		}
	}

	private void AddGradientFog( CS2MapLighting lighting, MapEntity entity )
	{
		var startDistance = entity.GetFloat( "fogstart" );
		var endDistance = entity.GetFloat( "fogend" );
		var heightFog = entity.GetBool( "heightfog", entity.Has( "fogverticalexponent" ) );

		var startHeight = entity.GetFloat( "fogstartheight" );
		var endHeight = entity.GetFloat( "fogendheight" );
		var verticalExponent = entity.GetFloat( "fogverticalexponent", 1 );

		if ( !heightFog && !entity.Has( "fogstartheight" ) )
		{
			Log.Warning( $"{mapName}: env_gradient_fog without height fog keys isn't ported; it is left out." );
			return;
		}

		var distanceScale = 1 / (endDistance - startDistance);
		var heightScale = 1 / (startHeight - endHeight);
		var color = ReadColor( entity, "fogcolor", [255, 255, 255] ) * entity.GetFloat( "fogstrength", 1 );

		lighting.GradientFog = true;
		lighting.GradientFogBiasAndScale = new Vector4( -startDistance * distanceScale, -endHeight * heightScale, distanceScale, heightScale );
		lighting.GradientFogExponents = new Vector2( entity.GetFloat( "fogfalloffexponent", 1 ), verticalExponent );
		lighting.GradientFogColor = new Vector4( color.r, color.g, color.b, entity.GetFloat( "fogmaxopacity", 1 ) );
		lighting.GradientFogCulling = new Vector2( startDistance * startDistance, startHeight );
	}

	/// <summary>
	/// Fog that fades the distance into the sky, coloured from the sky's cubemap at the sky's exposure.
	/// </summary>
	private void AddCubemapFog( CS2MapLighting lighting, MapEntity entity, IReadOnlyList<MapEntity> entities, Func<string, int> addTexture )
	{
		var source = entity.GetInt( "cubemapfogsource" );
		var rotation = ReadTransform( entity ).Rotation;
		string texture = null;
		string skyMaterial = null;
		var exposureBias = 0f;

		switch ( source )
		{
			case 0:
				texture = entity.GetString( "cubemapfogtexture" );
				break;

			case 1:
				var skyName = entity.GetString( "cubemapfogskyentity" );
				var sky = entities.FirstOrDefault( x => x.Classname.Equals( "env_sky", StringComparison.OrdinalIgnoreCase ) && x.TargetName.Equals( skyName, StringComparison.OrdinalIgnoreCase ) );
				if ( sky is null )
				{
					Log.Warning( $"{mapName}: env_cubemap_fog's sky '{skyName}' doesn't exist; the fog is left out." );
					return;
				}

				skyMaterial = sky.GetString( "skyname" );
				rotation = ReadTransform( sky ).Rotation;
				exposureBias += MathF.Log2( MathF.Max( sky.GetFloat( "brightnessscale", 1 ), 1e-6f ) );
				break;

			case 2:
				skyMaterial = entity.GetString( "cubemapfogskymaterial" );
				break;
		}

		if ( !string.IsNullOrEmpty( skyMaterial ) )
		{
			skyMaterial = CompiledWorld.NormalizePath( skyMaterial );
			if ( Host.FileExists( skyMaterial + "_c" ) )
			{
				var material = new CompiledMaterial( Host.ReadFile( skyMaterial + "_c" ) );
				texture = material.GetTexture( "g_tSkyTexture" );
				exposureBias += material.GetFloat( "g_flBrightnessExposureBias" ) + material.GetFloat( "g_flRenderOnlyExposureBias" );
			}
		}

		var textureIndex = addTexture( texture );
		if ( textureIndex < 0 )
		{
			Log.Warning( $"{mapName}: env_cubemap_fog has no cubemap to colour it; the fog is left out." );
			return;
		}

		var width = Host.ReadTextureHeader( CompiledWorld.NormalizePath( texture ) ).Width;

		var startDistance = entity.GetFloat( "cubemapfogstartdistance" );
		var endDistance = entity.GetFloat( "cubemapfogenddistance" );
		var distanceScale = 1 / (endDistance - startDistance);

		// Height fog: full up to the start height, none from the end height. Without it, the height term is 1.
		var heightOffset = 1f;
		var heightScale = 0.000001f;
		var heightExponent = 0f;

		if ( entity.GetBool( "cubemapheightfog", entity.Has( "cubemapfogheightexponent" ) ) )
		{
			var heightStart = entity.GetFloat( "cubemapfogheightstart" );
			var heightEnd = entity.Has( "cubemapfogheightend" )
				? entity.GetFloat( "cubemapfogheightend" )
				: heightStart + entity.GetFloat( "cubemapfogheightwidth" );

			if ( heightEnd - heightStart > 0 )
			{
				heightScale = 1 / (heightStart - heightEnd);
				heightOffset = 1 - heightStart * heightScale;
				heightExponent = entity.GetFloat( "cubemapfogheightexponent", 1 );
			}
		}

		lighting.CubemapFog = true;
		lighting.CubemapFogTexture = textureIndex;
		lighting.CubemapFogRotation = rotation.Angles();
		lighting.CubemapFogDistance = new Vector4( -startDistance * distanceScale, distanceScale, entity.GetFloat( "cubemapfoglodbiase" ), entity.GetFloat( "cubemapfogfalloffexponent", 1 ) );

		// The fog blurs down to the mip three levels above a single texel (ValveResourceFormat's).
		lighting.CubemapFogHeight = new Vector4( heightOffset, heightScale, heightExponent, MathF.Log2( width ) - 3 );
		lighting.CubemapFogParameters = new Vector4( startDistance * startDistance, MathF.Pow( 2, exposureBias ), 0, entity.GetFloat( "cubemapfogmaxopacity", 1 ) );
	}

	/// <summary>
	/// A column of a row vector matrix, which is a row of the transform the shaders apply with dot products.
	/// </summary>
	private static Vector4 Column( Numerics.Matrix4x4 m, int column ) => column switch
	{
		0 => new Vector4( m.M11, m.M21, m.M31, m.M41 ),
		1 => new Vector4( m.M12, m.M22, m.M32, m.M42 ),
		2 => new Vector4( m.M13, m.M23, m.M33, m.M43 ),
		_ => new Vector4( m.M14, m.M24, m.M34, m.M44 ),
	};

	private static Vector3 ReadVector( MapEntity entity, string key, float fallback )
	{
		var value = entity.GetVector( key, 3, [fallback, fallback, fallback] );
		return new Vector3( value[0], value[1], value[2] );
	}

	/// <summary>
	/// A vector stored as one key per component, such as light_probe_size_x, _y and _z.
	/// </summary>
	private static Vector3 ReadVector( MapEntity entity, string key, float fallback, string x, string y, string z )
	{
		return new Vector3( entity.GetFloat( $"{key}_{x}", fallback ), entity.GetFloat( $"{key}_{y}", fallback ), entity.GetFloat( $"{key}_{z}", fallback ) );
	}

	private static Vector3 SrgbToLinear( Color color )
	{
		static float Channel( float c ) => c <= 0.04045f ? c / 12.92f : MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
		return new Vector3( Channel( color.r ), Channel( color.g ), Channel( color.b ) );
	}
}
