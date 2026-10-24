using System;

namespace CS2;

/// <summary>
/// Shots: a trace along the aim, and what it hits: the surface's bullet impact sound and its impact decal.
/// </summary>
/// <remarks>
/// Not ported yet: spread and inaccuracy (shots go exactly along the punched aim), penetration, the impact particle
/// effects (CS2 particles need a port of their own), tracers, damage and impacts on props.
/// </remarks>
public sealed partial class CS2Player
{
	/// <summary>
	/// How far a shot reaches (weapons.vdata m_flRange, 8192 for the rifles).
	/// </summary>
	private const float BulletRange = 8192f;

	/// <summary>
	/// What shots pass through: brushes marked to let bullets by, the player and grenade clips, triggers and water.
	/// </summary>
	private static readonly string[] BulletIgnoreTags = ["passbullets", "playerclip", "npcclip", "csgo_grenadeclip", "trigger", "water"];

	/// <summary>
	/// A hit counts as grazing below this incidence (the cosine between the shot and the surface), jittered per shot
	/// so hits near it leave either mark (ValveResourceFormat's CS2 values).
	/// </summary>
	private const float GrazingIncidence = 0.55f;
	private const float GrazingIncidenceVariance = 0.1f;

	/// <summary>
	/// How long an impact decal stays; the game keeps a fixed number of them instead (not ported).
	/// </summary>
	private const float DecalLifetime = 60f;

	private void FireBullet()
	{
		var eye = WorldPosition + Vector3.Up * EyeHeight;
		var punch = aimPunch.Punch;
		var direction = (EyeAngles + new Angles( punch.X, punch.Y, 0 )).Forward;

		var trace = Scene.Trace.Ray( eye, eye + direction * BulletRange )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( BulletIgnoreTags )
			.Run();

		if ( !trace.Hit || trace.Tags.Contains( "sky" ) )
			return;

		var surface = CS2SurfaceInfo.For( trace.Surface ) ?? CS2SurfaceInfo.Default;
		CS2SoundEvent.Play( surface?.BulletImpact, trace.HitPosition );

		var cutoff = GrazingIncidence + Game.Random.Float( -1f, 1f ) * GrazingIncidenceVariance;
		var grazing = -Vector3.Dot( trace.Normal, direction ) < cutoff;
		var groupName = grazing && !string.IsNullOrEmpty( surface?.ImpactGrazingDecal ) ? surface.ImpactGrazingDecal : surface?.ImpactDecal;

		var decal = CS2DecalGroup.Find( groupName )?.Place( Scene, trace.HitPosition, trace.Normal, DecalLifetime );
		if ( decal is null )
			return;

		// A grazing mark streaks towards the bottom of its texture: its top faces back along the shot.
		if ( grazing && decal.Components.Get<Decal>() is { } component )
		{
			decal.WorldRotation = Rotation.LookAt( -trace.Normal, -direction );
			component.Rotation = 0f;
		}

		// Stuck to what it hit, so it moves with it.
		if ( trace.GameObject is { } hit )
			decal.SetParent( hit, true );
	}
}
