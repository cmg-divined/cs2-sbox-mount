using System.Text.Json.Serialization;

namespace CS2;

/// <summary>
/// What CS2 does with a surface property, as the CS2 mount serves it beside the s&amp;box <see cref="Surface"/> it makes of
/// it (<c>mount://cs2/surfaces/cs2_name.surface</c>): the footsteps each team's players make on it, its impact sounds,
/// and the bullet impact effect and decals. Base surfaces fill in what a surface doesn't have.
/// </summary>
public sealed class CS2SurfaceInfo : GameResource
{
	/// <summary>
	/// The surface property's name, e.g. <c>concrete</c>.
	/// </summary>
	[Hide, JsonIgnore] public string Name { get; set; }

	/// <summary>
	/// Footstep sound events (scripts/surfaceproperties_footsteps.txt), e.g. <c>CT_Concrete.StepLeft</c>.
	/// </summary>
	[Hide, JsonIgnore] public string FootstepCounterTerrorist { get; set; }
	[Hide, JsonIgnore] public string FootstepTerrorist { get; set; }

	/// <summary>
	/// The landing sound event: the footstep's material with Land_ for the team, e.g. <c>Land_Concrete.StepLeft</c>.
	/// </summary>
	[Hide, JsonIgnore] public string Land { get; set; }

	/// <summary>
	/// Sound events from the surface's audiosounds, e.g. <c>Concrete.BulletImpact</c>.
	/// </summary>
	[Hide, JsonIgnore] public string BulletImpact { get; set; }
	[Hide, JsonIgnore] public string ImpactHard { get; set; }
	[Hide, JsonIgnore] public string ImpactSoft { get; set; }

	/// <summary>
	/// The bullet impact particle effect (scripts/surfaceproperties_impact_effects.txt), and its decal groups for a
	/// head-on and a grazing hit, e.g. <c>Impact.Concrete</c>.
	/// </summary>
	[Hide, JsonIgnore] public string ImpactEffect { get; set; }
	[Hide, JsonIgnore] public string ImpactDecal { get; set; }
	[Hide, JsonIgnore] public string ImpactGrazingDecal { get; set; }

	/// <summary>
	/// The CS2 surface of an s&amp;box surface the CS2 mount made, or null for other surfaces.
	/// </summary>
	public static CS2SurfaceInfo For( Surface surface )
	{
		if ( surface?.ResourcePath is not { } path || !path.StartsWith( "mount://cs2/surfaces/", System.StringComparison.OrdinalIgnoreCase ) )
			return null;

		return GameResource.Load<CS2SurfaceInfo>( System.IO.Path.ChangeExtension( path, ".cs2surf" ) ) is { Name: not null } info ? info : null;
	}

	/// <summary>
	/// The default surface's.
	/// </summary>
	public static CS2SurfaceInfo Default => GameResource.Load<CS2SurfaceInfo>( "mount://cs2/surfaces/cs2_default.cs2surf" );
}
