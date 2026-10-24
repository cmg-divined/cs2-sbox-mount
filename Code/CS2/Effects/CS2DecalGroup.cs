using System;
using System.Text.Json.Serialization;

namespace CS2;

/// <summary>
/// A CS2 decal group (scripts/decalgroups.vdata, e.g. Impact.Concrete) as the CS2 mount serves it, at
/// <c>mount://cs2/decals/name.cs2decal</c>: decals picked from by their probability, each its csgo_projected_decals
/// material's textures and size.
/// </summary>
/// <remarks>
/// Drawn with s&amp;box's own <see cref="Decal"/> rather than a port of csgo_projected_decals (not ported yet: its cutoff
/// angle softness, lightmap sampling, parallax sample counts, size and depth variances).
/// </remarks>
public sealed class CS2DecalGroup : GameResource
{
	[Hide, JsonIgnore] public string Name { get; set; }
	[Hide, JsonIgnore] public List<CS2DecalOption> Options { get; set; } = [];

	/// <summary>
	/// A mounted decal group by name (case doesn't matter), or null.
	/// </summary>
	public static CS2DecalGroup Find( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		return GameResource.Load<CS2DecalGroup>( $"mount://cs2/decals/{name.ToLowerInvariant()}.cs2decal" ) is { Name: not null } group ? group : null;
	}

	/// <summary>
	/// Puts one of the group's decals on a surface, picked by probability, projected into it along its normal; gone
	/// after <paramref name="lifetime"/> seconds (0 keeps it).
	/// </summary>
	public GameObject Place( Scene scene, Vector3 position, Vector3 normal, float lifetime = 0f )
	{
		if ( Pick() is not { } option || option.Definition() is not { } definition )
			return null;

		var go = scene.CreateObject();
		go.Name = $"decal {Name}";
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = position;
		go.WorldRotation = Rotation.LookAt( -normal );

		var decal = go.Components.Create<Decal>();
		decal.Decals = [definition];
		decal.Depth = option.Depth;
		decal.Parallax = option.HeightScale > 0f ? 1f : 0f;
		decal.Transient = true;
		if ( lifetime > 0f )
			decal.LifeTime = lifetime;

		return go;
	}

	private CS2DecalOption Pick()
	{
		var total = Options.Sum( x => MathF.Max( x.Probability, 0f ) );
		if ( total <= 0f )
			return Options.FirstOrDefault();

		var roll = Game.Random.Float( 0f, total );
		foreach ( var option in Options )
		{
			roll -= MathF.Max( option.Probability, 0f );
			if ( roll <= 0f )
				return option;
		}

		return Options[^1];
	}
}

/// <summary>
/// One decal of a group: its material's textures (paths in the mount), its world size, depth and parallax scale, and how
/// likely it's picked.
/// </summary>
public sealed class CS2DecalOption
{
	public string Material { get; set; }
	public float Probability { get; set; } = 1f;
	public string Color { get; set; }
	public string Normal { get; set; }
	public string Height { get; set; }
	public float Width { get; set; } = 4f;
	public float Length { get; set; } = 4f;
	public float Depth { get; set; } = 8f;
	public float HeightScale { get; set; }

	private DecalDefinition _definition;

	internal DecalDefinition Definition()
	{
		if ( _definition is not null )
			return _definition;

		var color = Load( Color );
		if ( color is null )
			return null;

		return _definition = new DecalDefinition
		{
			ColorTexture = color,
			NormalTexture = Load( Normal ),
			HeightTexture = Load( Height ),
			ParallaxStrength = HeightScale,
			Width = Width,
			Height = Length,
		};
	}

	private static Texture Load( string path ) => string.IsNullOrEmpty( path ) ? null : Texture.Load( $"mount://cs2/{path}", false ) is { IsValid: true, IsError: false } texture ? texture : null;
}
