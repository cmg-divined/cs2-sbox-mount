using System;
using System.Text.Json.Serialization;
using CS2.Animation;

namespace CS2;

/// <summary>
/// A CS2 AnimGraph 2 graph (a <c>.vnmgraph</c>) as the CS2 mount serves it, at <c>mount://cs2/....vnmgraph</c>: the
/// graph and every file it uses (child graphs, clips and skeletons), as their KeyValues data. <see cref="CS2Animator"/>
/// plays it.
/// </summary>
public sealed class CS2AnimationGraph : GameResource, IAnimationFileLoader
{
	/// <summary>
	/// The graph's own path, e.g. <c>animation/graphs/worldmodel/worldmodel.vnmgraph</c>.
	/// </summary>
	[Hide, JsonIgnore]
	public string Root { get; set; }

	/// <summary>
	/// The graph and the files it uses, by path.
	/// </summary>
	[Hide, JsonIgnore]
	public IReadOnlyDictionary<string, KVObject> Files { get; set; } = new Dictionary<string, KVObject>();

	public KVObject Load( string path ) => path is not null && Files.TryGetValue( path.Replace( '\\', '/' ), out var data ) ? data : null;

	/// <summary>
	/// A mounted graph by the path CS2 refers to it by, or null when the CS2 mount has no such file.
	/// </summary>
	public static CS2AnimationGraph Find( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( path.EndsWith( "_c" ) )
			path = path[..^2];

		if ( !path.StartsWith( "mount://" ) )
			path = $"mount://cs2/{path}";

		return GameResource.Load<CS2AnimationGraph>( path ) is { Root: not null } graph ? graph : null;
	}
}
