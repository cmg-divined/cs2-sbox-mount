using System;
using System.Text.Json.Serialization;

namespace CS2;

/// <summary>
/// A weapon finish (items_game <c>paint_kits</c>).
/// </summary>
public sealed class CS2PaintKit
{
	/// <summary>
	/// Its number (the paint kit index the game's items carry).
	/// </summary>
	public int Id { get; set; }

	/// <summary>
	/// Its name, e.g. <c>cu_ak47_asiimov</c>.
	/// </summary>
	public string Name { get; set; }

	/// <summary>
	/// Its English name (description_tag in csgo_english), e.g. Asiimov.
	/// </summary>
	public string DisplayName { get; set; }

	/// <summary>
	/// Which version of a finish that shares its name with others this is, from the kit's name (Doppler and Gamma
	/// Doppler phases and gems); null otherwise. Not shown by the game.
	/// </summary>
	public string Variant { get; set; }

	/// <summary>
	/// items_game's style: 1 solid, 2 hydrographic, 3 spray paint, 4 anodized, 5 anodized multicoloured, 6 anodized
	/// airbrushed, 7 custom paint, 8 patina, 9 gunsmith (one more than the shader's F_PAINT_STYLE).
	/// </summary>
	public int Style { get; set; }

	/// <summary>
	/// The float range items of this finish are made with (wear_remap_min and _max; 0.06 to 0.8 when unset, as
	/// items_game's default kit).
	/// </summary>
	public float WearMin { get; set; }
	public float WearMax { get; set; } = 1;

	/// <summary>
	/// Whether it's made for a weapon's CS:GO model (use_legacy_model): weapons that have both show that one.
	/// </summary>
	public bool LegacyModel { get; set; }

	/// <summary>
	/// Whether it's a glove finish (composited from gloves/paints/).
	/// </summary>
	public bool Glove { get; set; }

	/// <summary>
	/// The composite material that paints it (composite_material_path, or weapons/paints/legacy/name.vcompmat as the
	/// game makes it when unset).
	/// </summary>
	public string CompositeMaterial { get; set; }

	/// <summary>
	/// items_game's rarity name, e.g. legendary.
	/// </summary>
	public string Rarity { get; set; }

	/// <summary>
	/// The weapons it comes on, e.g. <c>weapon_ak47</c>.
	/// </summary>
	public List<string> Weapons { get; set; } = [];

	/// <summary>
	/// The float the game would give a wear of 0 to 1: held to the kit's range. Items carry floats already within it
	/// (the item server rolls them in the range); this keeps a hand-set float to what an item could have.
	/// </summary>
	public float ClampWear( float wear ) => Math.Clamp( wear, WearMin, Math.Max( WearMin, WearMax ) );

	public override string ToString() => Variant is null ? $"{DisplayName} ({Name})" : $"{DisplayName} {Variant} ({Name})";
}

/// <summary>
/// A paintable glove (items_game items of prefab hands_paintable), e.g. <c>sporty_gloves</c>.
/// </summary>
public sealed class CS2GloveItem
{
	public string Name { get; set; }
	public string DisplayName { get; set; }

	/// <summary>
	/// Its model, e.g. <c>agents/models/shared/arms/glove_sporty/glove_sporty.vmdl</c>.
	/// </summary>
	public string Model { get; set; }

	public override string ToString() => $"{DisplayName} ({Name})";
}

/// <summary>
/// The paint kits the CS2 mount serves (<c>mount://cs2/weapons/paints/catalogue.cs2paintkits</c>): every weapon paint
/// kit of items_game with a composite material, and the weapons each comes on: the loot lists' <c>[kit]weapon</c>
/// entries, and the inventory icons the game ships for every weapon and finish it sells
/// (panorama/images/econ/default_generated/weapon_kit_light_png), which are where knives' finishes are found.
/// </summary>
public sealed class CS2PaintKits : GameResource
{
	[Hide, JsonIgnore] public List<CS2PaintKit> Kits { get; set; } = [];

	/// <summary>
	/// The paintable gloves.
	/// </summary>
	[Hide, JsonIgnore] public List<CS2GloveItem> Gloves { get; set; } = [];

	private Dictionary<string, CS2PaintKit> _byName;
	private Dictionary<string, List<CS2PaintKit>> _byWeapon;

	private static CS2PaintKits _loaded;

	/// <summary>
	/// The catalogue, or an empty one when the CS2 mount isn't there.
	/// </summary>
	public static CS2PaintKits Load()
	{
		if ( _loaded is { Kits.Count: > 0 } )
			return _loaded;

		var loaded = GameResource.Load<CS2PaintKits>( "mount://cs2/weapons/paints/catalogue.cs2paintkits" );
		if ( loaded is { Kits.Count: > 0 } )
			_loaded = loaded;

		return loaded ?? new CS2PaintKits();
	}

	/// <summary>
	/// A paint kit by name (e.g. <c>aa_fade</c>), or null.
	/// </summary>
	public static CS2PaintKit Find( string name )
	{
		if ( string.IsNullOrWhiteSpace( name ) )
			return null;

		var catalogue = Load();
		catalogue._byName ??= catalogue.Kits.GroupBy( x => x.Name, StringComparer.OrdinalIgnoreCase ).ToDictionary( x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase );
		return catalogue._byName.GetValueOrDefault( name.Trim() );
	}

	/// <summary>
	/// A paintable glove by name (e.g. <c>sporty_gloves</c>), or null.
	/// </summary>
	public static CS2GloveItem FindGlove( string name )
	{
		return string.IsNullOrWhiteSpace( name ) ? null : Load().Gloves.FirstOrDefault( x => string.Equals( x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase ) );
	}

	/// <summary>
	/// The paint kits a weapon comes with (e.g. <c>weapon_ak47</c>, <c>weapon_knife_karambit</c>), by English name.
	/// </summary>
	public static IReadOnlyList<CS2PaintKit> ForWeapon( string weapon )
	{
		if ( string.IsNullOrWhiteSpace( weapon ) )
			return [];

		var catalogue = Load();
		catalogue._byWeapon ??= catalogue.Kits
			.SelectMany( kit => kit.Weapons.Select( w => (Weapon: w, Kit: kit) ) )
			.GroupBy( x => x.Weapon, StringComparer.OrdinalIgnoreCase )
			.ToDictionary( x => x.Key, x => x.Select( y => y.Kit ).OrderBy( y => y.DisplayName, StringComparer.OrdinalIgnoreCase ).ThenBy( y => y.Variant ).ToList(), StringComparer.OrdinalIgnoreCase );

		return catalogue._byWeapon.TryGetValue( weapon.Trim(), out var kits ) ? kits : [];
	}
}

/// <summary>
/// The loadout slot a paint kit setting is for.
/// </summary>
public enum CS2PaintKitSlot
{
	Primary,
	Knife,
	Gloves,
}

/// <summary>
/// Marks a string property as a paint kit name for a <see cref="CS2Player"/> loadout slot: the inspector offers the
/// finishes the slot's weapon comes in.
/// </summary>
[AttributeUsage( AttributeTargets.Property )]
public sealed class CS2PaintKitAttribute( CS2PaintKitSlot slot ) : Attribute
{
	public CS2PaintKitSlot Slot => slot;
}

/// <summary>
/// Marks a string property as a glove name (items_game hands_paintable): the inspector offers the paintable gloves.
/// </summary>
[AttributeUsage( AttributeTargets.Property )]
public sealed class CS2GloveAttribute : Attribute
{
}
