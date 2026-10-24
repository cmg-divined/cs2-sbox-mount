using System;
using System.Reflection;

namespace Sandbox.Mounting.CS2;

public sealed partial class CS2Mount
{
	/// <summary>
	/// Makes the CS2 components (entities, map data...) known to the running
	/// game's type library. In this project they come with the game's own code. Installed on its own in s&amp;box's
	/// mount folder, the mount carries them in its own assembly, which games know nothing of, and scenes would drop
	/// them when saved or loaded. Done on mount and again before building a map, as loading a game rebuilds its type
	/// library.
	/// </summary>
	internal static void EnsureGameTypes()
	{
		try
		{
			var library = Game.TypeLibrary;
			if ( library is null || library.GetType( typeof( global::CS2.CS2Entity ) ) is not null )
				return;

			var addAssembly = library.GetType().GetMethod( "AddAssembly", BindingFlags.Instance | BindingFlags.NonPublic, [typeof( Assembly ), typeof( bool )] );
			addAssembly?.Invoke( library, [typeof( global::CS2.CS2Entity ).Assembly, false] );
		}
		catch ( Exception e )
		{
			global::Sandbox.Internal.GlobalGameNamespace.Log.Warning( $"CS2: couldn't make the CS2 components known to the game: {e.Message}" );
		}
	}
}
