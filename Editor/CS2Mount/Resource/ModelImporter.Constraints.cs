using CS2;
using System;
using System.IO;

namespace Sandbox.Mounting.CS2;

internal sealed partial class ModelImporter
{
	/// <summary>
	/// The constraint classes <see cref="CS2BoneConstraintSolver"/> solves.
	/// </summary>
	private static readonly HashSet<string> SolvedConstraints = ["CTiltTwistConstraint", "CPointConstraint", "CAimConstraint"];

	/// <summary>
	/// The model's bone constraints (its key values' <c>BoneConstraintList</c>), by imported bone name, in their order.
	/// Ones naming a bone the model doesn't have, or following an attachment, are left out.
	/// </summary>
	private List<CS2BoneConstraint> ReadBoneConstraints( string[] boneNames )
	{
		var text = model.Data.GetObject( "m_modelInfo" )?.GetString( "m_keyValueText" );
		if ( string.IsNullOrEmpty( text ) || !text.Contains( "BoneConstraintList" ) )
			return null;

		KVObject keyValues;
		try
		{
			keyValues = KV3TextReader.Read( text );
		}
		catch ( InvalidDataException e )
		{
			Log.Warning( $"{name}: couldn't read its key values: {e.Message}" );
			return null;
		}

		// Constraints name bones by hash, unless they give the name itself.
		var bonesByHash = new Dictionary<uint, string>();
		for ( var i = 0; i < model.Bones.Length && i < boneNames.Length; i++ )
		{
			if ( boneNames[i] is not null )
				bonesByHash.TryAdd( global::CS2.StringToken.Get( model.Bones[i].Name ), boneNames[i] );
		}

		string FindBone( KVObject data )
		{
			var hash = data.GetString( "m_sName" ) is { Length: > 0 } boneName ? global::CS2.StringToken.Get( boneName ) : unchecked((uint)data.GetInt64( "m_nBoneHash" ));
			return bonesByHash.GetValueOrDefault( hash );
		}

		var constraints = new List<CS2BoneConstraint>();
		var skipped = 0;

		foreach ( var data in keyValues.GetObjects( "BoneConstraintList" ) )
		{
			var className = data.GetString( "_class" );
			if ( className is null || !SolvedConstraints.Contains( className ) )
				continue;

			var constraint = new CS2BoneConstraint
			{
				Class = className,
				TargetAxis = data.GetInt32( "m_nTargetAxis" ),
				ConstrainedAxis = data.GetInt32( "m_nSlaveAxis" ),
				UpType = data.GetInt32( "m_nUpType" ),
				UpVector = ReadVector( data.GetArray( "m_vUpVector" ) ),
				AimOffset = ReadRotation( data.GetArray( "m_qAimOffset" ) ),
			};

			var complete = true;

			foreach ( var slave in data.GetObjects( "m_slaves" ) )
			{
				var bone = FindBone( slave );
				complete &= bone is not null;
				constraint.Bones.Add( new CS2ConstraintBone { Bone = bone, Weight = slave.GetFloat( "m_flWeight" ) } );
			}

			foreach ( var target in data.GetObjects( "m_targets" ) )
			{
				var bone = target.GetBool( "m_bIsAttachment" ) ? null : FindBone( target );
				complete &= bone is not null;
				constraint.Targets.Add( new CS2ConstraintBone
				{
					Bone = bone,
					Weight = target.GetFloat( "m_flWeight" ),
					Position = ReadVector( target.GetArray( "m_vOffset" ) ),
					Rotation = ReadRotation( target.GetArray( "m_qOffset" ) ),
				} );
			}

			if ( complete )
				constraints.Add( constraint );
			else
				skipped++;
		}

		if ( skipped > 0 )
			Log.Info( $"{name}: {skipped} bone constraints left out, for a bone it doesn't have or an attachment they follow." );

		return constraints;
	}

	private static Vector3 ReadVector( object[] values )
		=> values is { Length: >= 3 } ? new Vector3( KVValue.ToFloat( values[0] ), KVValue.ToFloat( values[1] ), KVValue.ToFloat( values[2] ) ) : Vector3.Zero;

	private static Rotation ReadRotation( object[] values )
		=> values is { Length: >= 4 } ? new Rotation( KVValue.ToFloat( values[0] ), KVValue.ToFloat( values[1] ), KVValue.ToFloat( values[2] ), KVValue.ToFloat( values[3] ) ) : Rotation.Identity;
}
