namespace CS2;

/// <summary>
/// A map's 3D skybox: the world its skybox_reference names, which the mount serves as a scene of its own, drawn by
/// s&amp;box's 3D skybox (<see cref="Skybox3D"/>) in a pass of its own behind the map, as the game draws it. This
/// object places it: the sky camera's point at this object's position, everything around it scale (this object's
/// scale) times larger.
/// </summary>
[Title( "CS2 3D Skybox" ), Category( "Counter-Strike 2" ), Icon( "landscape" )]
public sealed class CS2Skybox : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The skybox world's scene, e.g. mount://cs2/maps/prefabs/de_dust2/de_dust2_skybox.scene.
	/// </summary>
	[Property] public string SkyboxScene { get; set; }

	private Skybox3D _skybox;

	protected override void OnEnabled()
	{
		if ( string.IsNullOrWhiteSpace( SkyboxScene ) )
			return;

		var scene = SceneFile.Load( SkyboxScene );
		if ( scene is null )
		{
			Log.Warning( $"{GameObject.Name}: the skybox scene '{SkyboxScene}' didn't load." );
			return;
		}

		// Made here rather than saved with the map, so the scene it draws is loaded when the map is.
		_skybox = Components.Create<Skybox3D>( false );
		_skybox.Flags |= ComponentFlags.NotSaved | ComponentFlags.Hidden;
		_skybox.SkyboxScene = scene;
		_skybox.Enabled = true;
	}

	protected override void OnDisabled()
	{
		_skybox?.Destroy();
		_skybox = null;
	}
}
