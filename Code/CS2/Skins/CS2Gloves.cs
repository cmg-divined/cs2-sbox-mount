using System;

namespace CS2;

/// <summary>
/// A pair of CS2 gloves worn by an agent renderer, as the game wears them: the glove model (items_game hands_paintable,
/// e.g. <c>agents/models/shared/arms/glove_sporty/glove_sporty.vmdl</c>) bone merged onto the agent, showing its first
/// or third person meshes (its <c>first_or_third_person</c> body group), with the agent's own gloves hidden (its
/// <c>first_or_third_person</c> choice <c>firstperson_hide_default_gloves</c> or <c>thirdperson_hide_default_gloves</c>),
/// painted by <see cref="CS2WeaponSkin"/>. Follows the agent's shadow setting and its viewmodel lighting attributes.
/// </summary>
public sealed class CS2Gloves : Component
{
	/// <summary>
	/// The agent renderer the gloves are merged onto.
	/// </summary>
	public SkinnedModelRenderer Agent { get; set; }

	/// <summary>
	/// The gloves' own renderer.
	/// </summary>
	public SkinnedModelRenderer Renderer { get; set; }

	/// <summary>
	/// Whether the gloves are worn first person (on the arms) or third person (on the body).
	/// </summary>
	public bool FirstPerson { get; set; }

	private string HideDefaultGloves => FirstPerson ? "firstperson_hide_default_gloves" : "thirdperson_hide_default_gloves";

	/// <summary>
	/// The per renderer attributes the player gives its viewmodel (the unsqueezed lighting transform), copied onto the
	/// gloves.
	/// </summary>
	private static readonly string[] CopiedVector4s = ["CS2LightingTransform0", "CS2LightingTransform1", "CS2LightingTransform2"];
	private static readonly string[] CopiedVectors = ["CS2LightingNormal0", "CS2LightingNormal1", "CS2LightingNormal2"];

	/// <summary>
	/// Puts gloves (items_game name, e.g. <c>sporty_gloves</c>) on an agent renderer, first or third person, painted
	/// with a glove paint kit at a float and seed; an empty name takes them off and shows the agent's own gloves again.
	/// Returns the gloves' renderer, or null.
	/// </summary>
	public static SkinnedModelRenderer Apply( SkinnedModelRenderer agent, bool firstPerson, string glove, string paintKit, float wear, int seed )
	{
		if ( !agent.IsValid() )
			return null;

		var existing = agent.GameObject.Children.Select( x => x.Components.Get<CS2Gloves>() ).FirstOrDefault( x => x.IsValid() );
		var item = CS2PaintKits.FindGlove( glove );
		var person = firstPerson ? "firstperson" : "thirdperson";

		if ( item is null || Model.Load( $"mount://cs2/{item.Model}" ) is not { IsError: false } model )
		{
			if ( existing.IsValid() )
			{
				CS2WeaponSkin.Restore( existing.Renderer );
				existing.GameObject.Destroy();
			}

			agent.SetCS2BodyGroup( "first_or_third_person", $"{person}_default" );
			return null;
		}

		if ( !existing.IsValid() )
		{
			var gloveObject = new GameObject( agent.GameObject, true, "Gloves" );
			existing = gloveObject.Components.Create<CS2Gloves>();
			existing.Renderer = gloveObject.Components.Create<SkinnedModelRenderer>();
		}

		existing.Agent = agent;
		existing.FirstPerson = firstPerson;
		var renderer = existing.Renderer;
		if ( renderer.Model != model )
			renderer.Model = model;

		renderer.BoneMergeTarget = agent;
		renderer.RenderType = agent.RenderType;
		renderer.SetCS2BodyGroup( "first_or_third_person", $"{person}_default" );
		agent.SetCS2BodyGroup( "first_or_third_person", existing.HideDefaultGloves );

		CS2WeaponSkin.Apply( renderer, paintKit, wear, seed );
		return renderer;
	}

	protected override void OnPreRender()
	{
		if ( !Agent.IsValid() || !Renderer.IsValid() )
			return;

		Renderer.Enabled = Agent.Enabled;
		Renderer.RenderType = Agent.RenderType;

		// The agent's own gloves stay hidden when its model or body groups are set again (a new agent for a team).
		if ( Agent.GetCS2BodyGroup( "first_or_third_person" ) != HideDefaultGloves )
			Agent.SetCS2BodyGroup( "first_or_third_person", HideDefaultGloves );

		// Only what the agent has (NaN marks an attribute it doesn't).
		foreach ( var name in CopiedVector4s )
		{
			var value = Agent.Attributes.GetVector4( name, new Vector4( float.NaN ) );
			if ( !float.IsNaN( value.x ) )
				Renderer.Attributes.Set( name, value );
		}

		foreach ( var name in CopiedVectors )
		{
			var value = Agent.Attributes.GetVector( name, new Vector3( float.NaN ) );
			if ( !float.IsNaN( value.x ) )
				Renderer.Attributes.Set( name, value );
		}
	}
}
