using CS2;
using System;

namespace Sandbox.Mounting.CS2;

internal sealed partial class MapLoader
{
	/// <summary>
	/// skybox_reference: a world of its own the map draws around itself (maps/prefabs/.../name.vpk, worldspawn
	/// mapUsageType skybox), its distant scenery. With a sky_camera in it, it's a 3D skybox: the game views it from the
	/// sky camera, moved by the player's movement over its scale, so it shows as if scale times larger around the map.
	/// The mount serves it with the map's sky as a scene of its own (<see cref="Skybox"/>), and a
	/// <see cref="CS2Skybox"/> here has s&amp;box draw that as its 3D skybox, in a pass of its own behind the map as the
	/// game draws it: the sky camera's point at the map's origin, everything around it scale times larger.
	/// </summary>
	/// <remarks>
	/// How a reference's own transform combines with the sky camera isn't known (every map's sits at the origin); here
	/// the sky camera's point is taken through it. s&amp;box's 3D skybox doesn't turn, so a reference that does is
	/// drawn unturned, with a warning. s&amp;box draws one 3D skybox per scene; a second reference is left out.
	/// </remarks>
	private void BuildSkyboxes( IReadOnlyList<MapEntity> entities )
	{
		var (reference, target) = FindSkybox( entities );
		if ( reference is null )
			return;

		var skyEntities = ReadEntities( new CompiledWorld( Host.ReadFile( $"{target}/world.vwrld_c" ) ) );
		var placement = SkyboxPlacement( ReadTransform( reference ), skyEntities );
		if ( placement.Rotation != Rotation.Identity )
			Log.Warning( $"{mapName}: skybox_reference '{target}' is turned, which s&box's 3D skybox doesn't do; it's drawn unturned." );

		var gameObject = new GameObject( true, $"3D Skybox ({System.IO.Path.GetFileName( target )})" );
		gameObject.WorldTransform = placement.WithRotation( Rotation.Identity );
		gameObject.Components.Create<CS2Skybox>().SkyboxScene = $"mount://{Host.Ident}/maps/{mapName}/skybox.scene";
	}

	/// <summary>
	/// The map's 3D skybox on its own, in the skybox world's space: what the world draws, its entities and lighting,
	/// and the map's sky, which the game draws in the skybox's pass (s&amp;box's 3D skybox covers the map's own). The
	/// lighting is on its own renderers (scoped): what s&amp;box hands a 3D skybox's scene of the map's isn't known.
	/// </summary>
	private void BuildSkyboxScene( IReadOnlyList<MapEntity> mapEntities )
	{
		var (reference, target) = FindSkybox( mapEntities );
		if ( reference is null )
			return;

		_worldFolder = target;
		var world = new CompiledWorld( Host.ReadFile( $"{target}/world.vwrld_c" ) );
		var root = new GameObject( true, "Skybox" );

		BuildWorld( world, root );
		var entities = BuildEntities( world, root ).Select( x => x.Entity ).ToList();
		BuildLighting( world, entities, root );

		foreach ( var entity in mapEntities.Where( x => x.Classname.Equals( "env_sky", StringComparison.OrdinalIgnoreCase ) ) )
		{
			var sky = new GameObject( !entity.GetBool( "StartDisabled" ), entity.TargetName.Length > 0 ? entity.TargetName : entity.Classname );
			AddSky( entity, sky );
		}
	}

	/// <summary>
	/// The map's first skybox_reference whose world is in the game's content, and that world's folder; a warning for
	/// any other.
	/// </summary>
	private (MapEntity Reference, string World) FindSkybox( IReadOnlyList<MapEntity> entities )
	{
		(MapEntity Reference, string World) found = default;

		foreach ( var reference in entities )
		{
			if ( !reference.Classname.Equals( "skybox_reference", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var target = CompiledWorld.NormalizePath( reference.GetString( "targetMapName" ) ?? "" );
			if ( target.EndsWith( ".vmap", StringComparison.OrdinalIgnoreCase ) )
				target = target[..^".vmap".Length];

			if ( target.Length == 0 || !Host.FileExists( $"{target}/world.vwrld_c" ) )
				Log.Warning( $"{mapName}: skybox_reference's world '{target}' isn't in the game's content." );
			else if ( found.Reference is not null )
				Log.Warning( $"{mapName}: a second 3D skybox ('{target}') is left out; s&box draws one per scene." );
			else
				found = (reference, target);
		}

		return found;
	}

	/// <summary>
	/// Where a referenced world goes: around its sky camera, scale times larger, or just where the reference is.
	/// </summary>
	private static Transform SkyboxPlacement( Transform reference, IReadOnlyList<MapEntity> entities )
	{
		var camera = entities.FirstOrDefault( x => x.Classname.Equals( "sky_camera", StringComparison.OrdinalIgnoreCase ) );
		if ( camera is null )
			return reference;

		var scale = camera.GetFloat( "scale", 16 );
		var origin = ReadTransform( camera ).Position;

		// scale * ( reference( p ) - reference( origin ) ).
		return new Transform( reference.Rotation * (-origin * reference.Scale) * scale, reference.Rotation, reference.Scale * scale );
	}
}
