using Godot;
using System.Collections.Generic;
using System.Linq;

// the area3d uses the collisionshape3d
// this script uses values from the meshinstance3d
// so make sure the collisionshape3d and meshinstance3d child of this area3d represent the same thing

// smaller mesh instance corresponds to the deeper explosion
public partial class VSTSplittingComponent : Area3D
{
	// the layer of the VST that the fragments will be removed from, where the vstroot is the 0th layer
	// [Export(PropertyHint.Range, "1,8")] int explosionDepth = 2;

	[Export] MeshInstance3D explosionMeshSmall;
	[Export] MeshInstance3D explosionMeshLarge;

	// all other children of this node will be queuefreed() after ready function (see end of ready for reasons)
	[Export] CollisionShape3D mainCollisionShape;

	[Export] int explosionTreeDepthDeep = 2;
	[Export] int explosionTreeDepthShallow = 4;

	[Export] bool ApplyImpulseOnSplit = false;
	[Export] float ImpulseStrength = 10.0f;

	// this is set to be the radius of the sphere for now
	float explosionDistancesSmall;
	float explosionDistancesLarge;

	int framesUntilCloseExplosion = -10;

	[Export] MeshInstance3D point;

	public override void _Ready()
	{
		base._Ready();
	
		if ((explosionTreeDepthDeep <= 0) || (explosionTreeDepthDeep <= 0))
		{
			GD.PrintErr("explosionDepths have to be strictly greater than 0");
			return;
		}

		if (explosionMeshSmall.Mesh is not SphereMesh sphereMeshSmall ||
			explosionMeshLarge.Mesh is not SphereMesh sphereMeshLarge)
		{
			GD.PrintErr("only spheremeshes are supported for VSTSplittingComponenets for now. i.e. only spherically symmetric explosions can split VSTNodes.");
			return;
		}

		explosionDistancesSmall = sphereMeshSmall.Radius;
		explosionDistancesLarge = sphereMeshLarge.Radius;

		
		// this is dumb AF but I _believe_ that if the centre of the destronoinode is within some other object (in this case the small mesh)
		// then the area3d will just REFUSE to detect the destronoinode
		// we can bodge this by just queuefreeing ALL CHILDREN of this area3d that aren't the collisionshape
		// (that way even usused guides won't mess with overlappingbodies detection)
		// probably shit code and this will definitely fuck me in the future
		// foreach(Node child in GetChildren())
		// {
		// 	if (child != mainCollisionShape)
		// 	{
		// 		child.QueueFree();
		// 	}
		// }
		
		// if (GetChildCount() != 1)
		// {
		// 	GD.PrintErr("should only have 1 child at end of ready, but either 0 or 2+ were detected.");
		// }
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (!Input.IsActionJustPressed("splitting_explosion"))
		{
			return;
		}

		GD.Print("splitting (large scale)");

		GD.Print(GetOverlappingBodies());

		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestronoiNode destronoiNode)
			{
				continue;
			}

			SplitExplode(destronoiNode, explosionDistancesLarge, explosionTreeDepthShallow, new StandardMaterial3D());
		}

		framesUntilCloseExplosion = 5;
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

		// GD.Print("overlapping: ", GetOverlappingBodies());

		framesUntilCloseExplosion -= 1;

		if (framesUntilCloseExplosion == 0)
		{
			GD.Print("2ndary");

			CloserExplosion();
		}
	}

	public void CloserExplosion()
	{
		StandardMaterial3D material = new()
		{
			AlbedoColor = new Color(GD.Randf(), GD.Randf(), GD.Randf())
		};

		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestronoiNode destronoiNode)
			{
				continue;
			}

			SplitExplode(destronoiNode, explosionDistancesSmall, explosionTreeDepthDeep, material);
		}
	}

	public void SplitExplode(DestronoiNode destronoiNode,
							float explosionDistanceMax,
							int explosionTreeDepth,
							StandardMaterial3D debugMaterial)
	{
		VSTNode originalVSTRoot = destronoiNode.vstRoot;

		if (originalVSTRoot.left is null && originalVSTRoot.right is null)
		{
			destronoiNode.QueueFree();
		}

		if (explosionTreeDepth > destronoiNode.treeHeight)
		{
			explosionTreeDepth = destronoiNode.treeHeight;
		}

		if (explosionTreeDepth <= 1)
		{
			GD.Print("not enough treeDepth left");
			destronoiNode.QueueFree();
			return;
		}


		List<VSTNode> fragmentsAtGivenDepth = [];

		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, originalVSTRoot, explosionTreeDepth, originalVSTRoot.ownerID);

		GD.Print(originalVSTRoot.ID, " ", originalVSTRoot.ownerID, " ", originalVSTRoot.left, " ", originalVSTRoot.right);

		List<VSTNode> fragmentsToRemove = [];
		List<VSTNode> fragmentsToKeep = [];

		foreach (VSTNode vstnode in fragmentsAtGivenDepth)
		{
			Vector3 globalLeafPosition = destronoiNode.GlobalTransform * vstnode.meshInstance.GetAabb().GetCenter();

			MeshInstance3D meshInstance = (MeshInstance3D)point.Duplicate();
			meshInstance.TopLevel = true;
			destronoiNode.fragmentContainer.AddChild(meshInstance);
			meshInstance.GlobalPosition = globalLeafPosition;

			if ((globalLeafPosition - GlobalPosition).Length() < explosionDistanceMax)
			{
				fragmentsToRemove.Add(vstnode);

				// remove reference to this node from the parent's section of the tree
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
		// ie in this case, nothing would be changed. nothing about the vst either i believe (hence no need to clean the VST)
		// so we can save some computation and skip it. also prevents bodies moving about weirdly (duplicating them resets their position)
		if (fragmentsToRemove.Count == 0)
		{
			GD.Print("no fragments to remove");
			return;
		}

		// remove original object
		destronoiNode.QueueFree();

		// create small fragments

		int fragmentNumber = 0;

		foreach (VSTNode leaf in fragmentsToRemove)
		{
			string leafName = destronoiNode.Name + $"_fragment_{fragmentNumber}";
			
			GD.Print("Creating Individual DN");

			RigidBody3D body = destronoiNode.CreateDestronoiNode(leaf,
																leaf.meshInstance,
																destronoiNode.treeHeight - leaf.level,
																leafName,
																debugMaterial);
			
			ReduceLevelsAndReplaceOwnership(leaf, 0, leaf.ID, leaf);

			destronoiNode.fragmentContainer.AddChild(body);

			if (ApplyImpulseOnSplit)
			{
				body.ApplyCentralImpulse(new Vector3(1,1,1) * ImpulseStrength);
			}

			fragmentNumber++;
		}

		CleanVST(originalVSTRoot, explosionTreeDepth);

		// loop through originalVSTRoot (from deepest point to shallowest) and replace all meshes with the union of their children
		// OR just refrag / reinitialise the subtrees...
		ReFragTree(originalVSTRoot);

		if (fragmentsToKeep.Count == 0)
		{
			GD.Print("no fragmentsToKeep");
			return;
		}

		// create single body from fragmentstokeep

		List<MeshInstance3D> meshInstances = [.. fragmentsToKeep.Select(f => f.meshInstance)];

		MeshInstance3D overlappingCombinedMeshesToKeep = CombineMeshes(meshInstances);

		// MeshInstance3D finalCombinedMeshes = RemoveDuplicateSurfaces(overlappingCombinedMeshesToKeep);

		string combinedFragmentName = destronoiNode.Name + "_remaining_fragment";

		GD.Print("Creating Combined DN");

		DestronoiNode destronoiNodeToKeep = destronoiNode.CreateDestronoiNode(originalVSTRoot,
																			overlappingCombinedMeshesToKeep,
																			destronoiNode.treeHeight,
																			combinedFragmentName,
																			debugMaterial);

		destronoiNode.fragmentContainer.AddChild(destronoiNodeToKeep);
	}

	// replace all nodes' meshInstances with the union of their 2 children's meshInstances
	public void ReFragTree(VSTNode vstNode)
	{
		List<MeshInstance3D> meshesToCombine = [];

		if (vstNode.left is not null)
		{
			ReFragTree(vstNode.left);
			if (vstNode.left is not null)
			{
				meshesToCombine.Add(vstNode.left.meshInstance);
			}
		}
		if (vstNode.right is not null)
		{
			ReFragTree(vstNode.right);
			if (vstNode.right is not null)
			{
				meshesToCombine.Add(vstNode.right.meshInstance);
			}
		}

		if (meshesToCombine.Count == 2)
		{
			vstNode.meshInstance = CombineMeshes(meshesToCombine);
			return;
		}
		if (meshesToCombine.Count == 1)
		{
			vstNode.meshInstance = meshesToCombine[0];
			return;
		}
	}

	/// <summary>
	///  Remove nodes who have 2 children removed (as now their mesh doesn't correspond to a physical node)
	/// and fast forward references to nodes which reference only 1 node with the same owner ID
	/// cleans all layers which are equal to or shallower than the input explosionTreeDepth
	/// </summary>
	public static void CleanVST(VSTNode originalVSTRoot, int explosionTreeDepth)
	{
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

				continue;
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

				continue;
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
				
				continue;
			}
		}

		// repeat recursively
		if (explosionTreeDepth == 2)
		{
			return;
		}

		CleanVST(originalVSTRoot, explosionTreeDepth - 1);
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
	public static void ReduceLevelsAndReplaceOwnership(VSTNode leaf, int currentLevel, int newOwnerID, VSTNode newParent)
	{
		leaf.level = currentLevel;
		leaf.ownerID = newOwnerID;
		leaf.parent = newParent;

		// exit at end of tree
		if (leaf.left is not null)
		{
			ReduceLevelsAndReplaceOwnership(leaf.left, currentLevel + 1, newOwnerID, newParent);
		}

		if (leaf.right is not null)
		{
			ReduceLevelsAndReplaceOwnership(leaf.right, currentLevel + 1, newOwnerID, newParent);
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
