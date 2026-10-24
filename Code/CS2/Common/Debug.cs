using System;

namespace CS2;

/// <summary>
/// The ported ValveResourceFormat code's debug asserts. s&amp;box compiles game code in debug with
/// System.Diagnostics.Debug off limits, so the ported code asserts through this instead, which checks
/// nothing: the asserts guard invariants of well formed game data, already tested against ValveResourceFormat.
/// </summary>
internal static class Debug
{
	public static void Assert( bool condition )
	{
	}

	public static void Assert( bool condition, string message )
	{
	}
}
