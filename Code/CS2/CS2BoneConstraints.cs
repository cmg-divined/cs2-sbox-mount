using System;
using CS2.Animation.ModelAnimation;
using NVector3 = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using NMatrix = System.Numerics.Matrix4x4;

namespace CS2;

/// <summary>
/// A mounted CS2 model's bone constraints (the model's <c>BoneConstraintList</c>): helper bones the game poses from
/// other bones after animating, such as the forearm's twist bones following the hand. The CS2 mount stores them on the
/// model; <see cref="CS2Animator"/> solves them.
/// </summary>
[Sandbox.ModelEditor.GameData( "cs2_bone_constraints" )]
public sealed class CS2BoneConstraints
{
	/// <summary>
	/// Hidden from ModelDoc, which can't edit lists.
	/// </summary>
	[Hide]
	public List<CS2BoneConstraint> Constraints { get; set; } = [];
}

/// <summary>
/// One constraint, in the order the model lists them.
/// </summary>
public sealed class CS2BoneConstraint
{
	/// <summary>
	/// The compiled class: <c>CTiltTwistConstraint</c>, <c>CPointConstraint</c> or <c>CAimConstraint</c>.
	/// </summary>
	public string Class { get; set; }

	/// <summary>
	/// The bones it moves (the compiled <c>m_slaves</c>), with how much.
	/// </summary>
	public List<CS2ConstraintBone> Bones { get; set; } = [];

	/// <summary>
	/// The bones it follows (<c>m_targets</c>), each with its offset and weight.
	/// </summary>
	public List<CS2ConstraintBone> Targets { get; set; } = [];

	/// <summary>
	/// Tilt twist: the target's axis the twist is measured around, and the constrained bone's axis it's applied around.
	/// </summary>
	public int TargetAxis { get; set; }

	public int ConstrainedAxis { get; set; }

	/// <summary>
	/// Aim: how up is found (0 the last target's rotation of <see cref="UpVector"/>, 1 <see cref="UpVector"/> itself,
	/// 2 towards the last target, 3 the parent's rotation of <see cref="UpVector"/>), and the rotation after aiming.
	/// </summary>
	public int UpType { get; set; }

	public Vector3 UpVector { get; set; }

	public Rotation AimOffset { get; set; } = Rotation.Identity;
}

public sealed class CS2ConstraintBone
{
	public string Bone { get; set; }

	public float Weight { get; set; }

	/// <summary>
	/// A target's offset from its bone.
	/// </summary>
	public Vector3 Position { get; set; }

	public Rotation Rotation { get; set; } = Rotation.Identity;
}

/// <summary>
/// Solves a model's <see cref="CS2BoneConstraints"/> on its model space pose: a port of ValveResourceFormat's
/// BoneConstraintSolver, for the constraint classes CS2's player models use (tilt twist, point and aim). Constraints
/// naming bones the model doesn't have are left out, as are attachment targets (no CS2 agent has one).
/// </summary>
public sealed class CS2BoneConstraintSolver
{
	private readonly Constraint[] constraints;

	public CS2BoneConstraintSolver( Model model, CS2BoneConstraints definitions )
	{
		var context = new BuildContext( model );
		var built = new List<Constraint>();

		foreach ( var definition in definitions?.Constraints ?? [] )
		{
			Constraint constraint = definition.Class switch
			{
				"CTiltTwistConstraint" => TiltTwist.Create( definition, context ),
				"CPointConstraint" => Point.Create( definition, context ),
				"CAimConstraint" => Aim.Create( definition, context ),
				_ => null,
			};

			if ( constraint is not null )
				built.Add( constraint );
		}

		constraints = [.. built];
	}

	/// <summary>
	/// How many of the model's constraints are solved.
	/// </summary>
	public int Count => constraints.Length;

	/// <summary>
	/// Solves every constraint in order, moving the bones in <paramref name="pose"/> (model space, by model bone index).
	/// </summary>
	public void Evaluate( Transform[] pose )
	{
		var frames = new FrameBone[pose.Length];
		for ( var i = 0; i < pose.Length; i++ )
			frames[i] = ToFrame( pose[i] );

		foreach ( var constraint in constraints )
			constraint.Evaluate( frames );

		for ( var i = 0; i < pose.Length; i++ )
			pose[i] = ToTransform( frames[i] );
	}

	private sealed class BuildContext( Model model )
	{
		private readonly IReadOnlyList<BoneCollection.Bone> bones = model.Bones.AllBones;
		private readonly Dictionary<int, int[]> descendants = [];

		public int Find( string name ) => string.IsNullOrEmpty( name ) ? -1 : model.Bones.GetBone( name )?.Index ?? -1;

		public int ParentOf( int bone ) => bone >= 0 ? bones[bone].Parent?.Index ?? -1 : -1;

		public int[] DescendantsOf( int bone )
		{
			if ( descendants.TryGetValue( bone, out var list ) )
				return list;

			var collected = new List<int>();
			var stack = new Stack<BoneCollection.Bone>( bones[bone].Children );
			while ( stack.TryPop( out var child ) )
			{
				collected.Add( child.Index );
				foreach ( var grandchild in child.Children )
					stack.Push( grandchild );
			}

			return descendants[bone] = [.. collected.Order()];
		}

		public Constrained ResolveConstrained( CS2ConstraintBone definition )
		{
			var bone = Find( definition.Bone );
			return bone < 0 ? null : new Constrained( bone, ParentOf( bone ), definition.Weight, DescendantsOf( bone ) );
		}

		public Constrained[] ResolveConstrained( List<CS2ConstraintBone> definitions )
		{
			var resolved = new Constrained[definitions.Count];
			for ( var i = 0; i < resolved.Length; i++ )
			{
				if ( ResolveConstrained( definitions[i] ) is not { } constrained )
					return null;

				resolved[i] = constrained;
			}

			return resolved;
		}

		public Target[] ResolveTargets( List<CS2ConstraintBone> definitions )
		{
			var resolved = new Target[definitions.Count];
			for ( var i = 0; i < resolved.Length; i++ )
			{
				var definition = definitions[i];
				var bone = Find( definition.Bone );
				if ( bone < 0 )
					return null;

				var offset = new FrameBone( definition.Position, 1f, ToQuaternion( definition.Rotation ) );
				resolved[i] = new Target( bone, IsApproxIdentity( offset ) ? null : offset, definition.Weight );
			}

			return resolved;
		}
	}

	private sealed record Constrained( int Bone, int Parent, float Weight, int[] Descendants );

	/// <summary>
	/// A target bone, with its offset unless that's the identity.
	/// </summary>
	private sealed record Target( int Bone, FrameBone? Offset, float Weight );

	private abstract class Constraint
	{
		public abstract void Evaluate( FrameBone[] pose );
	}

	private abstract class TargetConstraint( Constrained[] constrainedBones, Target[] targets ) : Constraint
	{
		protected Constrained[] ConstrainedBones { get; } = constrainedBones;
		protected Target[] Targets { get; } = targets;

		protected static FrameBone GetTarget( Target target, FrameBone[] pose )
			=> target.Offset is { } offset ? pose[target.Bone].Concat( offset ) : pose[target.Bone];

		protected (NVector3 Position, float TotalWeight) BlendPositions( int count, FrameBone[] pose )
		{
			var position = NVector3.Zero;
			var totalWeight = 0f;

			for ( var i = 0; i < count; i++ )
			{
				var target = Targets[i];
				position += GetTarget( target, pose ).Position * target.Weight;
				totalWeight += target.Weight;
			}

			if ( totalWeight != 0f )
				position /= totalWeight;

			return (position, totalWeight);
		}
	}

	private sealed class TiltTwist( Constrained constrained, int targetBone, int targetParent, NQuaternion inverseOffset, int targetAxis, int constrainedAxis ) : Constraint
	{
		private bool hasPreviousAngle;
		private float previousAngle;

		public static TiltTwist Create( CS2BoneConstraint definition, BuildContext context )
		{
			if ( definition.Bones.Count == 0 || definition.Targets.Count == 0 || context.ResolveConstrained( definition.Bones[0] ) is not { } constrained )
				return null;

			var targetBone = context.Find( definition.Targets[0].Bone );
			if ( targetBone < 0 )
				return null;

			return new TiltTwist( constrained, targetBone, context.ParentOf( targetBone ), NQuaternion.Inverse( ToQuaternion( definition.Targets[0].Rotation ) ),
				Math.Clamp( definition.TargetAxis, 0, 2 ), Math.Clamp( definition.ConstrainedAxis, 0, 2 ) );
		}

		public override void Evaluate( FrameBone[] pose )
		{
			var rotation = pose[targetBone].Angle;
			if ( targetParent >= 0 )
				rotation = NQuaternion.Inverse( pose[targetParent].Angle ) * rotation;

			var angle = TwistAngle( inverseOffset * rotation, targetAxis ) * constrained.Weight;

			// Unwrapped against the last frame, so the bone doesn't flip as the angle crosses +-pi.
			if ( hasPreviousAngle && previousAngle != angle )
			{
				var previousWrapped = MathUtils.Wrap( previousAngle, -MathF.Tau, MathF.Tau );
				var wrapped = MathUtils.Wrap( angle, -MathF.Tau, MathF.Tau );
				var delta = wrapped - previousWrapped;

				if ( MathF.Abs( delta ) >= MathF.PI )
					delta += wrapped > previousWrapped ? -MathF.Tau : MathF.Tau;

				angle = previousAngle + delta;
			}

			hasPreviousAngle = true;
			previousAngle = angle;

			SetLocalRotation( pose, constrained, NQuaternion.CreateFromAxisAngle( Axis( constrainedAxis ), angle ) );
		}

		/// <summary>
		/// The twist of a rotation around one of its axes, ignoring the tilt of the axis itself.
		/// </summary>
		private static float TwistAngle( NQuaternion rotation, int axis )
		{
			rotation = rotation == default ? NQuaternion.Identity : NQuaternion.Normalize( rotation );

			var basis = new[] { NVector3.Transform( NVector3.UnitX, rotation ), NVector3.Transform( NVector3.UnitY, rotation ), NVector3.Transform( NVector3.UnitZ, rotation ) };
			var axis1 = (axis + 1) % 3;
			var axis2 = (axis + 2) % 3;
			const float epsilon = 0.0001f;

			var diagonal = Math.Clamp( MathF.Floor( Get( basis[axis], axis ) * 1e7f + 0.5f ) / 1e7f, -1f, 1f );
			var tilt = diagonal < 0f ? MathF.Acos( diagonal + 1f ) : MathF.Acos( 1f - diagonal );

			if ( MathF.Abs( tilt - MathF.PI / 2f ) > epsilon )
			{
				var a = Get( basis[axis], axis1 );
				var b = Get( basis[axis], axis2 );
				var invLength = 1f / MathF.Max( MathF.Sqrt( a * a + b * b ), 1.17549435e-38f );
				a *= invLength;
				b *= invLength;

				var m1 = Get( basis[axis1], axis );
				var m2 = Get( basis[axis2], axis );
				return MathF.Atan2( m2 * a - m1 * b, -m1 * a - m2 * b );
			}

			var diagonal1 = Get( basis[axis1], axis1 );
			if ( MathF.Abs( diagonal1 - 1f ) <= epsilon )
				return 0f;

			return MathF.Atan2( Get( basis[axis1], axis2 ), diagonal1 );
		}
	}

	private sealed class Point( Constrained[] constrainedBones, Target[] targets ) : TargetConstraint( constrainedBones, targets )
	{
		public static Point Create( CS2BoneConstraint definition, BuildContext context )
			=> context.ResolveConstrained( definition.Bones ) is { } constrainedBones && context.ResolveTargets( definition.Targets ) is { } targets
				? new Point( constrainedBones, targets )
				: null;

		public override void Evaluate( FrameBone[] pose )
		{
			var (position, totalWeight) = BlendPositions( Targets.Length, pose );

			foreach ( var constrained in ConstrainedBones )
			{
				var weight = totalWeight * constrained.Weight;
				if ( MathF.Abs( weight ) <= 0.0001f )
					continue;

				var current = pose[constrained.Bone];
				current.Position = MathF.Abs( weight - 1f ) <= 0.0001f ? position : NVector3.Lerp( current.Position, position, weight );
				WriteBack( pose, constrained, current );
			}
		}
	}

	private sealed class Aim( CS2BoneConstraint definition, Constrained[] constrainedBones, Target[] targets ) : TargetConstraint( constrainedBones, targets )
	{
		private readonly NVector3 upVector = definition.UpVector;
		private readonly NQuaternion aimOffset = ToQuaternion( definition.AimOffset );

		public static Aim Create( CS2BoneConstraint definition, BuildContext context )
			=> context.ResolveConstrained( definition.Bones ) is { } constrainedBones && context.ResolveTargets( definition.Targets ) is { } targets
				? new Aim( definition, constrainedBones, targets )
				: null;

		public override void Evaluate( FrameBone[] pose )
		{
			var upTarget = FrameBone.Identity;
			var upType = definition.UpType;

			// Up types 0 and 2 read the last target; 1 and 3 still leave it out of the aim when there is more than one.
			if ( upType is 0 or 2 )
			{
				if ( Targets.Length <= 1 )
					return;

				upTarget = GetTarget( Targets[^1], pose );
			}
			else if ( Targets.Length == 0 )
			{
				return;
			}

			var (targetPosition, totalWeight) = BlendPositions( Targets.Length > 1 ? Targets.Length - 1 : Targets.Length, pose );

			foreach ( var constrained in ConstrainedBones )
			{
				var weight = totalWeight * constrained.Weight;
				if ( MathF.Abs( weight ) <= 0.0001f )
					continue;

				var current = pose[constrained.Bone];
				var parent = constrained.Parent >= 0 ? pose[constrained.Parent] : FrameBone.Identity;
				var parentInverse = parent.Inverse();

				var localPosition = parentInverse.TransformPoint( current.Position );

				var up = upType switch
				{
					1 => upVector,
					3 => NVector3.Transform( upVector, parent.Angle ),
					2 => MathUtils.SafeNormalize( upTarget.Position - parent.TransformPoint( localPosition ) ),
					_ => NVector3.Transform( upVector, upTarget.Angle ),
				};

				up = NVector3.Transform( up, NQuaternion.Conjugate( parent.Angle ) );
				var direction = MathUtils.SafeNormalize( parentInverse.TransformPoint( targetPosition ) - localPosition );

				var look = LookRotation( direction, up );
				var local = look * Align( look, aimOffset );
				var rotation = parent.Angle * Align( parent.Angle, local );

				current.Angle = MathF.Abs( weight - 1f ) <= 0.0001f ? rotation : NQuaternion.Slerp( current.Angle, rotation, weight );
				WriteBack( pose, constrained, current );
			}
		}

		/// <summary>
		/// +Y along the direction, +Z towards up.
		/// </summary>
		private static NQuaternion LookRotation( NVector3 forward, NVector3 up )
		{
			forward = MathUtils.SafeNormalize( forward );
			up = MathUtils.SafeNormalize( MathUtils.ProjectOntoPlane( up, forward ) );
			var side = MathUtils.SafeNormalize( NVector3.Cross( forward, up ) );

			var basis = new NMatrix(
				side.X, side.Y, side.Z, 0f,
				forward.X, forward.Y, forward.Z, 0f,
				up.X, up.Y, up.Z, 0f,
				0f, 0f, 0f, 1f );
			return NQuaternion.CreateFromRotationMatrix( basis );
		}
	}

	private static void SetLocalRotation( FrameBone[] pose, Constrained constrained, NQuaternion localRotation )
	{
		var current = pose[constrained.Bone];

		if ( constrained.Parent < 0 )
		{
			current.Angle = localRotation;
			WriteBack( pose, constrained, current );
			return;
		}

		var parent = pose[constrained.Parent];
		var local = parent.Inverse().Concat( current );
		local.Angle = localRotation;
		WriteBack( pose, constrained, parent.Concat( local ) );
	}

	/// <summary>
	/// Moves the bone and carries its descendants along.
	/// </summary>
	private static void WriteBack( FrameBone[] pose, Constrained constrained, FrameBone model )
	{
		var previous = pose[constrained.Bone];
		pose[constrained.Bone] = model;

		if ( constrained.Descendants.Length == 0 )
			return;

		// Descendants stay put when the change looks like identity, which tolerates ~11 degrees of rotation.
		if ( IsApproxIdentity( model.Concat( previous.Inverse() ) ) )
			return;

		var previousInverse = previous.Inverse();
		foreach ( var descendant in constrained.Descendants )
			pose[descendant] = model.Concat( previousInverse.Concat( pose[descendant] ) );
	}

	/// <summary>
	/// Transform equality against identity: 1e-4 on position and scale, 0.1 on each quaternion component.
	/// </summary>
	private static bool IsApproxIdentity( FrameBone transform )
	{
		const float positionEpsilon = 0.0001f;
		const float rotationEpsilon = 0.1f;

		if ( MathF.Abs( transform.Position.X ) > positionEpsilon || MathF.Abs( transform.Position.Y ) > positionEpsilon
			|| MathF.Abs( transform.Position.Z ) > positionEpsilon || MathF.Abs( transform.Scale - 1f ) > positionEpsilon )
		{
			return false;
		}

		var q = transform.Angle;
		var matches = MathF.Abs( q.X ) <= rotationEpsilon && MathF.Abs( q.Y ) <= rotationEpsilon && MathF.Abs( q.Z ) <= rotationEpsilon;
		return matches && (MathF.Abs( q.W - 1f ) <= rotationEpsilon || MathF.Abs( q.W + 1f ) <= rotationEpsilon);
	}

	private static NQuaternion Align( NQuaternion reference, NQuaternion q ) => NQuaternion.Dot( reference, q ) < 0f ? -q : q;

	private static NVector3 Axis( int axis ) => axis switch { 0 => NVector3.UnitX, 1 => NVector3.UnitY, _ => NVector3.UnitZ };

	private static float Get( NVector3 v, int axis ) => axis switch { 0 => v.X, 1 => v.Y, _ => v.Z };

	private static NQuaternion ToQuaternion( Rotation rotation ) => new( rotation.x, rotation.y, rotation.z, rotation.w );

	private static FrameBone ToFrame( Transform transform ) => new( transform.Position, transform.UniformScale, ToQuaternion( transform.Rotation ) );

	private static Transform ToTransform( FrameBone bone )
		=> new( bone.Position, new Rotation( bone.Angle.X, bone.Angle.Y, bone.Angle.Z, bone.Angle.W ), bone.Scale );
}
