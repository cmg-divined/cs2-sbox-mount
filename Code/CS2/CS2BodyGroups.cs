using System;

namespace CS2;

/// <summary>
/// The body groups of a mounted CS2 model with their choice names, which s&amp;box doesn't keep for
/// models built at runtime: the renderer lists every choice as "mesh". Choices are in the same order as the
/// model's body part choices. Read it with <c>Model.GetData&lt;CS2BodyGroups&gt;()</c>.
/// </summary>
[Sandbox.ModelEditor.GameData( "cs2_body_groups" )]
public sealed class CS2BodyGroups
{
	/// <summary>
	/// Hidden from ModelDoc, which can't edit lists.
	/// </summary>
	[Hide]
	public List<CS2BodyGroup> Groups { get; set; } = [];

	public CS2BodyGroup Get( string group ) => Groups.FirstOrDefault( x => string.Equals( x.Name, group, StringComparison.OrdinalIgnoreCase ) );

	/// <summary>
	/// The renderer body group mask bit of a group's first choice: the importer registers every choice of every group,
	/// in this order, before adding any mesh, so choice <c>c</c> of a group is bit <c>offset + c</c>. -1 for an unknown
	/// group.
	/// </summary>
	public int FirstBit( string group )
	{
		var offset = 0;
		foreach ( var entry in Groups )
		{
			if ( string.Equals( entry.Name, group, StringComparison.OrdinalIgnoreCase ) )
				return offset;

			offset += entry.Choices.Count;
		}

		return -1;
	}
}

public sealed class CS2BodyGroup
{
	public string Name { get; set; }

	/// <summary>
	/// Choice names by choice index, as the model names them.
	/// </summary>
	public List<string> Choices { get; set; } = [];
}

public static class CS2BodyGroupExtensions
{
	/// <summary>
	/// Selects a body group choice by its CS2 name. Returns false if the model has no such group or choice.
	/// </summary>
	public static bool SetCS2BodyGroup( this ModelRenderer renderer, string group, string choice )
	{
		if ( !renderer.IsValid() || renderer.Model is not { } model || !model.TryGetData<CS2BodyGroups>( out var groups ) )
			return false;

		var index = groups?.Get( group )?.Choices.FindIndex( x => string.Equals( x, choice, StringComparison.OrdinalIgnoreCase ) ) ?? -1;
		return index >= 0 && renderer.SetCS2BodyGroup( group, index );
	}

	/// <summary>
	/// Selects a body group choice by index, turning on just that choice's own mask bit. s&amp;box's
	/// <c>SetBodyGroup</c> uses the native choice mask, which is every bit of every mesh in the choice: a mesh shared by
	/// two choices (an agent's body, in both <c>thirdperson_default</c> and <c>thirdperson_hide_default_gloves</c>)
	/// makes both choices the same mask, so the second could never be picked. A mesh draws when any of its bits is on,
	/// so the choice's own bit shows exactly its meshes. Returns false if the model has no such group or choice.
	/// </summary>
	public static bool SetCS2BodyGroup( this ModelRenderer renderer, string group, int choice )
	{
		if ( !renderer.IsValid() || renderer.Model is not { } model || !model.TryGetData<CS2BodyGroups>( out var groups ) || groups is null )
			return false;

		var count = groups.Get( group )?.Choices.Count ?? 0;
		var first = groups.FirstBit( group );
		if ( choice < 0 || choice >= count || first < 0 || first + count > 64 )
		{
			// Not a mounted model's group (or too many choices to address): s&box's own choosing.
			renderer.SetBodyGroup( group, choice );
			return false;
		}

		var groupMask = (count == 64 ? ulong.MaxValue : (1UL << count) - 1) << first;
		renderer.BodyGroups = (renderer.BodyGroups & ~groupMask) | (1UL << (first + choice));
		return true;
	}

	/// <summary>
	/// The CS2 name of a body group's current choice, or null.
	/// </summary>
	public static string GetCS2BodyGroup( this ModelRenderer renderer, string group )
	{
		if ( !renderer.IsValid() || renderer.Model is not { } model || !model.TryGetData<CS2BodyGroups>( out var groups ) )
			return null;

		if ( groups?.Get( group )?.Choices is not { } choices )
			return null;

		var index = renderer.GetCS2BodyGroupIndex( group );
		return index >= 0 && index < choices.Count ? choices[index] : null;
	}

	/// <summary>
	/// The index of a body group's current choice: the first of its choice bits that is on (see
	/// <see cref="SetCS2BodyGroup(ModelRenderer, string, int)"/>), else s&amp;box's answer. -1 if there's no such group.
	/// </summary>
	public static int GetCS2BodyGroupIndex( this ModelRenderer renderer, string group )
	{
		if ( !renderer.IsValid() || renderer.Model is not { } model )
			return -1;

		if ( model.TryGetData<CS2BodyGroups>( out var groups ) && groups?.Get( group ) is { } entry && groups.FirstBit( group ) is var first and >= 0 )
		{
			for ( var choice = 0; choice < entry.Choices.Count && first + choice < 64; choice++ )
			{
				if ( ((renderer.BodyGroups >> (first + choice)) & 1) != 0 )
					return choice;
			}
		}

		return model.Parts.Get( group ) is null ? -1 : renderer.GetBodyGroup( group );
	}
}
