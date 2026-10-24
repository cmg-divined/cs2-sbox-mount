using CS2;
using Sandbox.Volumes;
using System;
using System.IO;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Loads a CS2 map (maps/name.vpk) as a scene, as the game builds it: the world nodes' placed models and aggregate
/// geometry, the world collision split by collision attribute, every entity of the entity lumps, the baked and
/// realtime lighting the CS2 shaders light with, and the post processing; or, on its own, what the map draws as its
/// 3D skybox (<see cref="Skybox"/>).
/// </summary>
internal sealed partial class MapLoader : SceneLoader<CS2Mount>
{
	private readonly string mapName;

	/// <summary>
	/// Models built for this map (aggregates, collision), kept alive for as long as the scene is.
	/// </summary>
	private readonly List<Model> _models = [];

	private readonly HashSet<string> _missingModels = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The folder of the world being built, which the models built for it are named under: the map's, or its skybox
	/// world's.
	/// </summary>
	private string _worldFolder;

	/// <summary>
	/// Whether this builds a map's 3D skybox: only what's drawn, without collision, realtime lights or gameplay
	/// entities, as the game draws it behind the map.
	/// </summary>
	private readonly bool _skybox;

	public MapLoader( string mapName ) : this( mapName, false )
	{
	}

	private MapLoader( string mapName, bool skybox )
	{
		this.mapName = mapName;
		_worldFolder = $"maps/{mapName}";
		_skybox = skybox;
	}

	/// <summary>
	/// What a map draws as its 3D skybox, as a scene of its own in its own space (maps/name/skybox): the world its
	/// skybox_reference names and the map's sky, which the game draws in the skybox's pass.
	/// </summary>
	public static MapLoader Skybox( string mapName ) => new( mapName, true );

	protected override void BuildScene()
	{
		CS2Mount.EnsureGameTypes();

		var worldPath = $"maps/{mapName}/world.vwrld_c";
		if ( !Host.FileExists( worldPath ) )
		{
			Log.Warning( $"{mapName}: the map has no world." );
			return;
		}

		var world = new CompiledWorld( Host.ReadFile( worldPath ) );

		if ( _skybox )
		{
			BuildSkyboxScene( ReadEntities( world ) );
			return;
		}

		BuildWorld( world, null );
		BuildCollision();
		var entities = BuildEntities( world, null );
		BuildLighting( world, entities.Select( x => x.Entity ).ToList(), null );
		BuildPostProcessing();
		BuildSkyboxes( entities.Select( x => x.Entity ).ToList() );
	}

	/// <summary>
	/// CS2's post processing for whatever camera views the map: <see cref="CS2PostProcessing"/> in a volume over all
	/// of it, so a game's own camera gets it too. It blends the map's post processing volumes by where the camera is.
	/// </summary>
	/// <remarks>
	/// Games may turn their camera's own tonemapping back on unless the scene has its own: Sandbox's DefaultTonemapping
	/// (facepunch.sandbox, decompiled) does every frame, unless an enabled Tonemapping is on a camera or outside any
	/// volume but an infinite one, and it would tonemap the map a second time. So the map has one of those, which
	/// draws for no camera: s&amp;box only takes post processing from cameras and the volumes around them.
	/// </remarks>
	private static void BuildPostProcessing()
	{
		var gameObject = new GameObject( true, "Post Processing" );
		var volume = gameObject.Components.Create<PostProcessVolume>();
		volume.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
		gameObject.Components.Create<CS2PostProcessing>();

		new GameObject( true, "Scene Tonemapping (CS2 Post Processing's)" ).Components.Create<Tonemapping>();
	}

	/// <summary>
	/// World collision: maps/name/world_physics.vmdl, one static collider per collision attribute, tagged with
	/// what the attribute interacts as (solid, playerclip, npcclip, sky...).
	/// </summary>
	private void BuildCollision()
	{
		var path = $"maps/{mapName}/world_physics.vmdl";
		if ( !Host.FileExists( path + "_c" ) )
			return;

		var root = new GameObject( true, "Collision" );
		var groups = new ModelImporter( Host, new CompiledModel( Host.ReadFile( path + "_c" ) ), $"maps/{mapName}/world_physics" ).ImportCollisionGroups();

		foreach ( var (tags, model) in groups )
		{
			_models.Add( model );

			var collision = new GameObject( true, tags.Length > 0 ? string.Join( ", ", tags ) : "default" );
			collision.SetParent( root, false );

			foreach ( var tag in tags )
				collision.Tags.Add( tag.ToLowerInvariant() );

			var collider = collision.Components.Create<ModelCollider>();
			collider.Model = model;
			collider.Static = true;
		}
	}

	/// <summary>
	/// A mounted model by the path a map refers to it by, or null if it isn't in the game's content.
	/// </summary>
	private Model LoadModel( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		return Host.LoadModel( CompiledWorld.NormalizePath( path ) );
	}

	/// <summary>
	/// A 3x4 matrix as a transform. Its columns are the scaled axes; a mirroring matrix gets a negative Y scale.
	/// </summary>
	private static Transform ToTransform( Matrix3x4 matrix )
	{
		var m = matrix.Values;
		var x = new Vector3( m[0], m[4], m[8] );
		var y = new Vector3( m[1], m[5], m[9] );
		var z = new Vector3( m[2], m[6], m[10] );
		var position = new Vector3( m[3], m[7], m[11] );

		if ( x.Length <= 0 || z.Length <= 0 )
			return new Transform( position );

		var scale = new Vector3( x.Length, y.Length, z.Length );
		if ( Vector3.Dot( Vector3.Cross( x, y ), z ) < 0 )
			scale.y = -scale.y;

		return new Transform( position, Rotation.LookAt( x.Normal, z.Normal ), scale );
	}
}
