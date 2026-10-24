using System;
using System.Globalization;

namespace CS2;

/// <summary>
/// A texture a composite material generates: the target parameter, the size the composite asks for, and the
/// generating material's parameters (its shader, e.g. csgo_customweapon.vfx, and what the instructions set).
/// </summary>
public sealed class CS2CompositePass
{
	public string TargetParameter { get; set; }
	public int Resolution { get; set; }
	public Dictionary<string, CS2CompositeValue> Values { get; set; }
}

/// <summary>
/// What a composite material makes of a target material: the final material's parameters (the target's, changed) and
/// the textures to generate. A generated texture's parameter holds <see cref="CS2CompositeProgram.GeneratedPrefix"/>
/// and the pass's target parameter.
/// </summary>
public sealed class CS2CompositeResult
{
	public Dictionary<string, CS2CompositeValue> Material { get; set; } = new( StringComparer.OrdinalIgnoreCase );
	public List<CS2CompositePass> Passes { get; set; } = [];
}

/// <summary>
/// Runs a composite material's assembly procedure on one target material, as client.dll's composite material system
/// does (from IDA: the mutator executor and the procedure runner of CCompositeMaterialManager, 2026-10-21 build):
///  - Containers are KeyValues: a material's are its own parameters ("Shader" and every parameter it sets, numbers as
///    four floats, textures as paths; materialsystem2's material-to-KeyValues), loose variables their values. The item's
///    econ_instance values (g_nRandomSeed, g_flWearAmount) are set over the vcompmat's own.
///  - Mutators run in order on the final material (empty until INIT copies a container in); each first checks its
///    conditions, each condition passing when its result equals PassWhenTrue.
///  - COPY_MATCHING_KEYS copies only the keys the target already has (keeping its Shader), so a paint material gets
///    the composite inputs' textures and the rolled wear and grunge placement only where its vmat names them.
///  - GENERATE_TEXTURE starts from a copy of the initial container and runs its instructions on that copy.
///  - RANDOM_ROLL_INPUT_VARIABLES seeds a CUniformRandomStreamImpl (tier0) with the seed variable (none rolled when
///    it's -1, seed 0 when it's missing), then for each listed container.variable.swizzle rolls RandomFloat( min, max )
///    for each of x, y, z, w the variable has and the swizzle names, in that order; a variable that isn't there, has
///    no bounds or isn't a float draws (and throws away) one number per swizzle letter, so later variables still get
///    the numbers they would.
/// Not ported: COPY_KEYS_WITH_SUFFIX, POP_INPUT_QUEUE and DRAW_TEXT (stickers, patches, name tags), generated textures
/// referenced by other passes (_texgen_ref).
/// </summary>
public static class CS2CompositeProgram
{
	public const string GeneratedPrefix = "_cs2texgen:";

	private sealed class Container
	{
		public CS2CompositeContainer Definition;
		public Dictionary<string, CS2CompositeValue> Values;
		public List<CS2CompositeVariable> Variables;
	}

	/// <summary>
	/// Runs <paramref name="composite"/> on <paramref name="target"/>. <paramref name="attributeMaterials"/> are the
	/// materials the target's attributes name; the item's pattern seed and wear go into econ_instance.
	/// </summary>
	public static CS2CompositeResult Run( CS2CompositeMaterial composite, CS2MaterialParams target, IReadOnlyDictionary<string, CS2MaterialParams> attributeMaterials, int seed, float wear )
	{
		var containers = new Dictionary<string, Container>( StringComparer.OrdinalIgnoreCase );

		foreach ( var definition in composite.Containers )
		{
			var container = new Container
			{
				Definition = definition,
				Values = new Dictionary<string, CS2CompositeValue>( StringComparer.OrdinalIgnoreCase ),
				Variables = definition.Variables.Select( x => x.Clone() ).ToList(),
			};

			switch ( definition.Source )
			{
				case CS2CompositeSource.TargetMaterial:
				case CS2CompositeSource.TargetInstanceMaterial:
					CopyMaterial( target, container.Values );
					break;

				case CS2CompositeSource.MaterialFromTargetAttribute:
					if ( attributeMaterials?.GetValueOrDefault( definition.Attribute ?? "" ) is { } named )
						CopyMaterial( named, container.Values );
					break;

				case CS2CompositeSource.SpecificMaterial:
					if ( composite.Materials.GetValueOrDefault( definition.Material ?? "" ) is { } specific )
						CopyMaterial( specific, container.Values );
					break;

				case CS2CompositeSource.VariableFromTargetAttribute:
					if ( target.Attributes.TryGetValue( definition.Attribute ?? "", out var text ) )
						container.Values[definition.Attribute] = float.TryParse( text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number ) ? CS2CompositeValue.Of( number ) : CS2CompositeValue.Of( text );
					break;
			}

			// The item's own values over the vcompmat's.
			if ( string.Equals( definition.Alias, "econ_instance", StringComparison.OrdinalIgnoreCase ) )
			{
				SetVariable( container.Variables, "g_nRandomSeed", CS2CompositeVariableType.Integer1, seed );
				SetVariable( container.Variables, "g_flWearAmount", CS2CompositeVariableType.Float1, wear );
			}

			foreach ( var variable in container.Variables )
				container.Values[variable.Name] = variable.ToValue();

			containers[definition.Alias ?? ""] = container;
		}

		var result = new CS2CompositeResult();
		foreach ( var mutator in composite.Mutators )
			Execute( mutator, result.Material, containers, result );

		return result;
	}

	private static void CopyMaterial( CS2MaterialParams material, Dictionary<string, CS2CompositeValue> values )
	{
		values["Shader"] = CS2CompositeValue.Of( material.Shader );
		foreach ( var (name, value) in material.Values )
			values[name] = value;
	}

	private static void SetVariable( List<CS2CompositeVariable> variables, string name, CS2CompositeVariableType type, float value )
	{
		var variable = variables.FirstOrDefault( x => string.Equals( x.Name, name, StringComparison.OrdinalIgnoreCase ) );
		if ( variable is null )
			variables.Add( variable = new CS2CompositeVariable { Name = name, Type = type } );

		variable.Value = new Vector4( value, 0, 0, 0 );
	}

	private static void Execute( CS2CompositeMutator mutator, Dictionary<string, CS2CompositeValue> target, Dictionary<string, Container> containers, CS2CompositeResult result )
	{
		if ( !mutator.Enabled || target is null )
			return;

		foreach ( var condition in mutator.Conditions )
		{
			var passed = Evaluate( condition, containers );
			if ( passed is null || passed.Value != condition.PassWhenTrue )
				return;
		}

		switch ( mutator.Type )
		{
			case CS2CompositeMutatorType.Init:
				if ( containers.GetValueOrDefault( mutator.Container ?? "" ) is { } init )
				{
					target.Clear();
					foreach ( var (name, value) in init.Values )
						target[name] = value;
				}
				break;

			case CS2CompositeMutatorType.CopyMatchingKeys:
				if ( containers.GetValueOrDefault( mutator.Container ?? "" ) is { } source )
				{
					foreach ( var name in target.Keys.ToArray() )
					{
						if ( !string.Equals( name, "Shader", StringComparison.OrdinalIgnoreCase ) && source.Values.TryGetValue( name, out var value ) )
							target[name] = value;
					}
				}
				break;

			case CS2CompositeMutatorType.CopyProperty:
			{
				var from = string.Equals( mutator.Container, "this", StringComparison.OrdinalIgnoreCase ) ? target : containers.GetValueOrDefault( mutator.Container ?? "" )?.Values;
				if ( from is not null && from.TryGetValue( mutator.Property ?? "", out var value ) )
					target[mutator.TargetProperty ?? mutator.Property] = value;
				break;
			}

			case CS2CompositeMutatorType.SetValue:
				if ( mutator.Value is { Name: { Length: > 0 } valueName } )
					target[valueName] = mutator.Value.ToValue();
				break;

			case CS2CompositeMutatorType.GenerateTexture:
			{
				if ( string.IsNullOrEmpty( mutator.TargetParameter ) )
					break;

				var values = new Dictionary<string, CS2CompositeValue>( StringComparer.OrdinalIgnoreCase );
				if ( containers.GetValueOrDefault( mutator.Container ?? "" ) is { } initial )
				{
					foreach ( var (name, value) in initial.Values )
						values[name] = value;
				}

				foreach ( var instruction in mutator.Instructions )
					Execute( instruction, values, containers, result );

				result.Passes.RemoveAll( x => string.Equals( x.TargetParameter, mutator.TargetParameter, StringComparison.OrdinalIgnoreCase ) );
				result.Passes.Add( new CS2CompositePass { TargetParameter = mutator.TargetParameter, Resolution = mutator.Resolution, Values = values } );
				target[mutator.TargetParameter] = CS2CompositeValue.Of( GeneratedPrefix + mutator.TargetParameter );
				break;
			}

			case CS2CompositeMutatorType.ConditionalMutators:
				foreach ( var child in mutator.Mutators )
					Execute( child, target, containers, result );
				break;

			case CS2CompositeMutatorType.RandomRollInputVariables:
				RandomRoll( mutator, containers );
				break;
		}
	}

	/// <summary>
	/// A condition's result, or null when the value it compares can't be read as a number (the game then skips the
	/// mutator).
	/// </summary>
	private static bool? Evaluate( CS2CompositeCondition condition, Dictionary<string, Container> containers )
	{
		var container = containers.GetValueOrDefault( condition.Container ?? "" );
		switch ( condition.Type )
		{
			case CS2CompositeConditionType.ContainerExists:
				return container is not null && container.Values.Count > 0;

			case CS2CompositeConditionType.ContainerValueExists:
				return container is not null && container.Values.ContainsKey( condition.Variable ?? "" );
		}

		if ( container is null || !container.Values.TryGetValue( condition.Variable ?? "", out var value ) )
			return false;

		if ( value.IsText )
			return string.Equals( value.Text, condition.Value, StringComparison.OrdinalIgnoreCase );

		if ( !TryParseNumber( condition.Value, out var expected ) )
			return null;

		// Each component equal within a relative 1e-5, as client.dll compares.
		static bool Near( float a, float b ) => MathF.Abs( a - b ) <= MathF.Max( 1f, MathF.Max( MathF.Abs( a ), MathF.Abs( b ) ) ) * 0.00001f;
		return Near( value.Number.x, expected.x ) && Near( value.Number.y, expected.y ) && Near( value.Number.z, expected.z ) && Near( value.Number.w, expected.w );
	}

	/// <summary>
	/// A condition's value as four floats: numbers separated by spaces or commas; true and false as 1 and 0
	/// (client.dll's parser isn't traced; the vcompmats compare booleans with both "1" and "true").
	/// </summary>
	private static bool TryParseNumber( string text, out Vector4 value )
	{
		value = default;
		if ( string.IsNullOrWhiteSpace( text ) )
			return false;

		text = text.Trim();
		if ( text.Equals( "true", StringComparison.OrdinalIgnoreCase ) )
		{
			value = new Vector4( 1, 0, 0, 0 );
			return true;
		}

		if ( text.Equals( "false", StringComparison.OrdinalIgnoreCase ) )
			return true;

		var parts = text.Split( [' ', ',', '\t'], StringSplitOptions.RemoveEmptyEntries );
		var components = new float[4];
		for ( var i = 0; i < parts.Length && i < 4; i++ )
		{
			if ( !float.TryParse( parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out components[i] ) )
				return false;
		}

		value = new Vector4( components[0], components[1], components[2], components[3] );
		return true;
	}

	private static void RandomRoll( CS2CompositeMutator mutator, Dictionary<string, Container> containers )
	{
		var seedVariable = FindVariable( mutator.SeedVariable, containers, out _, out _ );
		var seed = seedVariable is null ? 0 : (int)MathF.Round( seedVariable.Value.x );
		if ( seedVariable is not null && seed == -1 )
			return;

		var random = new CS2UniformRandomStream();
		random.SetSeed( seed );

		foreach ( var name in mutator.RollVariables )
		{
			var variable = FindVariable( name, containers, out var container, out var swizzle );
			if ( variable is null || !variable.HasBounds || !variable.IsFloat )
			{
				for ( var i = 0; i < swizzle.Length; i++ )
					random.RandomFloat( 0f, 1f );
				continue;
			}

			bool Rolls( char component ) => swizzle.Length == 0 || swizzle.Contains( component, StringComparison.OrdinalIgnoreCase );

			var value = variable.Value;
			if ( Rolls( 'x' ) )
				value.x = random.RandomFloat( variable.Min.x, variable.Max.x );
			if ( variable.Components >= 2 && Rolls( 'y' ) )
				value.y = random.RandomFloat( variable.Min.y, variable.Max.y );
			if ( variable.Components >= 3 && Rolls( 'z' ) )
				value.z = random.RandomFloat( variable.Min.z, variable.Max.z );
			if ( variable.Components >= 4 && Rolls( 'w' ) )
				value.w = random.RandomFloat( variable.Min.w, variable.Max.w );

			variable.Value = value;
			if ( container.Values.ContainsKey( variable.Name ) )
				container.Values[variable.Name] = variable.ToValue();
		}
	}

	/// <summary>
	/// A loose variable by "container.variable" or "container.variable.swizzle", with its container and swizzle.
	/// </summary>
	private static CS2CompositeVariable FindVariable( string path, Dictionary<string, Container> containers, out Container container, out string swizzle )
	{
		container = null;
		swizzle = "";

		var parts = (path ?? "").Split( '.' );
		if ( parts.Length == 3 )
			swizzle = parts[2];
		else if ( parts.Length != 2 )
			return null;

		container = containers.GetValueOrDefault( parts[0] );
		return container?.Variables.FirstOrDefault( x => string.Equals( x.Name, parts[1], StringComparison.OrdinalIgnoreCase ) );
	}
}
