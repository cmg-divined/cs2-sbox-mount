namespace CS2;

/// <summary>
/// The AnimGraph 2 graphs (vnmgraph) a mounted CS2 model is played with, as the model names them. The CS2 mount
/// stores them on the model, read them with <c>Model.GetData&lt;CS2AnimGraphs&gt;()</c>.
/// </summary>
[Sandbox.ModelEditor.GameData( "cs2_anim_graphs" )]
public sealed class CS2AnimGraphs
{
	/// <summary>
	/// Hidden from ModelDoc, which can't edit lists.
	/// </summary>
	[Hide]
	public List<CS2AnimGraphRef> Graphs { get; set; } = [];
}

public sealed class CS2AnimGraphRef
{
	public string Identifier { get; set; }
	public string Graph { get; set; }
}
