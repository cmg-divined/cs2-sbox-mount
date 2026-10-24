using System;

namespace CS2;

/// <summary>
/// The guns a player can carry as their primary, by the game's names (weapons.vdata).
/// </summary>
public enum CS2Gun
{
	[Title( "AK-47" )] Ak47,
	[Title( "M4A4" )] M4a1,
	[Title( "M4A1-S" )] M4a1Silencer,
	[Title( "AWP" )] Awp,
	[Title( "AUG" )] Aug,
	[Title( "SG 553" )] Sg556,
	[Title( "FAMAS" )] Famas,
	[Title( "Galil AR" )] Galilar,
	[Title( "SSG 08" )] Ssg08,
	[Title( "SCAR-20" )] Scar20,
	[Title( "G3SG1" )] G3sg1,
	[Title( "M249" )] M249,
	[Title( "Negev" )] Negev,
	[Title( "MAC-10" )] Mac10,
	[Title( "MP9" )] Mp9,
	[Title( "MP7" )] Mp7,
	[Title( "MP5-SD" )] Mp5sd,
	[Title( "UMP-45" )] Ump45,
	[Title( "P90" )] P90,
	[Title( "PP-Bizon" )] Bizon,
	[Title( "Nova" )] Nova,
	[Title( "XM1014" )] Xm1014,
	[Title( "Sawed-Off" )] Sawedoff,
	[Title( "MAG-7" )] Mag7,
	[Title( "Desert Eagle" )] Deagle,
	[Title( "R8 Revolver" )] Revolver,
	[Title( "Glock-18" )] Glock,
	[Title( "USP-S" )] UspSilencer,
	[Title( "P2000" )] Hkp2000,
	[Title( "P250" )] P250,
	[Title( "Five-SeveN" )] Fiveseven,
	[Title( "Tec-9" )] Tec9,
	[Title( "CZ75-Auto" )] Cz75a,
	[Title( "Dual Berettas" )] Elite,
}

/// <summary>
/// The knives a player can carry, by the game's names (items_game); Default is the team's own.
/// </summary>
public enum CS2Knife
{
	[Title( "Default" )] Default,
	[Title( "Karambit" )] Karambit,
	[Title( "Butterfly Knife" )] Butterfly,
	[Title( "M9 Bayonet" )] M9Bayonet,
	[Title( "Bayonet" )] Bayonet,
	[Title( "Flip Knife" )] Flip,
	[Title( "Gut Knife" )] Gut,
	[Title( "Huntsman Knife" )] Tactical,
	[Title( "Falchion Knife" )] Falchion,
	[Title( "Bowie Knife" )] SurvivalBowie,
	[Title( "Shadow Daggers" )] Push,
	[Title( "Paracord Knife" )] Cord,
	[Title( "Survival Knife" )] Canis,
	[Title( "Ursus Knife" )] Ursus,
	[Title( "Navaja Knife" )] GypsyJackknife,
	[Title( "Nomad Knife" )] Outdoor,
	[Title( "Stiletto Knife" )] Stiletto,
	[Title( "Talon Knife" )] Widowmaker,
	[Title( "Skeleton Knife" )] Skeleton,
	[Title( "Kukri Knife" )] Kukri,
	[Title( "Classic Knife" )] Css,
}

/// <summary>
/// A finish's wear tier, by its float: Factory New under 0.07, Minimal Wear under 0.15, Field-Tested under 0.38,
/// Well-Worn under 0.45, Battle-Scarred from there to 1.
/// </summary>
public enum CS2Wear
{
	[Title( "Factory New" )] FactoryNew,
	[Title( "Minimal Wear" )] MinimalWear,
	[Title( "Field-Tested" )] FieldTested,
	[Title( "Well-Worn" )] WellWorn,
	[Title( "Battle-Scarred" )] BattleScarred,
}

public static class CS2LoadoutNames
{
	private static readonly float[] WearUpperBounds = [0.07f, 0.15f, 0.38f, 0.45f, 1f];

	/// <summary>
	/// The wear tier a float falls in.
	/// </summary>
	public static CS2Wear WearOf( float wear )
	{
		for ( var i = 0; i < WearUpperBounds.Length - 1; i++ )
		{
			if ( wear < WearUpperBounds[i] )
				return (CS2Wear)i;
		}

		return CS2Wear.BattleScarred;
	}

	/// <summary>
	/// A float in the middle of a wear tier.
	/// </summary>
	public static float FloatOf( this CS2Wear wear )
	{
		var index = (int)wear;
		var lower = index > 0 ? WearUpperBounds[index - 1] : 0f;
		return (lower + WearUpperBounds[index]) * 0.5f;
	}

	/// <summary>
	/// The game's name for a gun, e.g. weapon_m4a1_silencer.
	/// </summary>
	public static string WeaponName( this CS2Gun gun ) => gun switch
	{
		CS2Gun.M4a1Silencer => "weapon_m4a1_silencer",
		CS2Gun.UspSilencer => "weapon_usp_silencer",
		_ => $"weapon_{gun.ToString().ToLowerInvariant()}",
	};

	/// <summary>
	/// The game's name for a knife: the team's default (weapon_knife, weapon_knife_t) for Default.
	/// </summary>
	public static string WeaponName( this CS2Knife knife, CS2Team team ) => knife switch
	{
		CS2Knife.Default => team == CS2Team.Terrorist ? "weapon_knife_t" : "weapon_knife",
		CS2Knife.Bayonet => "weapon_bayonet",
		CS2Knife.M9Bayonet => "weapon_knife_m9_bayonet",
		CS2Knife.SurvivalBowie => "weapon_knife_survival_bowie",
		CS2Knife.GypsyJackknife => "weapon_knife_gypsy_jackknife",
		_ => $"weapon_knife_{knife.ToString().ToLowerInvariant()}",
	};
}
