using System;
using CS2.Animation;
using CS2.Animation.ModelAnimation;
using GraphRuntime = CS2.Animation.ModelAnimation2.AnimationGraph;
using ModelSkeleton = CS2.Animation.ModelAnimation.Skeleton;

namespace CS2;

/// <summary>
/// Animates a mounted CS2 model with an AnimGraph 2 graph, the way the game does: a port of ValveResourceFormat's
/// AnimGraph 2 runtime (itself Esoterica's) runs the graph each frame and its pose goes onto the renderer's bones. Set
/// the graph's parameters (movement, weapon, action) to drive it.
/// </summary>
/// <remarks>
/// The graph's bones are placed through the graph skeleton's own hierarchy (a viewmodel skeleton hangs the arms off its
/// root, where the model hangs them off the spine); the model's other bones keep their bind pose under their parents.
/// A held <see cref="Weapon"/> goes on the graph's <c>wpn</c> bone, its own bones played from the clips' secondary
/// animations for its skeleton, which the graph samples and blends along with the main pose (so a knife stays in the
/// hand through a blend between two of its clips).
/// </remarks>
[Title( "CS2 Animator" ), Category( "Counter-Strike 2" ), Icon( "directions_run" )]
public sealed class CS2Animator : Component, Component.ExecuteInEditor
{
	/// <summary>
	/// The renderer animated. Its own animation graph is turned off while this plays.
	/// </summary>
	[Property] public SkinnedModelRenderer Renderer { get; set; }

	/// <summary>
	/// The graph played. Unset, the model's own graph with <see cref="GraphIdentifier"/> is.
	/// </summary>
	[Property] public CS2AnimationGraph Graph { get; set; }

	/// <summary>
	/// Which of the model's graphs plays when <see cref="Graph"/> is unset: empty for its default (an agent's third
	/// person body), <c>hudmodel</c> for its first person arms, <c>uimodel</c> for the menus.
	/// </summary>
	[Property] public string GraphIdentifier { get; set; } = "";

	/// <summary>
	/// The weapon held, on the graph's <c>wpn</c> bone, its parts animated by the clips.
	/// </summary>
	[Property] public SkinnedModelRenderer Weapon { get; set; }

	/// <summary>
	/// A second renderer of the same model posed alike but with the upper body (from <c>spine_0</c> up) collapsed to
	/// nothing at the pelvis: a first person player's own legs, as the game draws them.
	/// </summary>
	[Property] public SkinnedModelRenderer FirstPersonLegs { get; set; }

	/// <summary>
	/// Whether the graph steps by itself every frame. Something that sets its parameters each frame (like
	/// <see cref="CS2Player"/>) turns this off and calls <see cref="Step"/> after setting them.
	/// </summary>
	[Property] public bool UpdateAutomatically { get; set; } = true;

	/// <summary>
	/// Whether it animates in the editor's scene view too, not only in play mode.
	/// </summary>
	[Property] public bool PlayInEditor { get; set; } = true;

	[Property, Range( 0, 4 )] public float TimeScale { get; set; } = 1f;

	/// <summary>
	/// How the graph's root moved during the last step, in the model's space.
	/// </summary>
	public Transform RootMotion { get; private set; } = global::Transform.Zero;

	/// <summary>
	/// Whether a graph is loaded and playing.
	/// </summary>
	public bool IsPlaying => graph is not null;

	/// <summary>
	/// The clips the last step sampled (name, time and duration in seconds), in sampling order. Recorded while
	/// <see cref="RecordSampledClips"/> is set.
	/// </summary>
	public IEnumerable<(string Clip, float Time, float Duration)> SampledClips => graph?.SampledClips.Select( x => (x.Name, x.Time, x.Duration) ) ?? [];

	/// <summary>
	/// Whether each step records the clips it samples (<see cref="SampledClips"/>), as game code watching for its
	/// actions' clips to end needs.
	/// </summary>
	public bool RecordSampledClips { get; set; }

	private GraphRuntime graph;
	private CS2AnimationGraph source;
	private Model boundModel;
	private CS2AnimationGraph boundGraph;
	private bool failed;

	// Per graph bone: its parent in the graph skeleton. Per model bone: the graph bone placing it or -1, its parent and
	// its bind pose relative to it.
	private int[] graphParent = [];
	private Transform[] graphModelSpace = [];
	private int[] graphBone = [];
	private int[] parentBone = [];
	private Transform[] bindLocal = [];
	private Transform[] modelSpace = [];
	private int weaponGraphBone = -1;
	private CS2BoneConstraintSolver constraints;

	// Per model bone: the bone the legs collapse it onto (the parent of spine_0), or -1 when it stays.
	private int[] collapsedOnto = [];

	private readonly Dictionary<string, float> floats = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, bool> bools = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, string> ids = new( StringComparer.OrdinalIgnoreCase );

	public void SetFloat( string name, float value )
	{
		floats[name] = value;
		if ( graph is not null && graph.FloatParameters.ContainsKey( name ) )
			graph.FloatParameters[name] = value;
	}

	public void SetBool( string name, bool value )
	{
		bools[name] = value;
		if ( graph is not null && graph.BoolParameters.ContainsKey( name ) )
			graph.BoolParameters[name] = value;
	}

	/// <summary>
	/// Sets an ID parameter, such as <c>action</c> to <c>action_inspect</c>; empty clears it.
	/// </summary>
	public void SetId( string name, string value )
	{
		value ??= string.Empty;
		ids[name] = value;
		if ( graph is not null && graph.IdParameters.ContainsKey( name ) )
			graph.IdParameters[name] = value;
	}

	/// <summary>
	/// Raises a bool parameter for one update, as the game signals an action starting over (<c>action_reset</c>).
	/// </summary>
	public void Signal( string name )
	{
		if ( graph is not null && graph.BoolParameters.ContainsKey( name ) )
			graph.SignalBoolParameter( name );
	}

	public float GetFloat( string name ) => graph is not null && graph.FloatParameters.TryGetValue( name, out var value ) ? value : floats.GetValueOrDefault( name );

	public string GetId( string name ) => graph is not null && graph.IdParameters.TryGetValue( name, out var value ) ? value : ids.GetValueOrDefault( name ) ?? "";

	/// <summary>
	/// The graph's parameter names, once it's loaded.
	/// </summary>
	public IReadOnlyList<string> ParameterNames => graph?.ParameterNames ?? [];

	protected override void OnEnabled()
	{
		AnimationLog.Handler ??= message => Log.Warning( message );
	}

	protected override void OnDisabled()
	{
		Release();
	}

	protected override void OnUpdate()
	{
		if ( UpdateAutomatically )
			Step( Time.Delta );
	}

	/// <summary>
	/// Advances the graph and poses the renderer's bones, and the weapon's.
	/// </summary>
	public void Step( float timeStep )
	{
		if ( !Game.IsPlaying && !PlayInEditor )
		{
			Release();
			return;
		}

		var renderer = Renderer;
		if ( !renderer.IsValid() || renderer.Model is not { } model || renderer.SceneModel is not { } sceneModel )
			return;

		if ( !Bind( model ) )
			return;

		if ( renderer.UseAnimGraph )
			renderer.UseAnimGraph = false;

		var world = renderer.WorldTransform;
		var worldBone = new FrameBone( world.Position, world.UniformScale, ToQuaternion( world.Rotation ) );

		FrameBone[] pose;

		try
		{
			graph.RecordUpdateDetails = RecordSampledClips;
			pose = graph.Update( MathF.Max( 0f, timeStep * TimeScale ), worldBone );
		}
		catch ( Exception e )
		{
			Log.Warning( $"{GameObject.Name}: animation graph failed: {e.Message}" );
			Release();
			failed = true;
			return;
		}

		RootMotion = ToTransform( graph.RootMotionDelta );
		CollectSoundEvents();

		for ( var i = 0; i < graphModelSpace.Length; i++ )
		{
			var local = i < pose.Length ? ToTransform( pose[i] ) : global::Transform.Zero;
			graphModelSpace[i] = graphParent[i] >= 0 ? graphModelSpace[graphParent[i]].ToWorld( local ) : local;
		}

		for ( var i = 0; i < graphBone.Length; i++ )
		{
			modelSpace[i] = graphBone[i] >= 0
				? graphModelSpace[graphBone[i]]
				: parentBone[i] >= 0 ? modelSpace[parentBone[i]].ToWorld( bindLocal[i] ) : bindLocal[i];
		}

		// The helper bones the graph doesn't animate (forearm and upper arm twists, scapulae) follow the bones it does.
		constraints?.Evaluate( modelSpace );

		for ( var i = 0; i < modelSpace.Length; i++ )
			sceneModel.SetBoneOverride( i, modelSpace[i] );

		PoseLegs();
		PoseWeapon( world );
	}

	/// <summary>
	/// The sound events the last step started in the graph's active branches: their names, and whether they play at
	/// the eyes (else at the entity). An event reports once, on the step that first samples it.
	/// </summary>
	public IReadOnlyList<(string Name, bool AtEyes)> SampledSoundEvents => soundEvents;

	private readonly List<(string Name, bool AtEyes)> soundEvents = [];
	private HashSet<CS2.Animation.SoundEvent> activeSoundEvents = [];
	private HashSet<CS2.Animation.SoundEvent> nextSoundEvents = [];

	private void CollectSoundEvents()
	{
		soundEvents.Clear();
		nextSoundEvents.Clear();

		var events = graph.SampledEvents;
		for ( var i = 0; i < events.Count; i++ )
		{
			var sampled = events[i];
			if ( sampled.AnimEvent is not CS2.Animation.SoundEvent sound || !sampled.IsFromActiveBranch || sampled.IsIgnored || string.IsNullOrEmpty( sound.Name ) )
				continue;

			if ( nextSoundEvents.Add( sound ) && !activeSoundEvents.Contains( sound ) )
				soundEvents.Add( (sound.Name, sound.Position == CS2.Animation.SoundEvent.PositionType.EntityEyePos) );
		}

		(activeSoundEvents, nextSoundEvents) = (nextSoundEvents, activeSoundEvents);
	}

	/// <summary>
	/// The model space transform of a graph bone after the last step, by name.
	/// </summary>
	public Transform? GetGraphBone( string name )
	{
		if ( graph is null )
			return null;

		var bones = graph.Skeleton.Bones;
		for ( var i = 0; i < bones.Length && i < graphModelSpace.Length; i++ )
		{
			if ( string.Equals( bones[i].Name, name, StringComparison.OrdinalIgnoreCase ) )
				return graphModelSpace[i];
		}

		return null;
	}

	/// <summary>
	/// Loads the graph for a model, and works out which graph bone places which model bone.
	/// </summary>
	private bool Bind( Model model )
	{
		var wanted = Graph ?? FindModelGraph( model, GraphIdentifier );

		if ( graph is not null && boundModel == model && boundGraph == wanted )
			return true;

		if ( failed && boundModel == model && boundGraph == wanted )
			return false;

		Release();
		boundModel = model;
		boundGraph = wanted;
		failed = true;

		if ( wanted?.Root is null || wanted.Load( wanted.Root ) is not { } definition )
			return false;

		try
		{
			graph = new GraphRuntime( wanted.Root, definition, wanted );
			source = wanted;
		}
		catch ( Exception e )
		{
			Log.Warning( $"{GameObject.Name}: animation graph '{wanted.Root}' failed to load: {e.Message}" );
			graph = null;
			return false;
		}

		var skeletonBones = graph.Skeleton.Bones;
		graphParent = skeletonBones.Select( x => x.Parent?.Index ?? -1 ).ToArray();
		graphModelSpace = new Transform[skeletonBones.Length];

		var graphBones = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		for ( var i = 0; i < skeletonBones.Length; i++ )
			graphBones.TryAdd( skeletonBones[i].Name, i );

		weaponGraphBone = graphBones.GetValueOrDefault( "wpn", -1 );

		var bones = model.Bones.AllBones;
		graphBone = new int[bones.Count];
		parentBone = new int[bones.Count];
		bindLocal = new Transform[bones.Count];
		modelSpace = new Transform[bones.Count];

		for ( var i = 0; i < bones.Count; i++ )
		{
			var bone = bones[i];
			graphBone[i] = graphBones.GetValueOrDefault( bone.Name, -1 );
			parentBone[i] = bone.Parent?.Index ?? -1;
			bindLocal[i] = bone.Parent is { } parent ? parent.LocalTransform.ToLocal( bone.LocalTransform ) : bone.LocalTransform;
		}

		constraints = model.TryGetData<CS2BoneConstraints>( out var definitions ) && definitions?.Constraints is { Count: > 0 }
			? new CS2BoneConstraintSolver( model, definitions )
			: null;

		// The upper body the legs hide: spine_0 and everything under it.
		collapsedOnto = new int[bones.Count];
		Array.Fill( collapsedOnto, -1 );
		var spine = bones.FirstOrDefault( x => string.Equals( x.Name, "spine_0", StringComparison.OrdinalIgnoreCase ) );
		if ( spine?.Parent is { } waist )
		{
			for ( var i = 0; i < bones.Count; i++ )
			{
				for ( var bone = bones[i]; bone is not null; bone = bone.Parent )
				{
					if ( bone == spine )
					{
						collapsedOnto[i] = waist.Index;
						break;
					}
				}
			}
		}

		ApplyParameters();
		failed = false;
		return true;
	}

	/// <summary>
	/// The first person legs: the pose, with the upper body scaled to nothing where it meets the pelvis.
	/// </summary>
	private void PoseLegs()
	{
		var legs = FirstPersonLegs;
		if ( !legs.IsValid() || legs.Model != boundModel || legs.SceneModel is not { } legsModel )
			return;

		if ( legs.UseAnimGraph )
			legs.UseAnimGraph = false;

		legs.WorldTransform = Renderer.WorldTransform;

		for ( var i = 0; i < modelSpace.Length; i++ )
		{
			var onto = i < collapsedOnto.Length ? collapsedOnto[i] : -1;
			legsModel.SetBoneOverride( i, onto >= 0 ? modelSpace[onto].WithScale( 0f ) : modelSpace[i] );
		}
	}

	private void ApplyParameters()
	{
		foreach ( var (name, value) in floats )
			if ( graph.FloatParameters.ContainsKey( name ) ) graph.FloatParameters[name] = value;

		foreach ( var (name, value) in bools )
			if ( graph.BoolParameters.ContainsKey( name ) ) graph.BoolParameters[name] = value;

		foreach ( var (name, value) in ids )
			if ( graph.IdParameters.ContainsKey( name ) ) graph.IdParameters[name] = value;
	}

	private static CS2AnimationGraph FindModelGraph( Model model, string identifier )
	{
		if ( !model.TryGetData<CS2AnimGraphs>( out var graphs ) || graphs?.Graphs is not { Count: > 0 } references )
			return null;

		var reference = references.FirstOrDefault( x => string.Equals( x.Identifier ?? "", identifier ?? "", StringComparison.OrdinalIgnoreCase ) );
		return reference is null ? null : CS2AnimationGraph.Find( reference.Graph );
	}

	private void Release()
	{
		if ( graph is not null && Renderer.IsValid() )
		{
			Renderer.ClearPhysicsBones();
			Renderer.UseAnimGraph = true;
		}

		graph = null;
		source = null;
		boundModel = null;
		boundGraph = null;
		failed = false;
		weaponBinding = null;
		constraints = null;
		RootMotion = global::Transform.Zero;
	}

	// The weapon: its model's bones by the secondary skeleton's bone order.
	private sealed class WeaponBinding
	{
		public Model Model;
		public string Skeleton;
		public int[] ModelBone = [];
		public int[] SkeletonParent = [];
		public Transform[] SkeletonModelSpace = [];
		public Transform[] ModelSpace = [];
		public int[] ModelParent = [];
		public Transform[] BindLocal = [];
	}

	private WeaponBinding weaponBinding;

	/// <summary>
	/// The held weapon on the graph's <c>wpn</c> bone, and its parts as the graph's blended secondary pose has them;
	/// without one, its root bone on the <c>wpn</c> bone in its bind pose. The weapon renderer sits
	/// where the animated one does and carries the placement in its bones, so a view model's stretched transform
	/// stretches both alike.
	/// </summary>
	private void PoseWeapon( Transform world )
	{
		var weapon = Weapon;
		if ( !weapon.IsValid() || weaponGraphBone < 0 || weapon.Model is not { } model || weapon.SceneModel is not { } sceneModel )
			return;

		weapon.WorldTransform = world;
		if ( weapon.UseAnimGraph )
			weapon.UseAnimGraph = false;

		var wpn = graphModelSpace[weaponGraphBone];
		var secondary = graph.SecondaryPose;
		var bones = model.Bones.AllBones;

		if ( secondary is null || BindWeapon( model, secondary.Skeleton ) is not { } binding )
		{
			var root = bones.Count > 0 ? bones[0].LocalTransform : global::Transform.Zero;
			for ( var i = 0; i < bones.Count; i++ )
				sceneModel.SetBoneOverride( i, wpn.ToWorld( root.ToLocal( bones[i].LocalTransform ) ) );

			return;
		}

		for ( var i = 0; i < binding.SkeletonModelSpace.Length; i++ )
		{
			var parent = binding.SkeletonParent[i];
			var local = i < secondary.Pose.Length ? ToTransform( secondary.Pose[i] ) : global::Transform.Zero;
			binding.SkeletonModelSpace[i] = parent >= 0 ? binding.SkeletonModelSpace[parent].ToWorld( local ) : local;
		}

		for ( var i = 0; i < binding.ModelSpace.Length; i++ )
		{
			var skeletonBone = Array.IndexOf( binding.ModelBone, i );
			binding.ModelSpace[i] = skeletonBone >= 0
				? binding.SkeletonModelSpace[skeletonBone]
				: binding.ModelParent[i] >= 0 ? binding.ModelSpace[binding.ModelParent[i]].ToWorld( binding.BindLocal[i] ) : binding.BindLocal[i];

			sceneModel.SetBoneOverride( i, wpn.ToWorld( binding.ModelSpace[i] ) );
		}
	}

	private WeaponBinding BindWeapon( Model model, string skeletonName )
	{
		if ( weaponBinding is { } bound && bound.Model == model && bound.Skeleton == skeletonName )
			return bound;

		weaponBinding = null;
		if ( ModelSkeleton.FromSkeletonResource( source, skeletonName ) is not { } skeleton )
			return null;

		var bones = model.Bones.AllBones;
		var byName = new Dictionary<string, int>( StringComparer.OrdinalIgnoreCase );
		foreach ( var bone in bones )
			byName.TryAdd( bone.Name, bone.Index );

		var binding = new WeaponBinding
		{
			Model = model,
			Skeleton = skeletonName,
			ModelBone = skeleton.Bones.Select( x => byName.GetValueOrDefault( x.Name, -1 ) ).ToArray(),
			SkeletonParent = skeleton.Bones.Select( x => x.Parent?.Index ?? -1 ).ToArray(),
			SkeletonModelSpace = new Transform[skeleton.Bones.Length],
			ModelSpace = new Transform[bones.Count],
			ModelParent = bones.Select( x => x.Parent?.Index ?? -1 ).ToArray(),
			BindLocal = bones.Select( x => x.Parent is { } parent ? parent.LocalTransform.ToLocal( x.LocalTransform ) : x.LocalTransform ).ToArray(),
		};

		weaponBinding = binding;
		return binding;
	}

	private static Transform ToTransform( FrameBone bone )
		=> new( bone.Position, new Rotation( bone.Angle.X, bone.Angle.Y, bone.Angle.Z, bone.Angle.W ), bone.Scale );

	private static System.Numerics.Quaternion ToQuaternion( Rotation rotation )
		=> new( rotation.x, rotation.y, rotation.z, rotation.w );
}
