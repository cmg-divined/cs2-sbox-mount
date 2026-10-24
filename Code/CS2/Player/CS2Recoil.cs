using System;

namespace CS2;

using Vector2 = System.Numerics.Vector2;

/// <summary>
/// Weapon recoil, CCSPlayer_AimPunchServices: a shot pushes a velocity that decays exponentially, and the punch angle
/// integrates it while decaying back to zero both exponentially and linearly. Ported from ValveResourceFormat's CS2
/// player.
/// </summary>
public sealed class CS2AimPunch
{
	/// <summary>Scale from the punch to where bullets go.</summary>
	public const float BulletScale = 2f;

	/// <summary>Scale from the punch to the rendered view: 0.45 of the bullet offset.</summary>
	public const float ViewScale = BulletScale * 0.45f;

	/// <summary>Scale from the punch to the first person weapon, another 0.325 of the bullet offset past the view.</summary>
	public const float ViewmodelScale = ViewScale + BulletScale * 0.325f;

	/// <summary>Share of each shot's kick that also goes into the view punch.</summary>
	public const float ViewPunchScale = 0.055f;

	private const float VelocityDecay = 4.5f;
	private const float ExponentialDecay = 8f;
	private const float LinearDecay = 18f;

	private Vector2 velocity;

	/// <summary>
	/// The punch as (pitch, yaw) degrees; negative pitch is up, as with Source angles.
	/// </summary>
	public Vector2 Punch { get; private set; }

	/// <summary>
	/// Kicks the punch for one shot, by the angle (0 straight up, positive to the right) and magnitude of the weapon's
	/// recoil pattern; returns the view punch the shot adds.
	/// </summary>
	public Vector2 Kick( float angle, float magnitude )
	{
		var radians = angle * MathF.PI / 180f;
		var kick = new Vector2( MathF.Cos( radians ), MathF.Sin( radians ) ) * magnitude;
		velocity -= kick;
		return -kick * ViewPunchScale;
	}

	public void Update( float deltaTime )
	{
		velocity *= MathF.Exp( -VelocityDecay * deltaTime );

		var punch = Punch * MathF.Exp( -ExponentialDecay * deltaTime );
		var length = punch.Length();
		var linear = LinearDecay * deltaTime;

		Punch = (length > linear ? punch * (1f - linear / length) : Vector2.Zero) + velocity * deltaTime;
	}
}

/// <summary>
/// The angle and magnitude of the kick for each shot of a spray, generated from a weapon's recoil keys in
/// weapons.vdata (m_nRecoilSeed, m_bIsFullAuto, m_flRecoilAngle/Variance, m_flRecoilMagnitude/Variance). Ported from
/// ValveResourceFormat's CS2 player.
/// </summary>
public sealed class CS2RecoilPattern
{
	/// <summary>Shots per mode; the shot index wraps past it.</summary>
	public const int Length = 64;

	private const int ModeCount = 2;
	private const float FullAutoSmoothing = 0.55f;
	private const int SuppressionShots = 4;
	private const float SuppressionFactor = 0.75f;

	private readonly (float Angle, float Magnitude)[] table = new (float, float)[ModeCount * Length];

	public CS2RecoilPattern( int seed, bool fullAuto, float[] angle, float[] angleVariance, float[] magnitude, float[] magnitudeVariance )
	{
		var random = new CS2UniformRandomStream();

		for ( var mode = 0; mode < ModeCount; mode++ )
		{
			random.SetSeed( seed );

			var shotAngle = 0f;
			var shotMagnitude = 0f;

			for ( var shot = 0; shot < Length; shot++ )
			{
				var newAngle = angle[mode] + random.RandomFloat( -angleVariance[mode], angleVariance[mode] );
				var newMagnitude = magnitude[mode] + random.RandomFloat( -magnitudeVariance[mode], magnitudeVariance[mode] );

				// Automatic fire drifts from one kick to the next rather than jumping around
				if ( fullAuto && shot > 0 )
				{
					shotAngle += (newAngle - shotAngle) * FullAutoSmoothing;
					shotMagnitude += (newMagnitude - shotMagnitude) * FullAutoSmoothing;
				}
				else
				{
					shotAngle = newAngle;
					shotMagnitude = newMagnitude;
				}

				// and its first few kicks ramp up to full strength
				if ( fullAuto && shot < SuppressionShots )
					shotMagnitude *= SuppressionFactor + (1f - SuppressionFactor) * (shot / (float)SuppressionShots);

				table[mode * Length + shot] = (shotAngle, shotMagnitude);
			}
		}
	}

	/// <summary>
	/// The kick for a shot at the weapon's recoil index.
	/// </summary>
	public (float Angle, float Magnitude) this[int mode, int shot] => table[mode * Length + (shot & (Length - 1))];
}

/// <summary>
/// Valve's seeded uniform random stream (Park-Miller with a Bays-Durham shuffle), as recoil patterns are generated with.
/// </summary>
public sealed class CS2UniformRandomStream
{
	private const int Multiplier = 16807;
	private const int Modulus = int.MaxValue;
	private const int Quotient = 127773;
	private const int Remainder = 2836;
	private const int TableSize = 32;
	private const int TableDivisor = 1 + (Modulus - 1) / TableSize;
	private const float Scale = 1f / Modulus;
	private const float MaxFraction = 1f - 1.2e-7f;

	private int state;
	private int previous;
	private readonly int[] table = new int[TableSize];

	public void SetSeed( int seed )
	{
		state = seed < 0 ? seed : -seed;
		previous = 0;
	}

	public float RandomFloat( float low = 0f, float high = 1f )
	{
		var fraction = MathF.Min( Scale * Next(), MaxFraction );
		return fraction * (high - low) + low;
	}

	private int Next()
	{
		if ( state <= 0 || previous == 0 )
		{
			state = -state < 1 ? 1 : -state;

			for ( var i = TableSize + 7; i >= 0; i-- )
			{
				Advance();
				if ( i < TableSize )
					table[i] = state;
			}

			previous = table[0];
		}

		Advance();

		var slot = previous / TableDivisor;
		previous = table[slot];
		table[slot] = state;
		return previous;
	}

	// Schrage's method: state * Multiplier % Modulus without overflowing 32 bits
	private void Advance()
	{
		var k = state / Quotient;
		state = Multiplier * (state - k * Quotient) - Remainder * k;
		if ( state < 0 )
			state += Modulus;
	}
}
