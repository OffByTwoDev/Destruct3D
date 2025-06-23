using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

// the area3d uses the collisionshape3d
// this script uses values from the meshinstance3d
// so make sure the collisionshape3d and meshinstance3d child of this area3d represent the same thing

public partial class VSTSplittingComponent : Area3D
{
	// the layer of the VST that the fragments will be removed from, where the vstroot is the 0th layer
	// [Export(PropertyHint.Range, "1,8")] int explosionDepth = 2;

	[Export] Godot.Collections.Array<MeshInstance3D> explosionMeshes;
	[Export] Godot.Collections.Array<int> explosionTreeDepths = [2, 4, 6];

	[Export] bool ApplyImpulseOnSplit = false;

	// this is set to be the radius of the sphere for now
	List<float> explosionDistances = [];

	public override void _Ready()
	{
		base._Ready();

		// --- error checking & setting up explosionDistances --- //
		if (explosionMeshes.Count != explosionTreeDepths.Count)
		{
			GD.PrintErr("explosionRanges & explosionDepths must have the same length. returning early.");
			return;
		}
		
		foreach (int explosionDepth in explosionTreeDepths)
		{
			if (!(0 < explosionDepth))
			{
				GD.PrintErr("explosionDepths have to be greater than 0");
				return;
			}
		}

		for (int i = 0; i < explosionMeshes.Count; i++)
		{
			if (explosionMeshes[i].Mesh is not SphereMesh sphereMesh)
			{
				GD.PrintErr("only spheremeshes are supported for VSTSplittingComponenets for now. i.e. only spherically symmetric explosions can split VSTNodes.");
				return;
			}

			if (i > 1 && sphereMesh.Radius > explosionDistances[i])
			{
				GD.PrintErr("meshes must be specified in terms of increasing size. returning early.");
				return;
			}

			explosionDistances.Add(sphereMesh.Radius);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (!Input.IsActionJustPressed("splitting_explosion"))
		{
			return;
		}

		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestronoiNode destronoiNode)
			{
				continue;
			}

			for (int i = 0; i < explosionMeshes.Count; i++)
			{
				if (i == 0)
				{
					SplitExplode(destronoiNode, 0, explosionDistances[i], explosionTreeDepths[i]);
				}
				else
				{
					SplitExplode(destronoiNode, explosionDistances[i-1], explosionDistances[i], explosionTreeDepths[i]);
				}
			}
		}
	}

	public void SplitExplode(DestronoiNode destronoiNode,
							float explosionDistanceMin,
							float explosionDistanceMax,
							int explosionTreeDepth)
	{
		VSTNode originalVSTRoot = destronoiNode.vstRoot;

		if (destronoiNode.treeHeight < explosionTreeDepth)
		{
			// destronoiNode.QueueFree();
			// replace with some particle effects
			return;
		}

		List<VSTNode> fragmentsAtGivenDepth = [];

		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, originalVSTRoot, explosionTreeDepth, originalVSTRoot.ownerID);

		List<VSTNode> fragmentsToRemove = [];
		List<VSTNode> fragmentsToKeep = [];

		foreach (VSTNode vstnode in fragmentsAtGivenDepth)
		{
			Vector3 leafPosition = destronoiNode.GlobalTransform * vstnode.meshInstance.GetAabb().GetCenter();

			if (explosionDistanceMin < (leafPosition - GlobalPosition).Length() &&
				(leafPosition - GlobalPosition).Length() < explosionDistanceMax)
			{
				fragmentsToRemove.Add(vstnode);

				// remove reference to this node in originalTree
				if (vstnode.laterality == Laterality.LEFT)
				{
					vstnode.parent.left = null;
				}
				else
				{
					vstnode.parent.right = null;
				}

			}
			else
			{
				fragmentsToKeep.Add(vstnode);
			}
		}

		// if we are removing no fragments, then the rest of this code is just gonna create a duplicate of the current node
		// so we can save some computation and skip it. also prevents bodies moving about weirdly (duplicating them resets their position)
		if (fragmentsToRemove.Count == 0)
		{
			return;
		}

		// remove original object
		destronoiNode.QueueFree();

		// create small fragments

		int fragmentNumber = 0;

		foreach (VSTNode leaf in fragmentsToRemove)
		{
			string leafName = destronoiNode.Name + $"_fragment_{fragmentNumber}";
			
			RigidBody3D body = destronoiNode.CreateDestronoiNode(leaf,
																leaf.meshInstance,
																destronoiNode.treeHeight - leaf.level,
																leafName);
			
			ReduceLevelsAndReplaceOwnership(leaf, 0, leaf.ID);

			destronoiNode.fragmentContainer.AddChild(body);

			if (ApplyImpulseOnSplit)
			{
				body.ApplyCentralImpulse(new Vector3(1,1,1) * 0.01f);
			}

			fragmentNumber++;
		}

		// create single body from fragmentstokeep

		List<MeshInstance3D> meshInstances = [.. fragmentsToKeep.Select(f => f.meshInstance)];

		MeshInstance3D overlappingCombinedMeshesToKeep = CombineMeshes(meshInstances);

		// MeshInstance3D finalCombinedMeshes = RemoveDuplicateSurfaces(overlappingCombinedMeshesToKeep);

		string combinedFragmentName = destronoiNode.Name + "_remaining_fragment";

		GD.Print("creatingDN");
		DestronoiNode destronoiNodeToKeep = destronoiNode.CreateDestronoiNode(originalVSTRoot,
																			overlappingCombinedMeshesToKeep,
																			destronoiNode.treeHeight,
																			combinedFragmentName);

		destronoiNode.fragmentContainer.AddChild(destronoiNodeToKeep);

		// --- clean VST --- //
		// i.e. remove nodes who have 2 children removed (as now their mesh doesn't correspond to a physical node)
		// and fast forward references to nodes which reference only 1 node with the same owner ID

		List<VSTNode> parentNodes = [];
		InitialiseFragmentsAtGivenDepth(parentNodes, originalVSTRoot, explosionTreeDepth - 1, originalVSTRoot.ownerID);

		foreach (VSTNode parentNode in parentNodes)
		{
			if ( (parentNode.left is null) &&
				 (parentNode.right is null) )
			{
				// set parent reference to this parentNode as null, this node does not represent anything present in the scene anymore
				if (parentNode.laterality == Laterality.LEFT)
				{
					parentNode.parent.left = null;
				}
				else
				{
					parentNode.parent.right = null;
				}
				return;
			}

			if (parentNode.left is null)
			{
				// fast forward parent to right child
				if (parentNode.laterality == Laterality.LEFT)
				{
					parentNode.parent.left = parentNode.right;
				}
				else
				{
					parentNode.parent.right = parentNode.right;
				}

				return;
			}

			if (parentNode.right is null)
			{
				// fast forward parent to left child
				if (parentNode.laterality == Laterality.LEFT)
				{
					parentNode.parent.left = parentNode.left;
				}
				else
				{
					parentNode.parent.right = parentNode.left;
				}
				
				return;
			}
		}

		// rigidBodyToKeep.GlobalPosition = destronoiNode.GlobalPosition;
	}

	public static void InitialiseFragmentsAtGivenDepth(List<VSTNode> fragmentsAtGivenDepth,
												VSTNode currentVSTNode,
												int depth,
												int rootOwnerID)
	{
		// represents a node not in the tree
		if (currentVSTNode is null)
		{
			return;
		}

		// if this node has been removed from the tree, it will have a different ownerID
		// in that case, this leaf is unphysical (i.e. its part of a distinct object now) and we should return nothing
		if (currentVSTNode.ownerID != rootOwnerID)
		{
			return;
		}

		if (currentVSTNode.level == depth)
		{
			fragmentsAtGivenDepth.Add(currentVSTNode);
			return;
		}

		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, currentVSTNode.left, depth, rootOwnerID);
		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, currentVSTNode.right, depth, rootOwnerID);
	}

	/// <summary>
	/// a function which renumbers the levels of a given leaf
	/// so that the given leaf and all its children have the correct level as per the initial input currentLevel
	/// </summary>
	/// <param name="leaf"></param>
	/// <param name="currentLevel"></param>
	/// <param name="newOwnerID"></param>
	public static void ReduceLevelsAndReplaceOwnership(VSTNode leaf, int currentLevel, int newOwnerID)
	{
		leaf.level = currentLevel;
		leaf.ownerID = newOwnerID;

		// exit at end of tree
		if (leaf.left is not null)
		{
			ReduceLevelsAndReplaceOwnership(leaf.left,currentLevel + 1, newOwnerID);
		}

		if (leaf.right is not null)
		{
			ReduceLevelsAndReplaceOwnership(leaf.right,currentLevel + 1, newOwnerID);
		}
	}

	// --- mesh combining schenanigans --- //

	public static MeshInstance3D CombineMeshes(List<MeshInstance3D> meshInstances)
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		foreach (var meshInstance in meshInstances)
		{
			var mesh = meshInstance.Mesh;
			if (mesh is null)
			{
				continue;
			}

			// Append all surfaces from this mesh with the MeshInstance's transform
			int surfaceCount = mesh.GetSurfaceCount();
			for (int s = 0; s < surfaceCount; s++)
			{
				// maybe this should be baseObject.GlobalPosition + meshInstance.Transform?
				// or maybe its fine as is idk...
				surfaceTool.AppendFrom(mesh, s, meshInstance.Transform);
			}
		}

		var combinedArrayMesh = surfaceTool.Commit();

		return new MeshInstance3D
		{
			Mesh = combinedArrayMesh
		};
	}

	// DEPRECATED
	// removes all internal vertices from a mesh
	// public static MeshInstance3D ConvertOverlappingMeshToExternalMesh(MeshInstance3D overlappingMeshInstance)
	// {
	// 	// list of all vertices that are on the surface of a mesh and not inside the mesh boundary
	// 	List<Vector3> boundaryPoints = GetBoundaryPoints(overlappingMeshInstance);

	// 	var surfaceTool = new SurfaceTool();
	// 	surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

	// 	foreach (Vector3 vertex in boundaryPoints)
	// 	{
	// 		surfaceTool.AddVertex(vertex);
	// 	}

	// 	ArrayMesh arrayMesh = surfaceTool.Commit();

	// 	return new MeshInstance3D()
	// 	{
	// 		Mesh = arrayMesh
	// 	};
	// }

	// public static List<Vector3> GetBoundaryPoints(MeshInstance3D meshInstance)
	// {
	// 	List<Vector3> boundaryPoints = [];

	// 	var mdt = new MeshDataTool();

	// 	if (meshInstance.Mesh is not ArrayMesh arrayMesh)
	// 	{
	// 		GD.PushError("arraymesh must be passed to GetInteriorPoints, not any other type of mesh");
	// 		return null;
	// 	}

	// 	mdt.CreateFromSurface(arrayMesh, 0);

	// 	if (mdt.GetFaceCount() == 0)
	// 	{
	// 		GD.PushWarning("no faces found in meshdatatool, GetBoundaryPoints will loop forever. returning early");
	// 		return null;
	// 	}

	// 	var direction = Vector3.Up;

	// 	for (int i = 0; i < mdt.GetVertexCount(); i++)
	// 	{
	// 		Vector3 currentVertex = mdt.GetVertex(i);

	// 		int intersections = 0;

	// 		for (int face = 0; face < mdt.GetFaceCount(); face++)
	// 		{
	// 			int v0 = mdt.GetFaceVertex(face, 0);
	// 			int v1 = mdt.GetFaceVertex(face, 1);
	// 			int v2 = mdt.GetFaceVertex(face, 2);
	// 			var p0 = mdt.GetVertex(v0);
	// 			var p1 = mdt.GetVertex(v1);
	// 			var p2 = mdt.GetVertex(v2);

	// 			Variant intersectionPoint = Geometry3D.RayIntersectsTriangle(currentVertex, direction, p0, p1, p2);
	// 			if (intersectionPoint.VariantType != Variant.Type.Nil)
	// 			{
	// 				intersections++;
	// 			}
	// 		}

			
	// 		// if number of intersections is odd, its inside
	// 		// if number of intersections is even, its outside
	// 		if (intersections % 2 == 0)
	// 		{
	// 			boundaryPoints.Add(currentVertex);
	// 		}
	// 	}

	// 	return boundaryPoints;
	// }

	public static MeshInstance3D RemoveDuplicateSurfaces(MeshInstance3D meshInstance3D)
	{
		if (meshInstance3D.Mesh is not ArrayMesh arrayMesh)
		{
			GD.PrintErr("mesh passed to RemoveDuplicateSurfaces must have an arraymesh mesh. returning early");
			return null;
		}

		HashSet<int> surfaceIndicesToRemove = [];

		for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
		{
			for (int j = 0; j < arrayMesh.GetSurfaceCount(); j++)
			{
				if (MeshUtils.AreMeshVerticesEqual(arrayMesh, i, j))
				{
					surfaceIndicesToRemove.Add(i);
					GD.Print("removing surface");
				}
			}
		}

		ArrayMesh newArrayMesh = new();

		for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
		{
			if (surfaceIndicesToRemove.Contains(i))
			{
				continue;
			}

			var arrays = arrayMesh.SurfaceGetArrays(i);
			var primitive = arrayMesh.SurfaceGetPrimitiveType(i);

			newArrayMesh.AddSurfaceFromArrays(primitive, arrays);

		}

		MeshInstance3D newMesh = new()
		{
			Mesh = newArrayMesh
		};

		return newMesh;
	}
}
