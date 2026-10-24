using System;
using System.IO;
using CS2;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Loads a compiled AnimGraph 2 graph (vnmgraph_c) as a <see cref="CS2AnimationGraph"/>: the graph with every file it
/// uses (child graphs, clips and skeletons), so the animator loads a whole tree at once.
/// </summary>
internal sealed class AnimationGraphLoader( string filePath ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var root = filePath.EndsWith( "_c" ) ? filePath[..^2] : filePath;
		var files = new Dictionary<string, global::CS2.KVObject>( StringComparer.OrdinalIgnoreCase );
		var pending = new Queue<string>();
		pending.Enqueue( root );

		while ( pending.Count > 0 )
		{
			var path = pending.Dequeue();
			if ( files.ContainsKey( path ) || !Host.FileExists( path + "_c" ) )
				continue;

			var data = new ResourceFile( Host.ReadFile( path + "_c" ) ).ReadKeyValues( "DATA" );
			if ( data is null )
				continue;

			files[path] = ToGameKeyValues( data );

			// Graphs name their skeleton and the clips and graphs they play; clips name only their skeleton (and their
			// secondary animations', for the weapon a viewmodel holds).
			if ( data.GetString( "m_skeleton" ) is { Length: > 0 } skeleton )
				pending.Enqueue( skeleton.Replace( '\\', '/' ) );

			foreach ( var secondary in data.GetArray( "m_secondaryAnimations" ) ?? [] )
			{
				if ( secondary is KVObject animation && animation.GetString( "m_skeleton" ) is { Length: > 0 } secondarySkeleton )
					pending.Enqueue( secondarySkeleton.Replace( '\\', '/' ) );
			}

			foreach ( var resource in data.GetStringArray( "m_resources" ) )
			{
				if ( resource.Length > 0 )
					pending.Enqueue( resource.Replace( '\\', '/' ) );
			}
		}

		if ( !files.ContainsKey( root ) )
			throw new InvalidDataException( "Animation graph has no DATA block." );

		return new CS2AnimationGraph { Root = root, Files = files };
	}

	/// <summary>
	/// A KeyValues3 tree as the game code's KeyValues, keeping each value's type.
	/// </summary>
	internal static global::CS2.KVObject ToGameKeyValues( object value )
	{
		switch ( value )
		{
			case KVObject obj:
			{
				var collection = global::CS2.KVObject.Collection( obj.Count );
				foreach ( var key in obj.Keys )
					collection.Add( key, ToGameKeyValues( obj[key] ) );

				return collection;
			}

			case object[] array:
			{
				var elements = global::CS2.KVObject.Array( array.Length );
				foreach ( var element in array )
					elements.Add( ToGameKeyValues( element ) );

				return elements;
			}

			case byte[] blob: return global::CS2.KVObject.Blob( blob );
			case string text: return new global::CS2.KVObject( text );
			case bool flag: return new global::CS2.KVObject( flag );
			case short x: return new global::CS2.KVObject( x );
			case ushort x: return new global::CS2.KVObject( x );
			case int x: return new global::CS2.KVObject( x );
			case uint x: return new global::CS2.KVObject( x );
			case long x: return new global::CS2.KVObject( x );
			case ulong x: return new global::CS2.KVObject( x );
			case float x: return new global::CS2.KVObject( x );
			case double x: return new global::CS2.KVObject( x );
			default: return global::CS2.KVObject.Null();
		}
	}
}
