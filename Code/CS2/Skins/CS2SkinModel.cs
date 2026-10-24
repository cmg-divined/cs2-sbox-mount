using System;
using System.Text.Json.Serialization;

namespace CS2;

/// <summary>
/// A material of a weapon model a finish can paint, with what the composite material reads of it.
/// </summary>
public sealed class CS2SkinModelMaterial
{
	/// <summary>
	/// The material's own parameters and attributes (the composite's target).
	/// </summary>
	public CS2MaterialParams Material { get; set; }

	/// <summary>
	/// The materials its attributes name (its <c>composite_inputs</c>), by attribute.
	/// </summary>
	public Dictionary<string, CS2MaterialParams> AttributeMaterials { get; set; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The body group choices of the meshes that draw it, by mesh group name (e.g. <c>body_@1_#&amp;body_legacy</c>).
	/// </summary>
	public List<string> MeshGroups { get; set; } = [];

	/// <summary>
	/// Whether a mesh outside any body group draws it: it shows whatever the choices.
	/// </summary>
	public bool Ungrouped { get; set; }
}

/// <summary>
/// A weapon model's materials as the composite materials see them, as the CS2 mount serves it beside the model
/// (<c>mount://cs2/weapons/models/ak47/weapon_rif_ak47.cs2skin</c>). CS2 weapons that have been remade carry their
/// CS:GO model too, as the <c>body</c> body group's <c>body_legacy</c> choice (the new one is <c>body_hd</c>); paint
/// kits made for it (items_game <c>use_legacy_model</c>) are shown on it.
/// </summary>
public sealed class CS2SkinModel : GameResource
{
	/// <summary>
	/// The model's path, e.g. <c>weapons/models/ak47/weapon_rif_ak47.vmdl</c>.
	/// </summary>
	[Hide, JsonIgnore] public string Model { get; set; }

	[Hide, JsonIgnore] public List<CS2SkinModelMaterial> Materials { get; set; } = [];

	/// <summary>
	/// The body group with the legacy model, its legacy and default choices; null when the model has no legacy model.
	/// </summary>
	[Hide, JsonIgnore] public string LegacyBodyGroup { get; set; }
	[Hide, JsonIgnore] public string LegacyChoice { get; set; }
	[Hide, JsonIgnore] public int LegacyChoiceIndex { get; set; }
	[Hide, JsonIgnore] public string DefaultChoice { get; set; }
	[Hide, JsonIgnore] public int DefaultChoiceIndex { get; set; }

	/// <summary>
	/// Whether a material shows with the legacy model or the default one chosen: drawn outside any body group, by
	/// another body group, or by the chosen choice.
	/// </summary>
	public bool IsShown( CS2SkinModelMaterial material, bool legacy )
	{
		if ( material.Ungrouped || LegacyBodyGroup is null )
			return true;

		var chosen = legacy ? LegacyChoice : DefaultChoice;
		return material.MeshGroups.Any( x => x == chosen || !x.StartsWith( LegacyBodyGroup + "_@", StringComparison.Ordinal ) );
	}

	/// <summary>
	/// The material by its path.
	/// </summary>
	public CS2SkinModelMaterial FindMaterial( string path ) => Materials.FirstOrDefault( x => string.Equals( x.Material.Path, path, StringComparison.OrdinalIgnoreCase ) );

	/// <summary>
	/// A mounted weapon model's paintable materials by the model's path (or mount path), or null.
	/// </summary>
	public static CS2SkinModel Find( string model )
	{
		if ( string.IsNullOrWhiteSpace( model ) )
			return null;

		model = model.Replace( '\\', '/' );
		if ( model.StartsWith( "mount://cs2/", StringComparison.OrdinalIgnoreCase ) )
			model = model["mount://cs2/".Length..];

		if ( model.EndsWith( "_c" ) )
			model = model[..^2];

		if ( !model.EndsWith( ".vmdl", StringComparison.OrdinalIgnoreCase ) )
			return null;

		return GameResource.Load<CS2SkinModel>( $"mount://cs2/{model[..^".vmdl".Length]}.cs2skin" ) is { Model: not null } skinModel ? skinModel : null;
	}
}
