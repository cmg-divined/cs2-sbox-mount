using System;
using System.IO;

namespace Sandbox.Mounting.CS2;

public sealed partial class CS2Mount
{
	private const string SurfacePropertiesPath = "surfaceproperties/surfaceproperties.vsurf_c";
	private const string FootstepsPath = "scripts/surfaceproperties_footsteps.txt";
	private const string ImpactEffectsPath = "scripts/surfaceproperties_impact_effects.txt";

	/// <summary>
	/// A CS2 surface property: its name, the surface it inherits from, and its entry (physics, audiosounds...).
	/// </summary>
	private sealed record SurfaceProperty( string Name, uint BaseHash, KVObject Data );

	private Dictionary<uint, SurfaceProperty> _surfaceProperties;
	private Dictionary<string, SurfaceProperty> _surfacePropertiesByName;
	private readonly Dictionary<string, Surface> _surfaces = new( StringComparer.OrdinalIgnoreCase );

	private void ClearSurfaces()
	{
		_surfaceProperties = null;
		_surfacePropertiesByName = null;
		_footsteps = null;
		_impactEffects = null;
		_surfaces.Clear();
	}

	/// <summary>
	/// Registers each CS2 surface property as an s&amp;box surface at <c>surfaces/cs2_name.surface</c>, and what CS2 does
	/// with it beside it, at <c>surfaces/cs2_name.cs2surf</c>.
	/// </summary>
	private void MountSurfaces( MountContext context )
	{
		foreach ( var name in SurfacePropertiesByName.Keys )
		{
			var path = SurfaceResourcePath( name );
			context.Add( ResourceType.GameResource, $"{path}.surface", new SurfaceLoader( name ) );
			context.Add( ResourceType.GameResource, $"{path}.cs2surf", new SurfaceInfoLoader( name ) );
		}
	}

	private static string SurfaceResourcePath( string name ) => $"surfaces/cs2_{name.ToLowerInvariant()}";

	/// <summary>
	/// The s&amp;box surface for a surface property hash, as physics shapes store them.
	/// </summary>
	internal Surface GetSurface( uint hash )
	{
		return GetSurface( SurfaceProperties.TryGetValue( hash, out var property ) ? property.Name : null );
	}

	/// <summary>
	/// The s&amp;box surface the mount made of a CS2 surface property, or the default surface's for a name CS2 doesn't
	/// have.
	/// </summary>
	internal Surface GetSurface( string name )
	{
		name = name is not null && SurfacePropertiesByName.ContainsKey( name ) ? name : "default";

		lock ( _surfaces )
		{
			if ( _surfaces.TryGetValue( name, out var cached ) )
				return cached;
		}

		var surface = GameResource.Load<Surface>( $"mount://{Ident}/{SurfaceResourcePath( name )}.surface" ) ?? Surface.FindByName( "default" );
		lock ( _surfaces )
			_surfaces[name] = surface;

		return surface;
	}

	/// <summary>
	/// The name a physics shape refers to a surface by: a mounted surface's path.
	/// </summary>
	internal static string SurfaceBindingName( Surface surface ) => surface.ResourcePath?.StartsWith( "mount://", StringComparison.OrdinalIgnoreCase ) == true ? surface.ResourcePath : surface.ResourceName;

	/// <summary>
	/// A surface property by name with what it inherits: the property itself, then its base, then its base's...
	/// </summary>
	internal IEnumerable<KVObject> SurfaceChain( string name )
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		for ( var property = SurfacePropertiesByName.GetValueOrDefault( name ?? "" ); property is not null && seen.Add( property.Name ); )
		{
			yield return property.Data;
			property = SurfaceProperties.GetValueOrDefault( property.BaseHash );
		}
	}

	internal IEnumerable<string> SurfaceNameChain( string name )
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		for ( var property = SurfacePropertiesByName.GetValueOrDefault( name ?? "" ); property is not null && seen.Add( property.Name ); )
		{
			yield return property.Name;
			property = SurfaceProperties.GetValueOrDefault( property.BaseHash );
		}
	}

	internal bool SoundEventExists( string name ) => !string.IsNullOrEmpty( name ) && _soundEvents.ContainsKey( name );

	private Dictionary<string, SurfaceProperty> SurfacePropertiesByName
	{
		get
		{
			if ( _surfacePropertiesByName is { } byName )
				return byName;

			byName = new Dictionary<string, SurfaceProperty>( StringComparer.OrdinalIgnoreCase );
			foreach ( var property in SurfaceProperties.Values )
				byName.TryAdd( property.Name, property );

			return _surfacePropertiesByName = byName;
		}
	}

	/// <summary>
	/// CS2's surface properties by name hash, read from every search path, earlier ones winning.
	/// </summary>
	private Dictionary<uint, SurfaceProperty> SurfaceProperties
	{
		get
		{
			if ( _surfaceProperties is { } properties )
				return properties;

			properties = [];

			foreach ( var archive in _archives )
			{
				if ( !archive.Contains( SurfacePropertiesPath ) )
					continue;

				try
				{
					var data = new ResourceFile( archive.ReadFile( SurfacePropertiesPath ) ).ReadKeyValues( "DATA" );

					foreach ( var property in data?.GetObjects( "SurfacePropertiesList" ) ?? [] )
					{
						var hash = unchecked((uint)property.GetInt64( "m_nameHash" ));
						var baseHash = unchecked((uint)property.GetInt64( "m_baseNameHash" ));
						properties.TryAdd( hash, new SurfaceProperty( property.GetString( "surfacePropertyName", "" ), baseHash, property ) );
					}
				}
				catch ( Exception e ) when ( e is InvalidDataException or IOException )
				{
					Log.Warning( $"Couldn't read {Title}'s surface properties: {e.Message}" );
				}
			}

			return _surfaceProperties = properties;
		}
	}

	private KVObject _footsteps;
	private KVObject _impactEffects;

	/// <summary>
	/// scripts/surfaceproperties_footsteps.txt: per team (ct_player, t_player), each surface's walking sounds.
	/// </summary>
	internal KVObject Footsteps => _footsteps ??= ReadTextKeyValues( FootstepsPath );

	/// <summary>
	/// scripts/surfaceproperties_impact_effects.txt: each surface's bullet impact effect and decals.
	/// </summary>
	internal KVObject ImpactEffects => _impactEffects ??= ReadTextKeyValues( ImpactEffectsPath );

	private KVObject ReadTextKeyValues( string path )
	{
		if ( !FileExists( path ) )
			return new KVObject();

		try
		{
			return KV3TextReader.Read( System.Text.Encoding.UTF8.GetString( ReadFile( path ) ) );
		}
		catch ( InvalidDataException e )
		{
			Log.Warning( $"Couldn't read '{path}': {e.Message}" );
			return new KVObject();
		}
	}
}
