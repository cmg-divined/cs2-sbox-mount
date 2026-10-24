using System;
using System.IO;
using CS2;

namespace Sandbox.Mounting.CS2;

public sealed partial class CS2Mount
{
	private const string DecalGroupsPath = "scripts/decalgroups.vdata_c";

	private KVObject _decalGroups;

	/// <summary>
	/// Registers each decal group of scripts/decalgroups.vdata at <c>decals/name.cs2decal</c>.
	/// </summary>
	private void MountDecals( MountContext context )
	{
		_decalGroups = null;
		foreach ( var name in DecalGroups.Keys )
		{
			if ( DecalGroups[name] is KVObject )
				context.Add( ResourceType.GameResource, $"decals/{name.ToLowerInvariant()}.cs2decal", new DecalGroupLoader( name ) );
		}
	}

	internal KVObject DecalGroups
	{
		get
		{
			if ( _decalGroups is not null )
				return _decalGroups;

			try
			{
				_decalGroups = FileExists( DecalGroupsPath ) ? new ResourceFile( ReadFile( DecalGroupsPath ) ).ReadKeyValues( "DATA" ) : null;
			}
			catch ( Exception e ) when ( e is InvalidDataException or IOException )
			{
				Log.Warning( $"Couldn't read '{DecalGroupsPath}': {e.Message}" );
			}

			return _decalGroups ??= new KVObject();
		}
	}
}

/// <summary>
/// Loads a decal group as a <see cref="CS2DecalGroup"/>: each option's material read for its textures (g_tColor,
/// g_tNormal, g_tHeight), its world size (DecalWorldWidth/Height), depth (DecalDepth) and parallax (g_flHeightMapScale).
/// </summary>
internal sealed class DecalGroupLoader( string name ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var group = new CS2DecalGroup { Name = name };

		foreach ( var option in Host.DecalGroups.GetObject( name )?.GetObjects( "m_vecOptions" ) ?? [] )
		{
			var path = option.GetString( "m_hMaterial" );
			if ( string.IsNullOrEmpty( path ) || !Host.FileExists( path + "_c" ) )
				continue;

			var material = new ResourceFile( Host.ReadFile( path + "_c" ) ).ReadKeyValues( "DATA" );
			if ( material is null )
				continue;

			string Texture( string parameter ) => material.GetObjects( "m_textureParams" ).FirstOrDefault( x => x.GetString( "m_name" ) == parameter )?.GetString( "m_pValue" );
			float Float( string key, string parameter, float fallback ) => material.GetObjects( key ).FirstOrDefault( x => x.GetString( "m_name" ) == parameter )?.GetFloat( "m_flValue", fallback ) ?? fallback;

			group.Options.Add( new CS2DecalOption
			{
				Material = path,
				Probability = option.GetFloat( "m_flProbability", 1f ),
				Color = Texture( "g_tColor" ),
				Normal = Texture( "g_tNormal" ),
				Height = Texture( "g_tHeight" ),
				Width = Float( "m_floatAttributes", "DecalWorldWidth", 4f ),
				Length = Float( "m_floatAttributes", "DecalWorldHeight", 4f ),
				Depth = Float( "m_floatAttributes", "DecalDepth", 8f ),
				HeightScale = Float( "m_floatParams", "g_flHeightMapScale", 0f ),
			} );
		}

		return group;
	}
}
