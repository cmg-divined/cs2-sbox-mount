using System;
using System.Text.RegularExpressions;
using CS2;

namespace Sandbox.Mounting.CS2;

public sealed partial class CS2Mount
{
	/// <summary>
	/// Registers what weapon and glove finishes need: every composite material (vcompmat) at its own path, each weapon
	/// and glove model's paintable materials beside it (<c>weapons/models/x/model.cs2skin</c>), and the paint kit
	/// catalogue at <c>weapons/paints/catalogue.cs2paintkits</c>.
	/// </summary>
	private void MountSkins( MountContext context )
	{
		foreach ( var file in _files.Keys )
		{
			if ( file.EndsWith( ".vcompmat_c", StringComparison.OrdinalIgnoreCase ) )
				context.Add( ResourceType.GameResource, file[..^2], new CompositeMaterialLoader( file ) );
			else if ( file.EndsWith( ".vmdl_c", StringComparison.OrdinalIgnoreCase ) && (file.StartsWith( "weapons/models/", StringComparison.OrdinalIgnoreCase ) || file.StartsWith( "agents/models/shared/arms/", StringComparison.OrdinalIgnoreCase )) )
				context.Add( ResourceType.GameResource, $"{file[..^".vmdl_c".Length]}.cs2skin", new SkinModelLoader( file ) );
		}

		context.Add( ResourceType.GameResource, "weapons/paints/catalogue.cs2paintkits", new PaintKitCatalogueLoader() );
	}

	/// <summary>
	/// A material's own parameters and string attributes, as the composite material system reads a material; null when
	/// it isn't in the game's content.
	/// </summary>
	internal CS2MaterialParams ReadMaterialParams( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( path.EndsWith( "_c" ) )
			path = path[..^2];

		if ( !FileExists( path + "_c" ) )
			return null;

		var material = new CompiledMaterial( ReadFile( path + "_c" ) );
		var result = new CS2MaterialParams { Path = path, Shader = material.ShaderName };

		foreach ( var (name, value) in material.IntParams )
			result.Values[name] = CS2CompositeValue.Of( value );

		foreach ( var (name, value) in material.FloatParams )
			result.Values[name] = CS2CompositeValue.Of( value );

		foreach ( var (name, value) in material.VectorParams )
		{
			float At( int i ) => i < value.Length ? value[i] : 0;
			result.Values[name] = CS2CompositeValue.Of( new Vector4( At( 0 ), At( 1 ), At( 2 ), At( 3 ) ) );
		}

		foreach ( var (name, value) in material.TextureParams )
			result.Values[name] = CS2CompositeValue.Of( value );

		// Attributes as text, whatever their type (gloves' handedness, "left", is an int).
		foreach ( var (name, value) in material.IntAttributes )
			result.Attributes[name] = value.ToString( System.Globalization.CultureInfo.InvariantCulture );

		foreach ( var (name, value) in material.FloatAttributes )
			result.Attributes[name] = value.ToString( System.Globalization.CultureInfo.InvariantCulture );

		foreach ( var (name, value) in material.StringAttributes )
			result.Attributes[name] = value.Replace( '\\', '/' );

		return result;
	}

	/// <summary>
	/// The inventory icons the game ships for each weapon (or glove) and finish it sells
	/// (<c>panorama/images/econ/default_generated/weapon_ak47_cu_ak47_asiimov_light_png.vtex_c</c>), as the weapon and
	/// the rest of the name.
	/// </summary>
	internal IEnumerable<(string Weapon, string Kit)> GeneratedIconPairs( IReadOnlyCollection<string> weapons )
	{
		const string Prefix = "panorama/images/econ/default_generated/";
		const string Suffix = "_light_png.vtex_c";

		var byLength = weapons.OrderByDescending( x => x.Length ).ToArray();
		foreach ( var file in _files.Keys )
		{
			if ( !file.StartsWith( Prefix, StringComparison.OrdinalIgnoreCase ) || !file.EndsWith( Suffix, StringComparison.OrdinalIgnoreCase ) )
				continue;

			var name = file[Prefix.Length..^Suffix.Length];
			var weapon = byLength.FirstOrDefault( x => name.Length > x.Length + 1 && name.StartsWith( x + "_", StringComparison.OrdinalIgnoreCase ) );
			if ( weapon is not null )
				yield return (weapon, name[(weapon.Length + 1)..]);
		}
	}
}

/// <summary>
/// Loads a composite material (vcompmat_c) as a <see cref="CS2CompositeMaterial"/>: its assembly procedure with the
/// files it includes merged in (theirs first; a container of an alias already there merges into it, its loose variables
/// by name), and the parameters of the specific materials its containers name.
/// </summary>
internal sealed class CompositeMaterialLoader( string filePath ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var composite = new CS2CompositeMaterial { Path = filePath.EndsWith( "_c" ) ? filePath[..^2] : filePath };
		Merge( composite, filePath, 0 );

		foreach ( var container in composite.Containers )
		{
			if ( container.Source == CS2CompositeSource.SpecificMaterial && !string.IsNullOrEmpty( container.Material ) && !composite.Materials.ContainsKey( container.Material ) )
			{
				if ( Host.ReadMaterialParams( container.Material ) is { } material )
					composite.Materials[container.Material] = material;
			}
		}

		return composite;
	}

	private void Merge( CS2CompositeMaterial composite, string path, int depth )
	{
		path = path.Replace( '\\', '/' );
		if ( !path.EndsWith( "_c" ) )
			path += "_c";

		if ( depth > 8 || !Host.FileExists( path ) || new ResourceFile( Host.ReadFile( path ) ).ReadKeyValues( "DATA" ) is not { } data )
			return;

		// The first editor point holds the procedures (one in every vcompmat the game ships).
		var point = data.GetObjects( "m_Points" ).FirstOrDefault();
		foreach ( var procedure in point?.GetObjects( "m_vecCompositeMaterialAssemblyProcedures" ) ?? [] )
		{
			foreach ( var include in procedure.GetStringArray( "m_vecCompMatIncludes" ) )
			{
				if ( include.Length > 0 )
					Merge( composite, include, depth + 1 );
			}

			foreach ( var filter in procedure.GetObjects( "m_vecMatchFilters" ) )
				composite.MatchFilters.Add( ReadMatchFilter( filter ) );

			foreach ( var container in procedure.GetObjects( "m_vecCompositeInputContainers" ) )
				MergeContainer( composite.Containers, ReadContainer( container ) );

			foreach ( var mutator in procedure.GetObjects( "m_vecPropertyMutators" ) )
				composite.Mutators.Add( ReadMutator( mutator ) );
		}
	}

	private static void MergeContainer( List<CS2CompositeContainer> containers, CS2CompositeContainer container )
	{
		var existing = containers.FirstOrDefault( x => string.Equals( x.Alias, container.Alias, StringComparison.OrdinalIgnoreCase ) );
		if ( existing is null )
		{
			containers.Add( container );
			return;
		}

		existing.Source = container.Source;
		if ( !string.IsNullOrEmpty( container.Material ) )
			existing.Material = container.Material;
		if ( !string.IsNullOrEmpty( container.Attribute ) )
			existing.Attribute = container.Attribute;

		foreach ( var variable in container.Variables )
		{
			existing.Variables.RemoveAll( x => string.Equals( x.Name, variable.Name, StringComparison.OrdinalIgnoreCase ) );
			existing.Variables.Add( variable );
		}
	}

	/// <summary>
	/// An enum value as the compiled vcompmats store them (its name); a number picks from the names in order.
	/// </summary>
	private static T Enum<T>( KVObject obj, string key, string prefix, T fallback ) where T : struct, System.Enum
	{
		switch ( obj[key] )
		{
			case string name:
			{
				var trimmed = name.StartsWith( prefix, StringComparison.Ordinal ) ? name[prefix.Length..] : name;
				return EnumNames<T>.ByName.TryGetValue( trimmed.Replace( "_", "" ), out var value ) ? value : fallback;
			}

			case null:
				return fallback;

			case var number:
			{
				var index = (int)KVValue.ToInt64( number );
				var values = System.Enum.GetValues<T>();
				return index >= 0 && index < values.Length ? values[index] : fallback;
			}
		}
	}

	private static class EnumNames<T> where T : struct, System.Enum
	{
		public static readonly Dictionary<string, T> ByName = System.Enum.GetValues<T>().ToDictionary( x => x.ToString(), x => x, StringComparer.OrdinalIgnoreCase );
	}

	private static CS2CompositeMatchFilter ReadMatchFilter( KVObject filter )
	{
		var type = filter.GetString( "m_nCompositeMaterialMatchFilterType" ) switch
		{
			"MATCH_FILTER_MATERIAL_ATTRIBUTE_EXISTS" => CS2CompositeMatchFilterType.MaterialAttributeExists,
			"MATCH_FILTER_MATERIAL_ATTRIBUTE_EQUALS" => CS2CompositeMatchFilterType.MaterialAttributeEquals,
			"MATCH_FILTER_MATERIAL_SHADER" => CS2CompositeMatchFilterType.MaterialShader,
			"MATCH_FILTER_MATERIAL_NAME_SUBSTR" => CS2CompositeMatchFilterType.MaterialNameSubstring,
			"MATCH_FILTER_MATERIAL_PROPERTY_EXISTS" => CS2CompositeMatchFilterType.MaterialPropertyExists,
			_ => CS2CompositeMatchFilterType.Unknown,
		};

		return new CS2CompositeMatchFilter
		{
			Type = type,
			Filter = filter.GetString( "m_strMatchFilter" ),
			Value = filter.GetString( "m_strMatchValue" ),
			PassWhenTrue = filter.GetBool( "m_bPassWhenTrue", true ),
		};
	}

	private static CS2CompositeContainer ReadContainer( KVObject container )
	{
		var source = container.GetString( "m_nCompositeMaterialInputContainerSourceType" ) switch
		{
			"CONTAINER_SOURCE_TYPE_TARGET_MATERIAL" => CS2CompositeSource.TargetMaterial,
			"CONTAINER_SOURCE_TYPE_MATERIAL_FROM_TARGET_ATTR" => CS2CompositeSource.MaterialFromTargetAttribute,
			"CONTAINER_SOURCE_TYPE_SPECIFIC_MATERIAL" => CS2CompositeSource.SpecificMaterial,
			"CONTAINER_SOURCE_TYPE_TARGET_INSTANCE_MATERIAL" => CS2CompositeSource.TargetInstanceMaterial,
			"CONTAINER_SOURCE_TYPE_VARIABLE_FROM_TARGET_ATTR" => CS2CompositeSource.VariableFromTargetAttribute,
			_ => CS2CompositeSource.LooseVariables,
		};

		var attribute = container.GetString( "m_strAttrName" );
		if ( source == CS2CompositeSource.VariableFromTargetAttribute )
			attribute = container.GetString( "m_strAttrNameForVar" );

		return new CS2CompositeContainer
		{
			Alias = container.GetString( "m_strAlias", "" ),
			Source = source,
			Material = container.GetString( "m_strSpecificContainerMaterial" )?.Replace( '\\', '/' ),
			Attribute = attribute,
			Variables = container.GetObjects( "m_vecLooseVariables" ).Select( ReadVariable ).ToList(),
		};
	}

	private static CS2CompositeVariable ReadVariable( KVObject variable )
	{
		var type = Enum( variable, "m_nVariableType", "LOOSE_VARIABLE_TYPE_", CS2CompositeVariableType.Float1 );
		var result = new CS2CompositeVariable
		{
			Name = variable.GetString( "m_strName", "" ),
			Type = type,
			HasBounds = variable.GetBool( "m_bHasFloatBounds" ),
		};

		Vector4 Floats( string suffix ) => new(
			variable.GetFloat( "m_flValueFloatX" + suffix ), variable.GetFloat( "m_flValueFloatY" + suffix ),
			variable.GetFloat( "m_flValueFloatZ" + suffix ), variable.GetFloat( "m_flValueFloatW" + suffix ) );

		switch ( type )
		{
			case CS2CompositeVariableType.Boolean:
				result.Value = new Vector4( variable.GetBool( "m_bValueBoolean" ) ? 1 : 0, 0, 0, 0 );
				break;

			case >= CS2CompositeVariableType.Integer1 and <= CS2CompositeVariableType.Integer4:
				result.Value = new Vector4( variable.GetInt32( "m_nValueIntX" ), variable.GetInt32( "m_nValueIntY" ), variable.GetInt32( "m_nValueIntZ" ), variable.GetInt32( "m_nValueIntW" ) );
				break;

			case >= CS2CompositeVariableType.Float1 and <= CS2CompositeVariableType.Float4:
				result.Value = Floats( "" );
				result.Min = Floats( "_Min" );
				result.Max = Floats( "_Max" );
				break;

			case CS2CompositeVariableType.Color4:
			{
				// A colour's 0 to 255 bytes as the 0 to 1 colours the paint material's parameters hold (unverified
				// how client.dll converts it).
				var color = variable.GetFloatArray( "m_cValueColor4" );
				float At( int i, float fallback ) => i < color.Length ? color[i] / 255f : fallback;
				result.Value = new Vector4( At( 0, 0 ), At( 1, 0 ), At( 2, 0 ), At( 3, 1 ) );
				break;
			}

			case CS2CompositeVariableType.ResourceTexture:
				result.Text = variable.GetString( "m_strTextureRuntimeResourcePath", "" ).Replace( '\\', '/' );
				break;

			case CS2CompositeVariableType.ResourceMaterial:
				result.Text = variable.GetString( "m_strResourceMaterial", "" ).Replace( '\\', '/' );
				break;

			case CS2CompositeVariableType.String:
				result.Text = variable.GetString( "m_strString", "" );
				break;
		}

		return result;
	}

	private static CS2CompositeMutator ReadMutator( KVObject mutator )
	{
		var result = new CS2CompositeMutator
		{
			Type = Enum( mutator, "m_nMutatorCommandType", "COMP_MAT_PROPERTY_MUTATOR_", CS2CompositeMutatorType.Init ),
			Enabled = mutator.GetBool( "m_bEnabled", true ),
			Conditions = mutator.GetObjects( "m_vecConditions" ).Select( ReadCondition ).ToList(),
		};

		switch ( result.Type )
		{
			case CS2CompositeMutatorType.Init:
				result.Container = mutator.GetString( "m_strInitWith_Container" );
				break;

			case CS2CompositeMutatorType.CopyMatchingKeys:
				result.Container = mutator.GetString( "m_strCopyMatchingKeys_InputContainerSrc" );
				break;

			case CS2CompositeMutatorType.CopyProperty:
				result.Container = mutator.GetString( "m_strCopyProperty_InputContainerSrc" );
				result.Property = mutator.GetString( "m_strCopyProperty_InputContainerProperty" );
				result.TargetProperty = mutator.GetString( "m_strCopyProperty_TargetProperty" );
				break;

			case CS2CompositeMutatorType.SetValue:
				if ( mutator.GetObject( "m_nSetValue_Value" ) is { } value )
					result.Value = ReadVariable( value );
				break;

			case CS2CompositeMutatorType.GenerateTexture:
				result.TargetParameter = mutator.GetString( "m_strGenerateTexture_TargetParam" );
				result.Container = mutator.GetString( "m_strGenerateTexture_InitialContainer" );
				result.Resolution = mutator.GetInt32( "m_nResolution", 512 );
				result.Instructions = mutator.GetObjects( "m_vecTexGenInstructions" ).Select( ReadMutator ).ToList();
				break;

			case CS2CompositeMutatorType.ConditionalMutators:
				result.Mutators = mutator.GetObjects( "m_vecConditionalMutators" ).Select( ReadMutator ).ToList();
				break;

			case CS2CompositeMutatorType.RandomRollInputVariables:
				result.SeedVariable = mutator.GetString( "m_strRandomRollInputVars_SeedInputVar" );
				result.RollVariables = mutator.GetStringArray( "m_vecRandomRollInputVars_InputVarsToRoll" ).ToList();
				break;
		}

		return result;
	}

	private static CS2CompositeCondition ReadCondition( KVObject condition )
	{
		return new CS2CompositeCondition
		{
			Type = Enum( condition, "m_nMutatorCondition", "COMP_MAT_MUTATOR_CONDITION_INPUT_", CS2CompositeConditionType.ContainerValueEquals ),
			Container = condition.GetString( "m_strMutatorConditionContainerName" ),
			Variable = condition.GetString( "m_strMutatorConditionContainerVarName" ),
			Value = condition.GetString( "m_strMutatorConditionContainerVarValue" ),
			PassWhenTrue = condition.GetBool( "m_bPassWhenTrue", true ),
		};
	}
}

/// <summary>
/// Loads a weapon model's paintable materials as a <see cref="CS2SkinModel"/>: each material its meshes draw, with its
/// parameters, the materials its attributes name (composite_inputs), and the body group choices it's drawn in.
/// </summary>
internal sealed class SkinModelLoader( string filePath ) : ResourceLoader<CS2Mount>
{
	protected override object Load()
	{
		var model = new CompiledModel( Host.ReadFile( filePath ) );
		var result = new CS2SkinModel { Model = filePath[..^2] };
		var choices = model.BodyGroupChoices;
		var byPath = new Dictionary<string, CS2SkinModelMaterial>( StringComparer.OrdinalIgnoreCase );

		for ( var mesh = 0; mesh < model.Meshes.Count; mesh++ )
		{
			var mask = model.GetMeshGroupMask( mesh );
			var groups = new List<string>();
			for ( var i = 0; mask != ulong.MaxValue && i < model.MeshGroups.Length && i < 64; i++ )
			{
				if ( ((mask >> i) & 1) != 0 && choices[i] is not null )
					groups.Add( model.MeshGroups[i] );
			}

			foreach ( var drawCall in model.Meshes[mesh].DrawCalls )
			{
				var path = drawCall.Material.Replace( '\\', '/' );
				if ( path.Length == 0 )
					continue;

				if ( !byPath.TryGetValue( path, out var entry ) )
				{
					if ( Host.ReadMaterialParams( path ) is not { } material )
						continue;

					entry = byPath[path] = new CS2SkinModelMaterial { Material = material };
					foreach ( var (attribute, value) in material.Attributes )
					{
						if ( value.EndsWith( ".vmat", StringComparison.OrdinalIgnoreCase ) && Host.ReadMaterialParams( value ) is { } named )
							entry.AttributeMaterials[attribute] = named;
					}

					result.Materials.Add( entry );
				}

				if ( groups.Count == 0 )
					entry.Ungrouped = true;

				entry.MeshGroups.AddRange( groups.Where( x => !entry.MeshGroups.Contains( x ) ) );
			}
		}

		// The legacy model: a choice named ..._legacy, beside its group's first choice.
		for ( var i = 0; i < choices.Length; i++ )
		{
			if ( choices[i] is not { } choice || !(choice.ChoiceName ?? "").EndsWith( "_legacy", StringComparison.OrdinalIgnoreCase ) )
				continue;

			var first = choices.Select( ( x, index ) => (Choice: x, Index: index) ).FirstOrDefault( x => x.Choice is { } c && c.Group == choice.Group && c.Choice == 0 );
			result.LegacyBodyGroup = choice.Group;
			result.LegacyChoice = model.MeshGroups[i];
			result.LegacyChoiceIndex = choice.Choice;
			result.DefaultChoice = first.Choice is not null ? model.MeshGroups[first.Index] : null;
			result.DefaultChoiceIndex = 0;
			break;
		}

		return result;
	}
}

/// <summary>
/// Loads the paint kit catalogue (<see cref="CS2PaintKits"/>) from items_game and csgo_english: every paint kit with a
/// composite material (weapons/paints/legacy/name.vcompmat for weapons, gloves/paints/name.vcompmat for gloves, as
/// client.dll makes the paths, unless the kit names its own), the weapons and gloves each comes on (the loot lists'
/// entries and the game's generated inventory icons), and the paintable gloves (items of prefab hands_paintable).
/// </summary>
internal sealed class PaintKitCatalogueLoader : ResourceLoader<CS2Mount>
{
	private static readonly Regex LootEntry = new( @"^\[([^\]]+)\]([a-z0-9_]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled );

	protected override object Load()
	{
		var items = Host.ItemsGame;
		var paintKits = items.GetObject( "paint_kits" );
		var rarities = items.GetObject( "paint_kits_rarity" );

		// Kits leave out what the default kit (0) sets.
		var defaults = paintKits?.GetObject( "0" );
		var defaultMin = defaults?.GetFloat( "wear_remap_min", 0.06f ) ?? 0.06f;
		var defaultMax = defaults?.GetFloat( "wear_remap_max", 0.8f ) ?? 0.8f;

		var kits = new Dictionary<string, CS2PaintKit>( StringComparer.OrdinalIgnoreCase );
		foreach ( var key in paintKits?.Keys ?? [] )
		{
			if ( paintKits.GetObject( key ) is not { } entry || entry.GetString( "name" ) is not { Length: > 0 } name || name == "default" || name == "workshop_default" )
				continue;

			var composite = entry.GetString( "composite_material_path" ) is { Length: > 0 } path ? path.Replace( '\\', '/' ) : $"weapons/paints/legacy/{name}.vcompmat";
			if ( !Host.FileExists( composite + "_c" ) && Host.FileExists( $"gloves/paints/{name}.vcompmat_c" ) )
				composite = $"gloves/paints/{name}.vcompmat";

			if ( !Host.FileExists( composite + "_c" ) || composite.Contains( "/_shared", StringComparison.OrdinalIgnoreCase ) )
				continue;

			kits.TryAdd( name, new CS2PaintKit
			{
				Id = int.TryParse( key, out var id ) ? id : 0,
				Name = name,
				DisplayName = Localize( entry.GetString( "description_tag" ) ) ?? name,
				Variant = VariantOf( name ),
				Style = entry.GetInt32( "style" ),
				WearMin = entry.GetFloat( "wear_remap_min", defaultMin ),
				WearMax = entry.GetFloat( "wear_remap_max", defaultMax ),
				LegacyModel = entry.GetBool( "use_legacy_model" ),
				Glove = composite.StartsWith( "gloves/", StringComparison.OrdinalIgnoreCase ),
				CompositeMaterial = composite,
				Rarity = rarities?.GetString( name ),
			} );
		}

		// Which weapons each kit comes on: the loot lists' "[kit]weapon_x" keys, anywhere in items_game, and the
		// inventory icons (the only place knives' finishes are listed).
		// The weapons, and the paintable gloves.
		var weapons = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var gloves = new List<CS2GloveItem>();
		var itemDefinitions = items.GetObject( "items" );
		foreach ( var key in itemDefinitions?.Keys ?? [] )
		{
			if ( itemDefinitions.GetObject( key ) is not { } item || item.GetString( "name" ) is not { Length: > 0 } name )
				continue;

			if ( name.StartsWith( "weapon_", StringComparison.OrdinalIgnoreCase ) )
			{
				weapons.Add( name );
			}
			else if ( item.GetString( "prefab" ) == "hands_paintable" && item.GetString( "model_player" ) is { Length: > 0 } model )
			{
				weapons.Add( name );
				gloves.Add( new CS2GloveItem { Name = name, DisplayName = Localize( item.GetString( "item_name" ) ) ?? name, Model = model.Replace( '\\', '/' ) } );
			}
		}

		void Pair( string weapon, string kit )
		{
			if ( weapons.Contains( weapon ) && kits.TryGetValue( kit, out var paintKit ) && !paintKit.Weapons.Contains( weapon, StringComparer.OrdinalIgnoreCase ) )
				paintKit.Weapons.Add( weapon.ToLowerInvariant() );
		}

		void Walk( KVObject obj, int depth )
		{
			foreach ( var key in obj.Keys )
			{
				if ( LootEntry.Match( key ) is { Success: true } match )
					Pair( match.Groups[2].Value, match.Groups[1].Value );
				else if ( depth < 6 && obj[key] is KVObject child )
					Walk( child, depth + 1 );
			}
		}

		Walk( items, 0 );

		foreach ( var (weapon, kit) in Host.GeneratedIconPairs( weapons ) )
			Pair( weapon, kit );

		return new CS2PaintKits
		{
			Kits = kits.Values.OrderBy( x => x.Id ).ToList(),
			Gloves = gloves.OrderBy( x => x.DisplayName, StringComparer.OrdinalIgnoreCase ).ToList(),
		};
	}

	private string Localize( string token ) => string.IsNullOrEmpty( token ) ? null : Host.Localize( token );

	/// <summary>
	/// The phase or gem of a finish sharing its name with others (Doppler, Gamma Doppler), from the kit's name.
	/// </summary>
	private static string VariantOf( string name )
	{
		if ( Regex.Match( name, @"_phase(\d)" ) is { Success: true } phase )
			return $"Phase {phase.Groups[1].Value}";

		foreach ( var (gem, title) in new[] { ("ruby", "Ruby"), ("sapphire", "Sapphire"), ("blackpearl", "Black Pearl"), ("emerald", "Emerald") } )
		{
			if ( name.Contains( gem + "_marbleized", StringComparison.OrdinalIgnoreCase ) )
				return title;
		}

		return null;
	}
}
