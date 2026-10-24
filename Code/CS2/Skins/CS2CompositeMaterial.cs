using System;
using System.Text.Json.Serialization;

namespace CS2;

/// <summary>
/// A value in a composite material's containers, as the game's composite material system keeps them (KeyValues3): a
/// number (ints, floats, vectors, colours and booleans, all as four floats; client.dll copies them so) or text (texture
/// and material paths, strings).
/// </summary>
public readonly record struct CS2CompositeValue( Vector4 Number, string Text )
{
	public bool IsText => Text is not null;

	public float X => Number.x;

	public static CS2CompositeValue Of( float x ) => new( new Vector4( x, 0, 0, 0 ), null );

	public static CS2CompositeValue Of( Vector4 value ) => new( value, null );

	public static CS2CompositeValue Of( string text ) => new( default, text ?? "" );

	public override string ToString() => IsText ? Text : $"{Number.x} {Number.y} {Number.z} {Number.w}";
}

/// <summary>
/// A CS2 material's own parameters (what its vmat sets; not the shader's defaults) and string attributes, as the
/// composite material system reads a material: ints, floats and vectors as numbers, textures as their paths.
/// </summary>
public sealed class CS2MaterialParams
{
	/// <summary>
	/// The material's path, e.g. <c>weapons/models/ak47/materials/weapon_rif_ak47.vmat</c>.
	/// </summary>
	public string Path { get; set; }

	/// <summary>
	/// Its shader, e.g. <c>csgo_weapon.vfx</c>.
	/// </summary>
	public string Shader { get; set; }

	public Dictionary<string, CS2CompositeValue> Values { get; set; } = new( StringComparer.OrdinalIgnoreCase );

	public Dictionary<string, string> Attributes { get; set; } = new( StringComparer.OrdinalIgnoreCase );
}

public enum CS2CompositeSource
{
	TargetMaterial,
	MaterialFromTargetAttribute,
	SpecificMaterial,
	TargetInstanceMaterial,
	LooseVariables,
	VariableFromTargetAttribute,
}

/// <summary>
/// CompositeMaterialInputLooseVariableType_t, in its order (client.dll checks FLOAT1 to FLOAT4 as 5 to 8).
/// </summary>
public enum CS2CompositeVariableType
{
	Boolean,
	Integer1,
	Integer2,
	Integer3,
	Integer4,
	Float1,
	Float2,
	Float3,
	Float4,
	Color4,
	String,
	SystemVariable,
	ResourceMaterial,
	ResourceTexture,
	PanoramaPanel,
}

/// <summary>
/// A loose variable of a container (or a SET_VALUE's value): its value and, for floats, the bounds a random roll
/// picks it from.
/// </summary>
public sealed class CS2CompositeVariable
{
	public string Name { get; set; }
	public CS2CompositeVariableType Type { get; set; }
	public Vector4 Value { get; set; }
	public bool HasBounds { get; set; }
	public Vector4 Min { get; set; }
	public Vector4 Max { get; set; }

	/// <summary>
	/// A string's text, or a texture's or material's path.
	/// </summary>
	public string Text { get; set; }

	public bool IsFloat => Type is >= CS2CompositeVariableType.Float1 and <= CS2CompositeVariableType.Float4;

	/// <summary>
	/// How many components a float variable has.
	/// </summary>
	public int Components => IsFloat ? Type - CS2CompositeVariableType.Float1 + 1 : 1;

	/// <summary>
	/// The variable as the composite system's KeyValues hold it.
	/// </summary>
	public CS2CompositeValue ToValue() => Type switch
	{
		CS2CompositeVariableType.String or CS2CompositeVariableType.ResourceMaterial or CS2CompositeVariableType.ResourceTexture => CS2CompositeValue.Of( Text ),
		_ => CS2CompositeValue.Of( Value ),
	};

	public CS2CompositeVariable Clone() => new()
	{
		Name = Name,
		Type = Type,
		Value = Value,
		HasBounds = HasBounds,
		Min = Min,
		Max = Max,
		Text = Text,
	};
}

/// <summary>
/// An input container: a material (the target, one named by a target attribute, a specific one) or loose variables,
/// by the alias mutators refer to it by.
/// </summary>
public sealed class CS2CompositeContainer
{
	public string Alias { get; set; }
	public CS2CompositeSource Source { get; set; }

	/// <summary>
	/// The specific material's path, for <see cref="CS2CompositeSource.SpecificMaterial"/>.
	/// </summary>
	public string Material { get; set; }

	/// <summary>
	/// The target's string attribute naming the material (<see cref="CS2CompositeSource.MaterialFromTargetAttribute"/>)
	/// or holding the variable (<see cref="CS2CompositeSource.VariableFromTargetAttribute"/>).
	/// </summary>
	public string Attribute { get; set; }

	public List<CS2CompositeVariable> Variables { get; set; } = [];
}

/// <summary>
/// CompMatPropertyMutatorType_t, in its order (client.dll's mutator switch).
/// </summary>
public enum CS2CompositeMutatorType
{
	Init,
	CopyMatchingKeys,
	CopyKeysWithSuffix,
	CopyProperty,
	SetValue,
	GenerateTexture,
	ConditionalMutators,
	PopInputQueue,
	DrawText,
	RandomRollInputVariables,
}

public enum CS2CompositeConditionType
{
	ContainerExists,
	ContainerValueExists,
	ContainerValueEquals,
}

public sealed class CS2CompositeCondition
{
	public CS2CompositeConditionType Type { get; set; }
	public string Container { get; set; }
	public string Variable { get; set; }
	public string Value { get; set; }

	/// <summary>
	/// The result the condition needs to pass (true unless the vcompmat says otherwise).
	/// </summary>
	public bool PassWhenTrue { get; set; } = true;
}

public sealed class CS2CompositeMutator
{
	public CS2CompositeMutatorType Type { get; set; }
	public bool Enabled { get; set; } = true;
	public List<CS2CompositeCondition> Conditions { get; set; } = [];

	/// <summary>
	/// The container it reads: Init's, CopyMatchingKeys', CopyProperty's source ("this" for the target itself),
	/// GenerateTexture's initial one.
	/// </summary>
	public string Container { get; set; }

	/// <summary>
	/// CopyProperty: the source property and the target property.
	/// </summary>
	public string Property { get; set; }
	public string TargetProperty { get; set; }

	/// <summary>
	/// SetValue: the value set, by its name.
	/// </summary>
	public CS2CompositeVariable Value { get; set; }

	/// <summary>
	/// RandomRollInputVariables: the seed (container.variable) and the variables rolled (container.variable.swizzle).
	/// </summary>
	public string SeedVariable { get; set; }
	public List<string> RollVariables { get; set; } = [];

	/// <summary>
	/// GenerateTexture: the material parameter it makes, the size the vcompmat asks for, and the instructions that
	/// set the generating material up.
	/// </summary>
	public string TargetParameter { get; set; }
	public int Resolution { get; set; }
	public List<CS2CompositeMutator> Instructions { get; set; } = [];

	/// <summary>
	/// ConditionalMutators: the mutators run when its conditions pass.
	/// </summary>
	public List<CS2CompositeMutator> Mutators { get; set; } = [];
}

public enum CS2CompositeMatchFilterType
{
	MaterialAttributeExists,
	MaterialAttributeEquals,
	MaterialShader,
	MaterialNameSubstring,
	MaterialPropertyExists,
	Unknown,
}

/// <summary>
/// Which of a model's materials a composite material applies to.
/// </summary>
public sealed class CS2CompositeMatchFilter
{
	public CS2CompositeMatchFilterType Type { get; set; }
	public string Filter { get; set; }
	public string Value { get; set; }
	public bool PassWhenTrue { get; set; } = true;

	public bool Matches( CS2MaterialParams material )
	{
		var result = Type switch
		{
			CS2CompositeMatchFilterType.MaterialAttributeExists => material.Attributes.ContainsKey( Filter ?? "" ),
			CS2CompositeMatchFilterType.MaterialAttributeEquals => material.Attributes.TryGetValue( Filter ?? "", out var value ) && string.Equals( value, Value, StringComparison.OrdinalIgnoreCase ),
			CS2CompositeMatchFilterType.MaterialShader => string.Equals( material.Shader, Filter, StringComparison.OrdinalIgnoreCase ),
			CS2CompositeMatchFilterType.MaterialNameSubstring => material.Path?.Contains( Filter ?? "", StringComparison.OrdinalIgnoreCase ) ?? false,
			CS2CompositeMatchFilterType.MaterialPropertyExists => material.Values.ContainsKey( Filter ?? "" ),
			_ => false,
		};

		return result == PassWhenTrue;
	}
}

/// <summary>
/// A CS2 composite material (a <c>.vcompmat</c>, e.g. a paint kit's finish) as the CS2 mount serves it, at
/// <c>mount://cs2/weapons/paints/legacy/cu_ak47_asiimov.vcompmat</c>: its assembly procedure with the files it
/// includes merged in (the included first; containers of one alias merged, loose variables by name), and the
/// parameters of the specific materials it names. <see cref="CS2CompositeProgram"/> runs it.
/// </summary>
public sealed class CS2CompositeMaterial : GameResource
{
	/// <summary>
	/// The vcompmat's path.
	/// </summary>
	[Hide, JsonIgnore] public string Path { get; set; }

	[Hide, JsonIgnore] public List<CS2CompositeMatchFilter> MatchFilters { get; set; } = [];
	[Hide, JsonIgnore] public List<CS2CompositeContainer> Containers { get; set; } = [];
	[Hide, JsonIgnore] public List<CS2CompositeMutator> Mutators { get; set; } = [];

	/// <summary>
	/// The specific materials the containers name, by path.
	/// </summary>
	[Hide, JsonIgnore] public Dictionary<string, CS2MaterialParams> Materials { get; set; } = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Whether it applies to a material: every match filter passes.
	/// </summary>
	public bool Matches( CS2MaterialParams material ) => MatchFilters.All( x => x.Matches( material ) );

	/// <summary>
	/// A mounted composite material by its path (e.g. <c>weapons/paints/legacy/aa_fade.vcompmat</c>), or null.
	/// </summary>
	public static CS2CompositeMaterial Find( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) )
			return null;

		path = path.Replace( '\\', '/' );
		if ( path.EndsWith( "_c" ) )
			path = path[..^2];

		if ( !path.StartsWith( "mount://" ) )
			path = $"mount://cs2/{path}";

		return GameResource.Load<CS2CompositeMaterial>( path ) is { Path: not null } composite ? composite : null;
	}
}
