using System;
using CS2;

namespace Editor;

/// <summary>
/// The inspector for a paint kit name (<see cref="CS2PaintKitAttribute"/>): a drop-down of the finishes the slot's
/// weapon comes in, by English name (Doppler phases and gems told apart), and None. Before play the weapon is the
/// team's (both teams' for a random team); while playing, the one equipped. A name typed into the scene that isn't in
/// the list stays selectable.
/// </summary>
[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( CS2PaintKitAttribute ) } )]
public sealed class CS2PaintKitControlWidget : ControlWidget
{
	private readonly ComboBox _combo;
	private string _built;

	public CS2PaintKitControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		_combo = new ComboBox( this );
		Layout.Add( _combo );
	}

	// Rebuilt when the value or the slot's weapons change.
	protected override int ValueHash => HashCode.Combine( base.ValueHash, string.Join( ",", Weapons() ) );

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		var current = SerializedProperty.As.String ?? "";
		var weapons = Weapons();
		var key = $"{current}|{string.Join( ",", weapons )}";
		if ( !_combo.IsValid() || key == _built )
			return;

		_built = key;
		_combo.Clear();
		_combo.AddItem( "None", onSelected: () => Select( "" ), selected: current.Length == 0 );

		var listed = false;
		foreach ( var weapon in weapons )
		{
			var weaponName = weapons.Length > 1 ? CS2Weapon.Find( weapon )?.DisplayName ?? CS2PaintKits.FindGlove( weapon )?.DisplayName ?? weapon : null;
			foreach ( var kit in CS2PaintKits.ForWeapon( weapon ) )
			{
				var label = kit.Variant is null ? kit.DisplayName : $"{kit.DisplayName} ({kit.Variant})";
				if ( weaponName is not null )
					label = $"{weaponName} | {label}";

				var selected = string.Equals( kit.Name, current, StringComparison.OrdinalIgnoreCase );
				listed |= selected;
				_combo.AddItem( label, onSelected: () => Select( kit.Name ), description: kit.Name, selected: selected );
			}
		}

		if ( !listed && current.Length > 0 )
		{
			var kit = CS2PaintKits.Find( current );
			_combo.AddItem( kit is null ? current : $"{kit.DisplayName} ({current})", onSelected: () => Select( current ), selected: true );
		}
	}

	private void Select( string name )
	{
		if ( string.Equals( SerializedProperty.As.String ?? "", name, StringComparison.Ordinal ) )
			return;

		PropertyStartEdit();
		SerializedProperty.SetValue( name );
		PropertyFinishEdit();
	}

	/// <summary>
	/// The weapons the slot holds: the one equipped while playing, else the team's.
	/// </summary>
	private string[] Weapons()
	{
		var slot = SerializedProperty?.GetAttributes<CS2PaintKitAttribute>().FirstOrDefault()?.Slot ?? CS2PaintKitSlot.Primary;
		if ( SerializedProperty?.Parent?.Targets?.OfType<CS2Player>().FirstOrDefault() is not { } player )
			return [];

		if ( slot == CS2PaintKitSlot.Gloves )
			return string.IsNullOrWhiteSpace( player.Gloves ) ? [] : [player.Gloves.Trim()];

		if ( (slot == CS2PaintKitSlot.Knife ? player.KnifeWeapon : player.PrimaryWeapon) is { } equipped )
			return [equipped.Name];

		var teams = player.Team == CS2Team.Random ? new[] { CS2Team.CounterTerrorist, CS2Team.Terrorist } : new[] { player.Team };
		return teams.Select( team => slot == CS2PaintKitSlot.Knife
				? player.Knife.WeaponName( team )
				: (team == CS2Team.Terrorist ? player.TerroristPrimary : player.CounterTerroristPrimary).WeaponName() )
			.Distinct()
			.ToArray();
	}

	protected override void OnPaint()
	{
		// The combo paints itself.
	}
}

/// <summary>
/// The inspector for a glove name (<see cref="CS2GloveAttribute"/>): a drop-down of the paintable gloves by English
/// name, and Default (the agent's own).
/// </summary>
[CustomEditor( typeof( string ), WithAllAttributes = new[] { typeof( CS2GloveAttribute ) } )]
public sealed class CS2GloveControlWidget : ControlWidget
{
	private readonly ComboBox _combo;
	private string _built;

	public CS2GloveControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Row();
		_combo = new ComboBox( this );
		Layout.Add( _combo );
	}

	protected override void OnValueChanged()
	{
		base.OnValueChanged();

		var current = SerializedProperty.As.String ?? "";
		if ( !_combo.IsValid() || current == _built )
			return;

		_built = current;
		_combo.Clear();
		_combo.AddItem( "Default", onSelected: () => Select( "" ), selected: current.Length == 0 );

		foreach ( var glove in CS2PaintKits.Load().Gloves )
			_combo.AddItem( glove.DisplayName, onSelected: () => Select( glove.Name ), description: glove.Name, selected: string.Equals( glove.Name, current, StringComparison.OrdinalIgnoreCase ) );
	}

	private void Select( string name )
	{
		if ( string.Equals( SerializedProperty.As.String ?? "", name, StringComparison.Ordinal ) )
			return;

		PropertyStartEdit();
		SerializedProperty.SetValue( name );
		PropertyFinishEdit();
	}

	protected override void OnPaint()
	{
		// The combo paints itself.
	}
}
