using System;

namespace CS2;

/// <summary>
/// The player's sounds: footsteps, jumps and landings on the surface underfoot, the weapon's shots, and the sound
/// events of the first person animations (magazines, bolts, knife swings). Movement sounds follow ValveResourceFormat's
/// CS2 player (CBasePlayer::UpdateStepSound's timing, its speed thresholds and volumes).
/// </summary>
public sealed partial class CS2Player
{
	private const float StepSoundSpeedWalk = 90f;
	private const float StepSoundSpeedRun = 220f;
	private const float WalkingStepVolume = 0.8f;
	private const float LandMinFallSpeed = 270f;
	private const float GearVolume = 0.1f;
	private const float RunSpeed = 250f;
	private const string JumpSound = "Default.WalkJump";

	private float stepSoundTime;

	// Movement sounds follow the player while they play ("use_entity_position_if_local_player" in their data): left
	// where they started, footsteps would fall behind at a run and get louder as they recede (their curve peaks at
	// about 116 units).
	private readonly List<(SoundHandle Handle, Vector3 Offset)> movementSounds = [];

	private string GearSound => CurrentTeam == CS2Team.CounterTerrorist ? "Gear.JumpLand.CT" : "Gear.JumpLand.T";

	/// <summary>
	/// What's underfoot, as CS2 knows it: the surface a short trace down hits, else the default surface's.
	/// </summary>
	private CS2SurfaceInfo GroundSurface()
	{
		var feet = WorldPosition;
		var trace = Scene.Trace.Ray( feet + Vector3.Up * 2f, feet - Vector3.Up * 16f )
			.IgnoreGameObjectHierarchy( GameObject )
			.WithoutTags( controller.IgnoreLayers.ToArray() )
			.Run();

		return (trace.Hit ? CS2SurfaceInfo.For( trace.Surface ) : null) ?? CS2SurfaceInfo.Default;
	}

	private string Footstep( CS2SurfaceInfo surface ) => CurrentTeam == CS2Team.CounterTerrorist ? surface?.FootstepCounterTerrorist : surface?.FootstepTerrorist;

	private void PlayMovementSound( string name, float volume = 1f )
	{
		if ( CS2SoundEvent.Find( name ) is not { } sound )
			return;

		if ( sound.Play( WorldPosition, volume ) is { IsValid: true } handle )
			movementSounds.Add( (handle, sound.PositionOffset) );
	}

	/// <summary>
	/// A step every 0.4 seconds at a walking pace, 0.3 at a run, a tenth longer ducked; none below 130 units a second
	/// or while walking quietly.
	/// </summary>
	private void StepSounds( float dt, bool walking, bool ducking )
	{
		FollowMovementSounds();

		var speed = controller.Velocity.Length;
		var quietSpeed = RunSpeed * WalkSpeedModifier;

		if ( speed < quietSpeed || walking )
		{
			// Standing still keeps the timer armed, so moving again doesn't step at once.
			if ( speed * speed < 10f )
				SetStepSoundTime( false, ducking );

			return;
		}

		stepSoundTime = MathF.Max( stepSoundTime - dt, 0f );
		if ( stepSoundTime > 0f || speed < StepSoundSpeedWalk || !controller.IsOnGround )
			return;

		var walkingPace = speed < StepSoundSpeedRun;
		SetStepSoundTime( walkingPace, ducking );
		PlayMovementSound( Footstep( GroundSurface() ), walkingPace ? WalkingStepVolume : 1f );
	}

	private void SetStepSoundTime( bool walking, bool ducking ) => stepSoundTime = (walking ? 0.4f : 0.3f) + (ducking ? 0.1f : 0f);

	/// <summary>
	/// A jump: a loud step when it's taken at more than a walk, the jump, and the gear rattling.
	/// </summary>
	private void JumpSounds( Vector3 velocity )
	{
		if ( velocity.Length > WalkSpeedModifier * RunSpeed )
			PlayMovementSound( Footstep( GroundSurface() ) );

		PlayMovementSound( JumpSound );
		PlayMovementSound( GearSound, GearVolume );
	}

	/// <summary>
	/// A landing faster than 270 units a second (a jump lands at about 302): the thud on the surface and the gear.
	/// </summary>
	private void LandSounds( float fallSpeed )
	{
		if ( fallSpeed <= LandMinFallSpeed )
			return;

		PlayMovementSound( GroundSurface()?.Land );
		PlayMovementSound( GearSound, GearVolume );
	}

	private void FollowMovementSounds()
	{
		for ( var i = movementSounds.Count - 1; i >= 0; i-- )
		{
			var (handle, offset) = movementSounds[i];
			if ( !handle.IsValid || !handle.IsPlaying )
			{
				movementSounds.RemoveAt( i );
				continue;
			}

			handle.Position = WorldPosition + offset;
		}
	}

	/// <summary>
	/// The sound events the first person animations crossed this update, played where they say: at the eyes, else at
	/// the player.
	/// </summary>
	private void AnimationSounds( CS2Animator animator, Vector3 eye )
	{
		foreach ( var (name, atEyes) in animator.SampledSoundEvents )
			CS2SoundEvent.Play( name, atEyes ? eye : WorldPosition );
	}
}
