using System.Text.Json.Serialization;

namespace CS2;

/// <summary>
/// A CS2 weapon as the CS2 mount serves it, at <c>mount://cs2/weapons/name.cs2weapon</c>: a gun from scripts/weapons.vdata
/// (its prefabs merged in), or a knife from items_game. What the player needs of it: its model, how fast it lets
/// its holder move, its timings, ammo, recoil and shot sound.
/// </summary>
public sealed class CS2Weapon : GameResource
{
	/// <summary>
	/// The game's name for it, e.g. <c>weapon_ak47</c>: the viewmodel graph's weapon_type.
	/// </summary>
	[Hide, JsonIgnore] public string Name { get; set; }

	/// <summary>
	/// Its English name, e.g. AK-47.
	/// </summary>
	[Hide, JsonIgnore] public string DisplayName { get; set; }

	/// <summary>
	/// The viewmodel graph's weapon_category, e.g. <c>weapon_category_rifle</c>.
	/// </summary>
	[Hide, JsonIgnore] public string Category { get; set; }

	[Hide, JsonIgnore] public string Model { get; set; }

	[Hide, JsonIgnore] public float MaxSpeed { get; set; } = 250;
	[Hide, JsonIgnore] public float CycleTime { get; set; } = 0.1f;
	[Hide, JsonIgnore] public float SecondaryCycleTime { get; set; } = 1f;
	[Hide, JsonIgnore] public float DeployDuration { get; set; } = 1f;
	[Hide, JsonIgnore] public int ClipSize { get; set; }
	[Hide, JsonIgnore] public int ReserveAmmo { get; set; }
	[Hide, JsonIgnore] public bool FullAuto { get; set; }

	/// <summary>
	/// The sound event of a shot without a silencer (vdata m_aShootSounds' WEAPON_SOUND_SINGLE), played at the player;
	/// its own offset lifts it to about the eyes.
	/// </summary>
	[Hide, JsonIgnore] public string ShootSound { get; set; }

	/// <summary>
	/// Whether it has a silencer that comes off (m_eSilencerType WEAPONSILENCER_DETACHABLE: the M4A1-S and USP-S), and
	/// its shot with the silencer on (WEAPON_SOUND_SPECIAL1). It's carried with the silencer on.
	/// </summary>
	[Hide, JsonIgnore] public bool DetachableSilencer { get; set; }
	[Hide, JsonIgnore] public string SilencedShootSound { get; set; }

	/// <summary>
	/// The recoil keys (m_nRecoilSeed, m_flRecoilAngle/Variance, m_flRecoilMagnitude/Variance), each for the normal and
	/// the alternate mode; empty for knives.
	/// </summary>
	[Hide, JsonIgnore] public int RecoilSeed { get; set; }
	[Hide, JsonIgnore] public float[] RecoilAngle { get; set; } = [];
	[Hide, JsonIgnore] public float[] RecoilAngleVariance { get; set; } = [];
	[Hide, JsonIgnore] public float[] RecoilMagnitude { get; set; } = [];
	[Hide, JsonIgnore] public float[] RecoilMagnitudeVariance { get; set; } = [];

	private CS2RecoilPattern _recoil;

	/// <summary>
	/// The kick of each shot of a spray, or null for a weapon without recoil keys.
	/// </summary>
	public CS2RecoilPattern Recoil => _recoil ??= RecoilAngle.Length >= 2 && RecoilAngleVariance.Length >= 2 && RecoilMagnitude.Length >= 2 && RecoilMagnitudeVariance.Length >= 2
		? new CS2RecoilPattern( RecoilSeed, FullAuto, RecoilAngle, RecoilAngleVariance, RecoilMagnitude, RecoilMagnitudeVariance )
		: null;

	public bool IsKnife => Category == "weapon_category_knife";

	/// <summary>
	/// A mounted weapon by its game name (e.g. <c>weapon_m4a1</c>, <c>weapon_knife_karambit</c>), or null.
	/// </summary>
	public static CS2Weapon Find( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		return GameResource.Load<CS2Weapon>( $"mount://cs2/weapons/{name.ToLowerInvariant()}.cs2weapon" ) is { Name: not null } weapon ? weapon : null;
	}
}

/// <summary>
/// The weapons the CS2 mount serves, by name: the guns of weapons.vdata and the knives of items_game.
/// </summary>
public sealed class CS2WeaponCatalogue : GameResource
{
	[Hide, JsonIgnore] public string[] Guns { get; set; } = [];
	[Hide, JsonIgnore] public string[] Knives { get; set; } = [];

	public static CS2WeaponCatalogue Load() => GameResource.Load<CS2WeaponCatalogue>( "mount://cs2/weapons/catalogue.cs2weapons" );
}
