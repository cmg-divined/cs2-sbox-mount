using CS2;
using System;
using System.IO;

namespace Sandbox.Mounting.CS2;

internal sealed partial class MapLoader
{
	/// <summary>
	/// The world nodes' static geometry: placed models (scene objects) and aggregates, the merged world geometry.
	/// </summary>
	private void BuildWorld( CompiledWorld world, GameObject parent )
	{
		var root = new GameObject( true, "World" );
		if ( parent is not null )
			root.SetParent( parent, false );

		var aggregates = new List<WorldAggregate>();

		for ( var nodeIndex = 0; nodeIndex < world.NodePrefixes.Length; nodeIndex++ )
		{
			var nodePath = world.NodePrefixes[nodeIndex] + ".vwnod_c";
			if ( !Host.FileExists( nodePath ) )
			{
				Log.Warning( $"{mapName}: world node '{world.NodePrefixes[nodeIndex]}' is missing." );
				continue;
			}

			var node = new CompiledWorldNode( Host.ReadFile( nodePath ) );

			foreach ( var (feature, count) in node.Unsupported )
				Log.Warning( $"{mapName}: {count} {feature} aren't placed yet." );

			var overrides = node.MaterialOverrides.ToLookup( x => x.SceneObject );

			foreach ( var sceneObject in node.SceneObjects )
				AddSceneObject( root, sceneObject, [.. overrides[sceneObject.Index]] );

			aggregates.AddRange( node.Aggregates );
		}

		AddAggregates( root, aggregates );
	}

	/// <summary>
	/// The aggregates' geometry (the world's merged geometry), regrouped by <see cref="WorldCell"/>: the game culls
	/// every fragment on its own, and an aggregate holds one material's fragments from all over the map, so drawn
	/// whole it could never be culled. Each cell is one scene object with a mesh per material. Aggregates are read
	/// in parallel; their chunks upload as they come, which bounds the memory the geometry takes on its way.
	/// </summary>
	private void AddAggregates( GameObject root, List<WorldAggregate> aggregates )
	{
		var cells = new Dictionary<WorldCell, List<(Mesh Mesh, uint LodMask)>>();
		var bounds = new Dictionary<WorldCell, BBox>();
		var failures = new System.Collections.Concurrent.ConcurrentQueue<string>();
		// A few finished aggregates waiting for upload at most: readers wait rather than pile them up in memory.
		using var results = new System.Collections.Concurrent.BlockingCollection<List<AggregateChunk>>( 4 );

		var reading = System.Threading.Tasks.Task.Run( () =>
		{
			try
			{
				var options = new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = Math.Max( 1, Environment.ProcessorCount - 1 ) };
				System.Threading.Tasks.Parallel.For( 0, aggregates.Count, options, index =>
				{
					var aggregate = aggregates[index];
					try
					{
						var path = aggregate.Model + "_c";
						if ( !Host.FileExists( path ) )
						{
							failures.Enqueue( $"aggregate model '{aggregate.Model}' is missing." );
							return;
						}

						var importer = new ModelImporter( Host, new CompiledModel( Host.ReadFile( path ) ), aggregate.Model );
						results.Add( importer.ReadAggregate( aggregate, index ) );
					}
					catch ( Exception e )
					{
						failures.Enqueue( $"aggregate '{aggregate.Model}' failed: {e.Message}" );
					}
				} );
			}
			finally
			{
				results.CompleteAdding();
			}
		} );

		// Meshes are created here, on the main thread, as the readers finish aggregates.
		foreach ( var chunks in results.GetConsumingEnumerable() )
		{
			foreach ( var chunk in chunks )
			{
				if ( chunk.Indices.Count == 0 )
					continue;

				Mesh mesh;
				try
				{
					mesh = ModelImporter.CreateChunkMesh( Host, chunk );
				}
				catch ( Exception e )
				{
					// Keep consuming, or the readers would block on a full queue.
					failures.Enqueue( $"geometry with '{chunk.Material}' failed to upload: {e.Message}" );
					continue;
				}

				if ( !cells.TryGetValue( chunk.Cell, out var meshes ) )
					cells[chunk.Cell] = meshes = [];

				meshes.Add( (mesh, chunk.LodMask) );
				bounds[chunk.Cell] = bounds.TryGetValue( chunk.Cell, out var cellBounds ) ? cellBounds.AddBBox( chunk.Bounds ) : chunk.Bounds;
			}
		}

		reading.Wait();

		foreach ( var failure in failures )
			Log.Warning( $"{mapName}: {failure}" );

		var cellRoot = new GameObject( true, "Aggregates" );
		cellRoot.SetParent( root, false );

		foreach ( var (cell, meshes) in cells.OrderBy( x => (x.Key.Z, x.Key.Y, x.Key.X, x.Key.LodAggregate) ) )
		{
			var name = (cell.X == int.MaxValue ? $"large_{cell.Y}_{cell.Z}" : $"cell_{cell.X}_{cell.Y}_{cell.Z}")
				+ (cell.NoShadows ? "_noshadows" : "") + (cell.LodAggregate >= 0 ? $"_lod{cell.LodAggregate}" : "");
			var model = BuildCellModel( $"{_worldFolder}/aggregates/{name}", cell, meshes, bounds[cell], aggregates );
			if ( model is null )
				continue;

			var gameObject = new GameObject( true, name );
			gameObject.SetParent( cellRoot, false );

			var renderer = gameObject.Components.Create<ModelRenderer>();
			renderer.Model = model;

			if ( cell.NoShadows || _skybox )
				renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
		}
	}

	/// <summary>
	/// A cell's meshes as one model, each on the levels of detail its fragments are in.
	/// </summary>
	private Model BuildCellModel( string name, WorldCell cell, List<(Mesh Mesh, uint LodMask)> meshes, BBox bounds, List<WorldAggregate> aggregates )
	{
		var builder = Model.Builder.WithName( name );

		// A setup's switch distances apply to the levels its fragments are in.
		if ( cell.LodAggregate >= 0 && aggregates[cell.LodAggregate].LodSetups is [var setup, ..] )
		{
			for ( var lod = 1; lod < setup.SwitchDistances.Length && lod < ModelImporter.MaxLods; lod++ )
				builder.WithLodDistance( lod, setup.SwitchDistances[lod] );
		}

		foreach ( var (mesh, lodMask) in meshes )
		{
			if ( lodMask == 0 )
			{
				builder.AddMesh( mesh );
				continue;
			}

			for ( var lod = 0; lod < ModelImporter.MaxLods; lod++ )
			{
				if ( (lodMask & (1u << lod)) != 0 )
					builder.AddMesh( mesh, lod );
			}
		}

		builder.WithViewBounds( bounds );

		var model = builder.Create();
		if ( model is null || model.IsError )
			return null;

		_models.Add( model );
		return model;
	}

	private void AddSceneObject( GameObject root, WorldSceneObject sceneObject, WorldMaterialOverride[] overrides )
	{
		// Light blockers (toolssolidblocklight meshes, BlockLight) are there to block light, never to be seen: the ones
		// that also have NoShadows only shaped the baked lighting, the others cast shadows. A skybox's cast none on the
		// map.
		var blocksLight = (sceneObject.Flags & ObjectTypeFlags.BlockLight) != 0;
		if ( blocksLight && ((sceneObject.Flags & ObjectTypeFlags.NoShadows) != 0 || _skybox) )
			return;

		if ( LoadModel( sceneObject.Model ) is not { } model )
		{
			// Some maps place models that didn't ship with the game; say so once per model.
			if ( !string.IsNullOrEmpty( sceneObject.Model ) && _missingModels.Add( sceneObject.Model ) )
				Log.Warning( $"{mapName}: scene object model '{sceneObject.Model}' isn't in the game's content." );

			return;
		}

		var gameObject = new GameObject( true, System.IO.Path.GetFileNameWithoutExtension( sceneObject.Model ) );
		gameObject.SetParent( root, false );
		gameObject.WorldTransform = ToTransform( sceneObject.Transform );

		var renderer = gameObject.Components.Create<ModelRenderer>();
		renderer.Model = model;

		// A zero alpha means no tint was set.
		if ( sceneObject.TintColor is { Length: 4 } tint && tint[3] != 0 )
			renderer.Tint = new Color( tint[0], tint[1], tint[2], tint[3] );

		if ( !string.IsNullOrEmpty( sceneObject.Skin ) )
			renderer.MaterialGroup = sceneObject.Skin;

		if ( blocksLight )
			renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
		else if ( (sceneObject.Flags & ObjectTypeFlags.NoShadows) != 0 || _skybox )
			renderer.RenderType = ModelRenderer.ShadowRenderType.Off;

		if ( sceneObject.LodOverride >= 0 )
			renderer.LodOverride = sceneObject.LodOverride;

		ApplyMaterialOverrides( renderer, sceneObject, overrides );
	}

	/// <summary>
	/// Replaces the materials the world node overrides on this object. s&amp;box overrides a model's material
	/// slots, so an overridden draw call's material changes wherever the model uses that material.
	/// </summary>
	private void ApplyMaterialOverrides( ModelRenderer renderer, WorldSceneObject sceneObject, WorldMaterialOverride[] overrides )
	{
		if ( overrides.Length == 0 )
			return;

		if ( overrides.FirstOrDefault( x => x.IsWholeModel ) is { } whole )
		{
			renderer.MaterialOverride = Host.LoadMaterial( whole.Material );
			return;
		}

		var compiled = new CompiledModel( Host.ReadFile( sceneObject.Model + "_c" ) );

		foreach ( var materialOverride in overrides )
		{
			var mesh = compiled.Meshes.FirstOrDefault( x => x.MeshIndex == materialOverride.SubSceneObject );
			var drawCall = mesh?.DrawCalls.ElementAtOrDefault( (int)materialOverride.DrawCall );
			if ( drawCall is null )
				continue;

			var original = Host.LoadMaterial( drawCall.Material );
			for ( var slot = 0; slot < renderer.Materials.Count; slot++ )
			{
				if ( renderer.Materials.GetOriginal( slot ) == original )
					renderer.Materials.SetOverride( slot, Host.LoadMaterial( materialOverride.Material ) );
			}
		}
	}
}
