using System;

namespace CS2;

/// <summary>
/// A weapon's StatTrak module, as CS2 shows it (the client's C_StattrakModule, from client.dll): <c>weapons/models/shared/
/// stattrak/stattrak_module.vmdl</c>, or <c>stattrak_module_knife.vmdl</c> for knives (the game's test: the weapon's
/// entity name contains <c>weapon_knife</c>; here: a model under <c>weapons/models/knife/</c>, where every knife model is),
/// at the weapon model's <c>stattrak</c> attachment, <c>stattrak_legacy</c> when its <c>body</c> body group shows a
/// <c>_legacy</c> choice (the CS:GO model), its display (csgo_weapon with F_STATTRAK, cs2_weapon here) showing the kill
/// count, given to it as the CS2StatTrakValue render attribute (the game's display reads it as g_nStatTrakValue, set by
/// the material's dynamic expression <c>return $ent_stattrak;</c>). Follows the weapon's shadow setting and its viewmodel
/// lighting attributes.
/// </summary>
public sealed class CS2StatTrak : Component
{
	private const string ModulePath = "mount://cs2/weapons/models/shared/stattrak/stattrak_module.vmdl";
	private const string KnifeModulePath = "mount://cs2/weapons/models/shared/stattrak/stattrak_module_knife.vmdl";

	/// <summary>
	/// The weapon renderer the module sits on.
	/// </summary>
	public SkinnedModelRenderer Weapon { get; set; }

	/// <summary>
	/// The module's own renderer.
	/// </summary>
	public ModelRenderer Renderer { get; set; }

	/// <summary>
	/// The kill count shown (the display has six digits).
	/// </summary>
	public int Kills { get; set; }

	private static readonly string[] CopiedVector4s = ["CS2LightingTransform0", "CS2LightingTransform1", "CS2LightingTransform2"];
	private static readonly string[] CopiedVectors = ["CS2LightingNormal0", "CS2LightingNormal1", "CS2LightingNormal2"];

	/// <summary>
	/// Shows (or with <paramref name="enabled"/> false, removes) a StatTrak module with a kill count on a weapon renderer.
	/// Call after <see cref="CS2WeaponSkin.Apply"/>, which picks the model the module sits on. Returns the module's
	/// renderer, or null.
	/// </summary>
	public static ModelRenderer Apply( SkinnedModelRenderer weapon, bool enabled, int kills )
	{
		if ( !weapon.IsValid() )
			return null;

		var existing = weapon.GameObject.Children.Select( x => x.Components.Get<CS2StatTrak>() ).FirstOrDefault( x => x.IsValid() );
		var knife = weapon.Model?.ResourcePath?.Contains( "weapons/models/knife/", StringComparison.OrdinalIgnoreCase ) ?? false;
		if ( !enabled || Model.Load( knife ? KnifeModulePath : ModulePath ) is not { IsError: false } model )
		{
			existing?.GameObject.Destroy();
			return null;
		}

		if ( !existing.IsValid() )
		{
			var moduleObject = new GameObject( weapon.GameObject, true, "StatTrak" );
			existing = moduleObject.Components.Create<CS2StatTrak>();
			existing.Renderer = moduleObject.Components.Create<ModelRenderer>();
		}

		if ( existing.Renderer.Model != model )
			existing.Renderer.Model = model;

		existing.Weapon = weapon;
		existing.Kills = Math.Clamp( kills, 0, 999999 );
		existing.Place();
		return existing.Renderer;
	}

	protected override void OnPreRender() => Place();

	/// <summary>
	/// At the weapon's attachment, as the weapon is drawn, showing the count.
	/// </summary>
	private void Place()
	{
		if ( !Weapon.IsValid() || !Renderer.IsValid() )
			return;

		// In the weapon's own space (the module is its child), so it takes the weapon's pose and the viewmodel's squeeze.
		// The game also checks a value (hashed name 231760911, unidentified) before picking the legacy one.
		var legacy = Weapon.GetCS2BodyGroup( "body" )?.EndsWith( "_legacy", StringComparison.OrdinalIgnoreCase ) ?? false;
		var attachment = legacy ? "stattrak_legacy" : "stattrak";
		var transform = Weapon.GetAttachment( attachment, false ) ?? Weapon.GetAttachment( "stattrak", false );
		Renderer.Enabled = Weapon.Enabled && transform is not null;
		if ( transform is { } at )
			Renderer.LocalTransform = at;

		Renderer.RenderType = Weapon.RenderType;
		Renderer.Attributes.Set( "CS2StatTrakValue", (float)Kills );

		// Only what the weapon has (NaN marks an attribute it doesn't).
		foreach ( var name in CopiedVector4s )
		{
			var value = Weapon.Attributes.GetVector4( name, new Vector4( float.NaN ) );
			if ( !float.IsNaN( value.x ) )
				Renderer.Attributes.Set( name, value );
		}

		foreach ( var name in CopiedVectors )
		{
			var value = Weapon.Attributes.GetVector( name, new Vector3( float.NaN ) );
			if ( !float.IsNaN( value.x ) )
				Renderer.Attributes.Set( name, value );
		}
	}
}
