using System;
using System.Threading.Tasks;

namespace CS2;

/// <summary>
/// Puts a mounted CS2 map into this scene, the way <see cref="MapInstance"/> does for Hammer maps. The map's objects
/// become unsaved children of this GameObject: they show in the editor without being written into the scene file, and
/// load again when the game starts. Put your player, camera and game logic in the same scene.
/// </summary>
/// <remarks>
/// Mounted map scenes are read only, and pressing play on one plays the map as mounted, without anything added to it
/// in the editor. This component is how a scene of your own gets the map.
/// </remarks>
[Title( "CS2 Map" ), Category( "Counter-Strike 2" ), Icon( "map" )]
public class CS2Map : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The map's name, as in game/csgo/maps: de_dust2, de_mirage, cs_office...
	/// </summary>
	[Property]
	public string MapName { get; set; } = "de_dust2";

	/// <summary>
	/// Whether a <see cref="CS2Player"/> spawns when the game starts, on a random team at one of its spawn points,
	/// unless the scene has a player of its own.
	/// </summary>
	[Property]
	public bool SpawnPlayer { get; set; } = true;

	/// <summary>
	/// The mounted scene the map loads from.
	/// </summary>
	public string ScenePath => $"mount://cs2/maps/{MapName}.scene";

	private string _loadedMap;
	private string _missingMap;

	protected override Task OnLoad( LoadingContext context )
	{
		// In game, load while the scene loads, so the map is there before anything spawns into it.
		if ( Active )
			LoadMap();

		return Task.CompletedTask;
	}

	protected override void OnStart()
	{
		if ( !Game.IsPlaying || !SpawnPlayer || Scene.GetAllComponents<CS2Player>().Any() )
			return;

		var player = new GameObject( true, "Player" );
		player.WorldPosition = WorldPosition;
		player.Components.Create<CS2Player>();
	}

	protected override void OnUpdate()
	{
		// Loads the map once the component is enabled, and again when its name changes in the inspector to another
		// map. Not in OnEnabled: adding the component in the editor enables it inside its own creation, and building
		// the map's objects there leaves their renderers half set up (it crashed s&box in the Deadlock mount).
		if ( _loadedMap != MapName && _missingMap != MapName )
			LoadMap();
	}

	protected override void OnDisabled() => UnloadMap();

	protected override void OnDestroy() => UnloadMap();

	public void LoadMap()
	{
		if ( string.IsNullOrWhiteSpace( MapName ) )
		{
			UnloadMap();
			_loadedMap = MapName;
			return;
		}

		// A name that isn't a map (one being typed in the inspector, a letter at a time) keeps the map that's loaded.
		var sceneFile = SceneFile.Load( ScenePath );
		if ( sceneFile?.GameObjects is null )
		{
			_missingMap = MapName;
			Log.Warning( $"{GameObject.Name}: couldn't load the CS2 map '{MapName}' ({ScenePath}). Is the Counter-Strike 2 mount enabled?" );
			return;
		}

		UnloadMap();
		_loadedMap = MapName;

		// The map's colliders would join that body and the whole map would move with it.
		if ( Components.GetInAncestorsOrSelf<Rigidbody>() is { } rigidbody )
			Log.Warning( $"{GameObject.Name}: CS2 Map is under '{rigidbody.GameObject.Name}', which has a Rigidbody, so the map will move with it. Put it on an empty GameObject." );

		// The map is placed where this GameObject is, but always at its own size: a scaled GameObject (a scaled plane,
		// say) would scale the whole map around everything else.
		var scale = GameObject.WorldScale;
		if ( scale != Vector3.One )
			Log.Info( $"{GameObject.Name}: CS2 Map ignores its GameObject's scale ({scale}); the map is built at its own size." );

		foreach ( var json in sceneFile.GameObjects )
		{
			var gameObject = new GameObject( false );
			gameObject.Flags |= GameObjectFlags.NotSaved;
			gameObject.SetParent( GameObject, false );
			gameObject.Deserialize( json );

			if ( scale != Vector3.One )
			{
				var local = gameObject.LocalTransform;
				gameObject.LocalTransform = new Transform( local.Position / scale, local.Rotation, local.Scale / scale );
			}
		}
	}

	public void UnloadMap()
	{
		_loadedMap = null;

		if ( !GameObject.IsValid() )
			return;

		// Immediately, so a reload doesn't meet the old objects' ids.
		foreach ( var child in GameObject.Children.Where( x => x.Flags.Contains( GameObjectFlags.NotSaved ) ).ToArray() )
			child.DestroyImmediate();
	}
}
