using System;
using System.IO;
using System.Threading.Tasks;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Mounts Counter-Strike 2's compiled Source 2 content from its VPKs.
/// </summary>
public sealed partial class CS2Mount : BaseGameMount
{
	private const long AppId = 730;

	/// <summary>
	/// Content folders under <c>game/</c>, highest priority first, as listed in CS2's gameinfo.gi.
	/// </summary>
	private static readonly string[] SearchPaths = ["csgo", "core"];

	private readonly List<VpkArchive> _archives = [];
	private readonly Dictionary<string, VpkArchive> _files = new( StringComparer.OrdinalIgnoreCase );
	private string _gameDirectory;

	/// <summary>
	/// The mount the CS2 loaders and components use, while it's mounted.
	/// </summary>
	internal static CS2Mount Current { get; private set; }

	public override string Ident => "cs2";
	public override string Title => "Counter-Strike 2";
	public override long? SteamAppId => AppId;

	protected override void Initialize( InitializeContext context )
	{
		IsInstalled = false;
		IsMounted = false;

		if ( !context.IsAppInstalled( AppId ) )
			return;

		var installDirectory = context.GetAppDirectory( AppId );
		if ( string.IsNullOrWhiteSpace( installDirectory ) )
			return;

		_gameDirectory = System.IO.Path.Combine( installDirectory, "game" );
		IsInstalled = System.IO.File.Exists( System.IO.Path.Combine( _gameDirectory, "csgo", "pak01_dir.vpk" ) );
	}

	protected override Task Mount( MountContext context )
	{
		CloseArchives();

		foreach ( var searchPath in SearchPaths )
		{
			var path = System.IO.Path.Combine( _gameDirectory, searchPath, "pak01_dir.vpk" );
			if ( !System.IO.File.Exists( path ) )
				continue;

			try
			{
				OpenArchive( path );
			}
			catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException )
			{
				Log.Warning( $"Couldn't open '{path}': {e.Message}" );
				context.AddError( $"Couldn't open '{path}': {e.Message}" );
			}
		}

		// Map VPKs come after the base content, which wins where a map bundles a file under the same path.
		var maps = new List<string>();
		var mapsDirectory = System.IO.Path.Combine( _gameDirectory, "csgo", "maps" );

		if ( System.IO.Directory.Exists( mapsDirectory ) )
		{
			foreach ( var path in System.IO.Directory.GetFiles( mapsDirectory, "*.vpk" ).Order( StringComparer.OrdinalIgnoreCase ) )
			{
				var mapName = System.IO.Path.GetFileNameWithoutExtension( path );

				try
				{
					var archive = OpenArchive( path );
					if ( archive.Contains( $"maps/{mapName}/world.vwrld_c" ) )
						maps.Add( mapName );
				}
				catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException )
				{
					Log.Warning( $"Couldn't open '{path}': {e.Message}" );
					context.AddError( $"Couldn't open '{path}': {e.Message}" );
				}
			}

			// Worlds maps reference rather than being maps of their own: the 3D skyboxes (skybox_reference) and other
			// prefab worlds, compiled to maps/prefabs/.../name.vpk.
			var prefabsDirectory = System.IO.Path.Combine( mapsDirectory, "prefabs" );
			if ( System.IO.Directory.Exists( prefabsDirectory ) )
			{
				foreach ( var path in System.IO.Directory.GetFiles( prefabsDirectory, "*.vpk", System.IO.SearchOption.AllDirectories ).Order( StringComparer.OrdinalIgnoreCase ) )
				{
					try
					{
						OpenArchive( path );
					}
					catch ( Exception e ) when ( e is IOException or InvalidDataException or UnauthorizedAccessException )
					{
						Log.Warning( $"Couldn't open '{path}': {e.Message}" );
						context.AddError( $"Couldn't open '{path}': {e.Message}" );
					}
				}
			}
		}

		foreach ( var mapName in maps )
		{
			context.Add( ResourceType.Scene, $"maps/{mapName}", new MapLoader( mapName ) );

			// What the map draws as its 3D skybox, a scene of its own its CS2 3D Skybox loads; only for that, so hidden
			// from what lists the mount's content in game. Empty for a map without one.
			var skybox = MapLoader.Skybox( mapName );
			skybox.Flags = ResourceFlags.DeveloperOnly;
			context.Add( ResourceType.Scene, $"maps/{mapName}/skybox", skybox );
		}

		int models = 0, materials = 0, textures = 0;

		foreach ( var file in _files.Keys )
		{
			// Resources are registered under their source name, without the compiled "_c" suffix.
			var extension = System.IO.Path.GetExtension( file );

			if ( extension.Equals( ".vmdl_c", StringComparison.OrdinalIgnoreCase ) )
			{
				context.Add( ResourceType.Model, file[..^2], new ModelLoader( file ) );
				models++;
			}
			else if ( extension.Equals( ".vmat_c", StringComparison.OrdinalIgnoreCase ) )
			{
				context.Add( ResourceType.Material, file[..^2], new MaterialLoader( file ) );
				materials++;
			}
			else if ( extension.Equals( ".vtex_c", StringComparison.OrdinalIgnoreCase ) )
			{
				context.Add( ResourceType.Texture, file[..^2], new TextureLoader( file ) );
				textures++;

				// Lightmaps are also served split: the top mip, and the mips below it.
				if ( IsLightmap( file ) )
				{
					context.Add( ResourceType.Texture, LightmapPart( file[..^2], true ), new TextureLoader( file, 0, 1 ) );
					context.Add( ResourceType.Texture, LightmapPart( file[..^2], false ), new TextureLoader( file, 1 ) );
				}
			}
			else if ( extension.Equals( ".vpost_c", StringComparison.OrdinalIgnoreCase ) )
			{
				// A post processing resource's colour correction, as the 3D texture it is.
				context.Add( ResourceType.Texture, ColorCorrectionPath( file[..^2] ), new ColorCorrectionLoader( file ) );
			}
			else if ( extension.Equals( ".vnmgraph_c", StringComparison.OrdinalIgnoreCase ) )
			{
				// Animation graphs, with the clips they play, for the CS2 Animator.
				context.Add( ResourceType.GameResource, file[..^2], new AnimationGraphLoader( file ) );
			}
		}

		MountSounds( context );
		MountSurfaces( context );
		MountWeapons( context );
		MountSkins( context );
		MountDecals( context );

		Log.Info( $"Mounted {_files.Count} files from {_archives.Count} VPKs: {maps.Count} maps, {models} models, {materials} materials, {textures} textures." );

		IsMounted = _files.Count > 0;
		Current = this;
		EnsureGameTypes();
		return Task.CompletedTask;
	}

	/// <summary>
	/// The suffix of a lightmap's top mip, served as a texture of its own. CS2's 8192² lightmaps come with a second
	/// mip, and created whole they are larger than one texture may be (<see cref="TextureLoader.MaxUploadBytes"/>), so
	/// the lighting takes them in two parts and its shaders filter across both as the GPU would across one.
	/// </summary>
	internal const string LightmapTopMip = "_cs2mip0";

	/// <summary>
	/// The suffix of the mips below a lightmap's top one, served as a texture of their own.
	/// </summary>
	internal const string LightmapLowerMips = "_cs2mips1";

	private static bool IsLightmap( string file ) => file.StartsWith( "maps/", StringComparison.OrdinalIgnoreCase ) && file.Contains( "/lightmaps/", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// The path a part of a lightmap is served under, from the lightmap's (e.g. <c>maps/x/lightmaps/irradiance.vtex</c>).
	/// </summary>
	/// <summary>
	/// Where a post processing resource's colour correction lookup table is served, as a texture.
	/// </summary>
	internal static string ColorCorrectionPath( string vpost ) => $"{vpost[..^".vpost".Length]}_colorcorrection.vtex";

	internal static string LightmapPart( string path, bool top ) => $"{path[..^".vtex".Length]}{(top ? LightmapTopMip : LightmapLowerMips)}.vtex";

	protected override void Shutdown()
	{
		if ( Current == this )
			Current = null;

		CloseArchives();
	}

	private VpkArchive OpenArchive( string path )
	{
		var archive = new VpkArchive( path );
		_archives.Add( archive );

		// Earlier search paths win, like the game's own filesystem.
		foreach ( var file in archive.Files )
			_files.TryAdd( file, archive );

		return archive;
	}

	private void CloseArchives()
	{
		lock ( _files )
		{
			foreach ( var archive in _archives )
				archive.Dispose();

			_archives.Clear();
			_files.Clear();
			_itemsGame = null;
			_localization = null;
			_weaponsData = null;
			_knifeItems = null;
			ClearSurfaces();
		}
	}

	internal bool FileExists( string path ) => _files.ContainsKey( path );

	/// <summary>
	/// Reads a file from the mounted VPKs, by its path inside the game's content, e.g. <c>models/foo.vmdl_c</c>.
	/// </summary>
	internal byte[] ReadFile( string path, int maxLength = int.MaxValue )
	{
		VpkArchive archive;
		path = path.Replace( '\\', '/' );

		lock ( _files )
		{
			if ( !_files.TryGetValue( path, out archive ) )
				throw new FileNotFoundException( $"'{path}' is not in {Title}'s content.", path );
		}

		return archive.ReadFile( path, maxLength );
	}

	/// <summary>
	/// Loads a mounted model by the path a map refers to it by, e.g. <c>models/foo.vmdl</c>, or null if it isn't in
	/// the game's content.
	/// </summary>
	internal Model LoadModel( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( !FileExists( path + "_c" ) )
			return null;

		var model = Model.Load( $"mount://{Ident}/{path}" );
		return model is { IsError: false } ? model : null;
	}

	/// <summary>
	/// Loads a mounted material by the path a model or map refers to it by, e.g. <c>materials/foo.vmat</c>.
	/// Falls back to the default material when it isn't in the game's content.
	/// </summary>
	internal Material LoadMaterial( string path )
	{
		if ( !string.IsNullOrWhiteSpace( path ) )
		{
			path = path.Replace( '\\', '/' );

			if ( FileExists( path + "_c" ) && Material.Load( $"mount://{Ident}/{path}" ) is { IsValid: true } material )
				return material;
		}

		return Material.Load( "materials/default.vmat" );
	}

	/// <summary>
	/// Loads a mounted texture by the path a material refers to it by, e.g. <c>materials/foo_png_1234.vtex</c>.
	/// Returns null when it isn't in the game's content.
	/// </summary>
	internal Texture LoadTexture( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );

		if ( !FileExists( path + "_c" ) )
			return null;

		var texture = Texture.Load( $"mount://{Ident}/{path}", false );
		return texture is { IsValid: true, IsError: false } ? texture : null;
	}

	/// <summary>
	/// A texture's header, without necessarily reading its pixel data.
	/// </summary>
	internal CompiledTexture ReadTextureHeader( string path )
	{
		// The header and block table come before the pixel data, so a small prefix is usually enough.
		try
		{
			return new CompiledTexture( ReadFile( path + "_c", 64 * 1024 ) );
		}
		catch ( Exception e ) when ( e is InvalidDataException or ArgumentOutOfRangeException or IndexOutOfRangeException )
		{
			return new CompiledTexture( ReadFile( path + "_c" ) );
		}
	}
}
