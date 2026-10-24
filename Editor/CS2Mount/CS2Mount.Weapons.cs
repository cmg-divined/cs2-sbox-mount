using System;
using System.IO;
using CS2;

namespace Sandbox.Mounting.CS2;

public sealed partial class CS2Mount
{
	private const string WeaponsDataPath = "scripts/weapons.vdata_c";

	/// <summary>
	/// The viewmodel graph's weapon_category for each vdata m_WeaponType.
	/// </summary>
	internal static readonly Dictionary<string, string> WeaponCategories = new( StringComparer.OrdinalIgnoreCase )
	{
		["WEAPONTYPE_KNIFE"] = "weapon_category_knife",
		["WEAPONTYPE_PISTOL"] = "weapon_category_pistol",
		["WEAPONTYPE_SUBMACHINEGUN"] = "weapon_category_smg",
		["WEAPONTYPE_RIFLE"] = "weapon_category_rifle",
		["WEAPONTYPE_SHOTGUN"] = "weapon_category_shotgun",
		["WEAPONTYPE_SNIPER_RIFLE"] = "weapon_category_sniper",
		["WEAPONTYPE_MACHINEGUN"] = "weapon_category_machinegun",
		["WEAPONTYPE_GRENADE"] = "weapon_category_grenade",
	};

	private Dictionary<string, KVObject> _weaponsData;

	/// <summary>
	/// Registers each gun of weapons.vdata and each knife of items_game at <c>weapons/name.cs2weapon</c>, and the list of
	/// them at <c>weapons/catalogue.cs2weapons</c>.
	/// </summary>
	private void MountWeapons( MountContext context )
	{
		// vdata's knife entries are served from items_game with the other knives; its numbered and other entries
		// aren't weapons a player carries.
		var knives = KnifeItems().Keys.Order().ToArray();
		var guns = WeaponsData.Where( x => x.Key.StartsWith( "weapon_", StringComparison.OrdinalIgnoreCase ) && x.Value.GetString( "m_szWorldModel" ) is { Length: > 0 } )
			.Select( x => x.Key ).Except( knives, StringComparer.OrdinalIgnoreCase ).Order().ToArray();

		foreach ( var name in guns.Concat( knives ) )
			context.Add( ResourceType.GameResource, $"weapons/{name.ToLowerInvariant()}.cs2weapon", new WeaponLoader( name ) );

		context.Add( ResourceType.GameResource, "weapons/catalogue.cs2weapons", new WeaponCatalogueLoader( guns, knives ) );
	}

	/// <summary>
	/// weapons.vdata's entries by name, each with its prefabs (_base) merged in: its own keys first.
	/// </summary>
	private Dictionary<string, KVObject> WeaponsData
	{
		get
		{
			if ( _weaponsData is { } loaded )
				return loaded;

			var raw = new Dictionary<string, KVObject>( StringComparer.OrdinalIgnoreCase );
			try
			{
				if ( FileExists( WeaponsDataPath ) && new ResourceFile( ReadFile( WeaponsDataPath ) ).ReadKeyValues( "DATA" ) is { } data )
				{
					foreach ( var key in data.Keys )
					{
						if ( data[key] is KVObject entry )
							raw[key] = entry;
					}
				}
			}
			catch ( Exception e ) when ( e is InvalidDataException or IOException )
			{
				Log.Warning( $"Couldn't read '{WeaponsDataPath}': {e.Message}" );
			}

			KVObject Merge( KVObject entry, int depth )
			{
				if ( depth > 8 || entry.GetString( "_base" ) is not { Length: > 0 } baseName || !raw.TryGetValue( baseName, out var baseEntry ) )
					return entry;

				var merged = new KVObject();
				foreach ( var key in entry.Keys )
					merged[key] = entry[key];

				var resolved = Merge( baseEntry, depth + 1 );
				foreach ( var key in resolved.Keys )
				{
					if ( !merged.ContainsKey( key ) )
						merged[key] = resolved[key];
				}

				return merged;
			}

			return _weaponsData = raw.Where( x => !x.Key.EndsWith( "_prefab", StringComparison.OrdinalIgnoreCase ) && x.Key != "weapon_base" )
				.ToDictionary( x => x.Key, x => Merge( x.Value, 0 ), StringComparer.OrdinalIgnoreCase );
		}
	}

	internal KVObject GetWeaponData( string name ) => WeaponsData.GetValueOrDefault( name );

	/// <summary>
	/// The knives of items_game by name: the default knives (prefab melee) and the rest (melee_unusual).
	/// </summary>
	internal Dictionary<string, KVObject> KnifeItems()
	{
		if ( _knifeItems is { } cached )
			return cached;

		var knives = new Dictionary<string, KVObject>( StringComparer.OrdinalIgnoreCase );
		var items = ItemsGame.GetObject( "items" );

		foreach ( var key in items?.Keys ?? [] )
		{
			if ( items.GetObject( key ) is not { } item || item.GetString( "prefab" ) is not ("melee" or "melee_unusual") )
				continue;

			if ( item.GetString( "name" ) is { Length: > 0 } name && item.GetString( "model_player" ) is { Length: > 0 } )
				knives.TryAdd( name, item );
		}

		return _knifeItems = knives;
	}

	private Dictionary<string, KVObject> _knifeItems;
}

/// <summary>
/// Loads a gun from weapons.vdata or a knife from items_game as a <see cref="CS2Weapon"/>. A knife's swings aren't timed
/// by vdata: its light and heavy misses' times are ValveResourceFormat's CS2 player's.
/// </summary>
internal sealed class WeaponLoader( string name ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		if ( Host.GetWeaponData( name ) is { } data && !Host.KnifeItems().ContainsKey( name ) )
		{
			var maxSpeed = data.GetFloatArray( "m_flMaxSpeed" );
			var clip = data.GetInt32( "m_iMaxClip1" );
			var reserve = data.GetInt32( "m_nPrimaryReserveAmmoMax" );

			return new CS2Weapon
			{
				Name = name,
				DisplayName = Host.Localize( Host.ItemsGame.GetObject( "prefabs" )?.GetObject( $"{name}_prefab" )?.GetString( "item_name" ) ) ?? name,
				Category = CS2Mount.WeaponCategories.GetValueOrDefault( data.GetString( "m_WeaponType" ) ?? "", "weapon_category_equipment" ),
				Model = data.GetString( "m_szWorldModel" ),
				MaxSpeed = maxSpeed.Length > 0 ? maxSpeed[0] : 250,
				CycleTime = data.GetFloat( "m_flCycleTime", 0.1f ),
				DeployDuration = data.GetFloat( "m_flDeployDuration", 1f ),
				ClipSize = clip,
				ReserveAmmo = data.GetBool( "m_bReserveAmmoAsClips" ) ? reserve * clip : reserve,
				FullAuto = data.GetBool( "m_bIsFullAuto" ),
				ShootSound = data.GetObject( "m_aShootSounds" )?.GetString( "WEAPON_SOUND_SINGLE" ),
				DetachableSilencer = data.GetString( "m_eSilencerType" ) == "WEAPONSILENCER_DETACHABLE",
				SilencedShootSound = data.GetObject( "m_aShootSounds" )?.GetString( "WEAPON_SOUND_SPECIAL1" ),
				RecoilSeed = data.GetInt32( "m_nRecoilSeed" ),
				RecoilAngle = data.GetFloatArray( "m_flRecoilAngle" ),
				RecoilAngleVariance = data.GetFloatArray( "m_flRecoilAngleVariance" ),
				RecoilMagnitude = data.GetFloatArray( "m_flRecoilMagnitude" ),
				RecoilMagnitudeVariance = data.GetFloatArray( "m_flRecoilMagnitudeVariance" ),
			};
		}

		if ( Host.KnifeItems().GetValueOrDefault( name ) is { } knife )
		{
			return new CS2Weapon
			{
				Name = name,
				DisplayName = Host.Localize( knife.GetString( "item_name" ) ) ?? name,
				Category = "weapon_category_knife",
				Model = knife.GetString( "model_player" ),
				MaxSpeed = 250,
				CycleTime = 0.4f,
				SecondaryCycleTime = 1f,
				DeployDuration = 1f,
			};
		}

		throw new InvalidOperationException( $"Weapon '{name}' isn't mounted." );
	}
}

internal sealed class WeaponCatalogueLoader( string[] guns, string[] knives ) : ResourceLoader<CS2Mount>
{
	protected override object Load() => new CS2WeaponCatalogue { Guns = guns, Knives = knives };
}
