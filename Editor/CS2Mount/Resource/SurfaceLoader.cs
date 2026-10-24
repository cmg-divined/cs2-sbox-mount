using System;
using CS2;

namespace Sandbox.Mounting.CS2;

/// <summary>
/// Makes an s&amp;box <see cref="Surface"/> of a CS2 surface property: its physics (density, elasticity, friction), each
/// from the nearest surface in its base chain that has it.
/// </summary>
internal sealed class SurfaceLoader( string name ) : ResourceLoader<CS2Mount>
{
	/// <summary>
	/// s&amp;box's acoustic surface by what the name contains; CS2 has no such thing (its audioparams don't map onto it).
	/// </summary>
	private static readonly (string Keyword, AudioSurface Surface)[] AudioSurfaces =
	[
		("metal", AudioSurface.Metal), ("grate", AudioSurface.Metal), ("chain", AudioSurface.Metal), ("vent", AudioSurface.Metal),
		("glass", AudioSurface.Glass), ("wood", AudioSurface.Wood), ("ladder", AudioSurface.Wood), ("cardboard", AudioSurface.Wood),
		("tile", AudioSurface.Ceramic), ("porcelain", AudioSurface.Ceramic),
		("sand", AudioSurface.Sand), ("dirt", AudioSurface.Soil), ("mud", AudioSurface.Soil), ("gravel", AudioSurface.Soil), ("grass", AudioSurface.Soil),
		("water", AudioSurface.Water), ("wet", AudioSurface.Water), ("slime", AudioSurface.Water), ("puddle", AudioSurface.Water),
		("concrete", AudioSurface.Concrete), ("asphalt", AudioSurface.Concrete), ("rock", AudioSurface.Concrete), ("brick", AudioSurface.Concrete),
		("stucco", AudioSurface.Concrete), ("boulder", AudioSurface.Concrete), ("plaster", AudioSurface.Concrete),
	];

	protected override object Load()
	{
		var chain = Host.SurfaceChain( name ).ToList();

		float Physics( string key, float fallback )
		{
			foreach ( var data in chain )
			{
				if ( data.GetObject( "physics" ) is { } physics && physics.ContainsKey( key ) )
					return physics.GetFloat( key );
			}

			return fallback;
		}

		var audio = AudioSurfaces.FirstOrDefault( x => name.Contains( x.Keyword, StringComparison.OrdinalIgnoreCase ) );

		return new Surface
		{
			Description = $"{name} (Counter-Strike 2)",
			Friction = Physics( "friction", 0.8f ),
			Elasticity = Physics( "elasticity", 0.25f ),
			Density = Physics( "density", 2000f ),
			Tags = "cs2",
			AudioSurface = audio.Keyword is null ? AudioSurface.Generic : audio.Surface,
		};
	}
}

/// <summary>
/// Loads what CS2 does with a surface property as a <see cref="CS2SurfaceInfo"/>: sounds from the surface itself, else
/// the nearest base that has them; the impact effect entry of the nearest surface in the chain that has one. Unverified:
/// that the game walks the base chain for footsteps and impact effects as it does for audiosounds (the scripts list
/// only some surfaces); and the landing sound, which is the footstep's
/// with its team prefix swapped for Land_ (CT_Concrete.StepLeft, Land_Concrete.StepLeft), else Land_Default.
/// </summary>
internal sealed class SurfaceInfoLoader( string name ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var names = Host.SurfaceNameChain( name ).Append( "default" ).ToList();
		var chain = Host.SurfaceChain( name ).Concat( Host.SurfaceChain( "default" ) ).ToList();

		string Audio( string key )
		{
			foreach ( var data in chain )
			{
				if ( data.GetObject( "audiosounds" )?.GetString( key ) is { Length: > 0 } sound )
					return sound;
			}

			return null;
		}

		string Script( KVObject script, string key )
		{
			var entries = script?.GetObjects( "SurfacePropertiesList" ).ToList() ?? [];
			foreach ( var surface in names )
			{
				var entry = entries.FirstOrDefault( x => string.Equals( x.GetString( "surfacePropertyName" ), surface, StringComparison.OrdinalIgnoreCase ) );
				if ( entry?.GetString( key ) is { Length: > 0 } value )
					return value;
			}

			return null;
		}

		var footsteps = Host.Footsteps;
		var counterTerrorist = Script( footsteps.GetObject( "ct_player" ), "walkleft" ) ?? "CT_Default.StepLeft";
		var terrorist = Script( footsteps.GetObject( "t_player" ), "walkleft" ) ?? "T_Default.StepLeft";

		var land = counterTerrorist.StartsWith( "CT_", StringComparison.OrdinalIgnoreCase ) ? $"Land_{counterTerrorist[3..]}" : null;
		if ( !Host.SoundEventExists( land ) )
			land = "Land_Default.StepLeft";

		// Impact effects come as a whole entry: the nearest surface in the chain that has one.
		var effectEntries = Host.ImpactEffects.GetObjects( "SurfacePropertiesList" ).ToList();
		var effects = names.Select( surface => effectEntries.FirstOrDefault( x => string.Equals( x.GetString( "surfacePropertyName" ), surface, StringComparison.OrdinalIgnoreCase ) ) ).FirstOrDefault( x => x is not null );

		return new CS2SurfaceInfo
		{
			Name = name,
			FootstepCounterTerrorist = counterTerrorist,
			FootstepTerrorist = terrorist,
			Land = land,
			BulletImpact = Audio( "bulletimpact" ),
			ImpactHard = Audio( "impacthard" ),
			ImpactSoft = Audio( "impactsoft" ),
			ImpactEffect = effects?.GetString( "effect" ),
			ImpactDecal = effects?.GetString( "impactDecalName" ),
			ImpactGrazingDecal = effects?.GetString( "impactGrazingDecalName" ),
		};
	}
}
