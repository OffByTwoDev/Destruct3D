using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CDestronoi;

// the area3d uses the collisionshape3d
// this script uses values from the meshinstance3d
// so make sure the collisionshape3d and meshinstance3d child of this area3d represent the same thing

// smaller mesh instance corresponds to the deeper explosion
public partial class VSTSplittingComponent : Area3D
{
	/// <summary>the layer of the VST that the fragments will be removed from, where the vstroot is the 0th layer</summary>
	[Export(PropertyHint.Range, "1,8")] int explosionDepth = 2;

	[Export] private MeshInstance3D explosionMeshSmall;
	[Export] private MeshInstance3D explosionMeshLarge;

	[Export] private int explosionTreeDepthDeep = 2;
	[Export] private int explosionTreeDepthShallow = 2;

	[Export] private bool ApplyImpulseOnSplit = false;
	[Export] private float ImpulseStrength = 1.0f;

	// these are to be set to be the radius of the sphere
	private float explosionDistancesSmall;
	private float explosionDistancesLarge;

	[Export] private bool DebugPrints = false;
	/// <summary>if true, the secondary explosion has a randomly coloured material (random for each explosion, i.e. one colour per explosion not per fragment)</summary>
	[Export] private bool DebugMaterialsOnSecondaryExplosion = false;

	// material to set for fragments
	private StandardMaterial3D fragmentMaterial = new();

	public override void _Ready()
	{
		base._Ready();
	
		if ((explosionTreeDepthDeep <= 0) || (explosionTreeDepthDeep <= 0))
		{
			GD.PushError("explosionDepths have to be strictly greater than 0");
			return;
		}

		if (explosionMeshSmall.Mesh is not SphereMesh sphereMeshSmall ||
			explosionMeshLarge.Mesh is not SphereMesh sphereMeshLarge)
		{
			GD.PushError("only spheremeshes are supported for VSTSplittingComponenets for now. i.e. only spherically symmetric explosions can split VSTNodes.");
			return;
		}

		explosionDistancesSmall = sphereMeshSmall.Radius;
		explosionDistancesLarge = sphereMeshLarge.Radius;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (!Input.IsActionJustPressed("splitting_explosion"))
		{
			return;
		}

		_ = Activate();
	}

	public async Task Activate()
	{
		if (DebugPrints)
		{
			GD.Print("primary explosion");
		}

		ShallowExplosion();

		// await so there's enough time for fragments to be instantiated
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		if (DebugPrints)
		{
			GD.Print("secondary explosion");
		}

		CloserExplosion();
	}

	/// <summary>carry out the explosion that goes to a tree depth of explosionTreeDepthShallow</summary>
	private void ShallowExplosion()
	{
		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestronoiNode destronoiNode)
			{
				continue;
			}

			SplitExplode(destronoiNode, explosionDistancesLarge, explosionTreeDepthShallow, new StandardMaterial3D());
		}
	}

	private void CloserExplosion()
	{
		if (DebugMaterialsOnSecondaryExplosion)
		{
			fragmentMaterial.AlbedoColor = new Color(GD.Randf(), GD.Randf(), GD.Randf());
		}

		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestronoiNode destronoiNode)
			{
				continue;
			}

			SplitExplode(destronoiNode, explosionDistancesSmall, explosionTreeDepthDeep, fragmentMaterial);
		}
	}

	public void SplitExplode(DestronoiNode destronoiNode,
							float explosionDistanceMax,
							int explosionTreeDepth,
							StandardMaterial3D fragmentMaterial)
	{
		VSTNode originalVSTRoot = destronoiNode.vstRoot;

		// desired explosionTreeDepth is relative to a body's root node, hence we add the depth of the root node
		explosionTreeDepth += originalVSTRoot.level;

		// GD.Print(explosionTreeDepth);

		// if (explosionTreeDepth > deepestNode)
		// {
			// set explosionTreeDepth to deepestNode i.e. just remove the smallest thing we have
			// OR create a particle effect for the fragments to remove
		// }

		// might also want to queuefree fragments and instantiate some temporary explosion looking particle effects
		// if all sides of the aabb of the fragment are less than some value (or maybe the volume of the aabb is less than some value)

		// add early return condition for if originalVSTRoot depth is 0?

		List<VSTNode> fragmentsAtGivenDepth = [];

		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, originalVSTRoot, explosionTreeDepth);

		if (explosionTreeDepth == originalVSTRoot.level)
		{
			GD.PushError("explosionTreeDepth = originalVSTRoot.level. this might lead to issues idk, i can't work it out lmao");
		}

		if (fragmentsAtGivenDepth.Count == 0)
		{
			if (DebugPrints)
			{
				GD.Print("fragmentsAtGivenDepth.Count is 0, returning early to avoid unnecessary computation");
			}
			
			return;
		}

		List<VSTNode> fragmentsToRemove = [];

		foreach (VSTNode vstNode in fragmentsAtGivenDepth)
		{
			// if the node is not an end point, its children says something about what it represents
			if (!vstNode.endPoint)
			{
				if (vstNode.left is null && vstNode.right is null)
				{
					GD.PushWarning("this shouldn't be possible, no references to unphysical nodes should be present after orphaning");
					continue;
				}

				if (vstNode.left is null || vstNode.right is null)
				{
					// this node has been split; no node of the required depth is available as it has already been split in 2
					continue;
				}
			}
			// redundant check, can just remove probs
			else if ( !(vstNode.left is null && vstNode.right is null) )
			{
				GD.PushWarning("all endpoints should have 2 null children, this one has at least 1 non null child. This is unexpected.");
			}
			
			// test for fragment centre's inclusion in explosion region
			if (!destronoiNode.IsInsideTree())
			{
				GD.PushError("destronoi node not in tree, returning early from splitexplode");
				return;
			}

			Vector3 globalLeafPosition = destronoiNode.GlobalTransform * vstNode.meshInstance.GetAabb().GetCenter();

			if ((globalLeafPosition - GlobalPosition).Length() < explosionDistanceMax)
			{
				fragmentsToRemove.Add(vstNode);
				Orphan(vstNode);
				TellParentThatChildChanged(vstNode);

				if (vstNode.childrenChanged)
				{
					GD.Print("expect a subsequent warning about combining meshes");
				}
			}
		}

		// if we are removing no fragments, then the rest of this code is just gonna create a duplicate of the current node
		// ie in this case, nothing would be changed. nothing about the vst either i believe (hence no need to clean the VST)
		// so we can save some computation and skip it. also prevents bodies moving about weirdly (duplicating them resets their position)
		if (fragmentsToRemove.Count == 0)
		{
			if (DebugPrints)
			{
				GD.Print("no fragments to remove");
			}
			return;
		}

		// --- create small fragments --- //

		int fragmentNumber = 0;

		foreach (VSTNode leaf in fragmentsToRemove)
		{
			string leafName = destronoiNode.Name + $"_fragment_{fragmentNumber}";

			MeshInstance3D meshToInstantate = new();

			if (leaf.childrenChanged)
			{
				GD.Print("created combined mesh for this fragment.");
				List<MeshInstance3D> meshInstances = [];
				GetDeepestMeshInstances(meshInstances, leaf);
				meshToInstantate = CombineMeshes(meshInstances);
			}
			else
			{
				meshToInstantate = leaf.meshInstance;
			}

			DestronoiNode newDestronoiNode = destronoiNode.CreateDestronoiNode(leaf,
																meshToInstantate,
																leafName,
																fragmentMaterial);
			
			destronoiNode.fragmentContainer.AddChild(newDestronoiNode);
			destronoiNode.binaryTreeMapToActiveNodes.AddToActiveTree(newDestronoiNode);


			if (ApplyImpulseOnSplit)
			{
				newDestronoiNode.ApplyCentralImpulse(new Vector3(
													GD.Randf()-0.5f,
													GD.Randf()-0.5f,
													GD.Randf()-0.5f
													).Normalized() * ImpulseStrength);
			}

			fragmentNumber++;
		}

		// if this node now has no children, all its mass has been removed and it doesn't represent anything physical anymore
		if (originalVSTRoot.left is null &&
			originalVSTRoot.right is null)
		{
			Deactivate(destronoiNode);
			return;
		}

		if (!originalVSTRoot.childrenChanged)
		{
			GD.PushWarning("i didnt expect this to be possible. if this vstroot has no changed children, then i would expect fragmentsToRemove.Count to be 0 above, and hence for the program to early return before this point.");
			GD.Print($"fragmentsToRemove = {fragmentsToRemove.Count}");
			return;
		}

		// --- create parent fragments --- //
		// update single body by redrawing originalVSTroot // this destronoinode, (given that now lots of the children are null)
		// (assumes originalVSTRoot.childrenChanged is true, and hence we combine the relevant meshes)
		
		if (DebugPrints)
		{
			GD.Print("Creating Combined DN");
		}

		List<VSTNode> vstNodes = [];
		GetDeepestVSTNodes(vstNodes, originalVSTRoot);

		if (vstNodes.Count == 0)
		{
			GD.PushError("this doesnt make sense, we have a non zero number of fragments to remove but then no valid vstNodes were found by GetDeepestVSTNodes");
			originalVSTRoot.DebugPrint();
			return;
		}

		List<List<VSTNode>> groupedVSTNodes = GetGroupedVSTNode(vstNodes);

		// could save 1 destronoinode creation
		// by having this node become groupedMeshes[0], orphan non adjacent vstNodes
		// and for the rest, we create a new destronoiNode with a copy of the vstNode, orphan non adjacent vstNodes
		// i.e. create n-1 new destronoi nodes rather than n

		GD.Print($"creating {groupedVSTNodes.Count} groups of nodes");

		foreach (List<VSTNode> vstNodeGroup in groupedVSTNodes)
		{
			// parent of root node is null, hence pass null in
			if (originalVSTRoot is null)
			{
				GD.PushWarning("hmm this is unexpected");
				return;
			}

			VSTNode newVSTRoot = originalVSTRoot.DeepCopy();
			newVSTRoot.parent = null;
			
			// now we can create a list of non adjacent nodes. HOWEVER this list of VSTNodes is of DISTINCT objects compared to the ones in newVSTRoot
			// as we just created a deepcopy of originalVSTRoot
			// hence when orphaning, we have to check against IDs (which are not allowed to change after initialisation), not object references themselves
			List<int> nonAdjacentNodeIDs = groupedVSTNodes
				.Where(innerList => !ReferenceEquals(innerList, vstNodeGroup))
				.SelectMany(innerList => innerList)    // flatten all nodes in the other groups
				.Select(vstNode => vstNode.ID)         // get their IDs
				.ToList();

			OrphanDeepestNonAdjacentNodesByID(newVSTRoot, nonAdjacentNodeIDs);

			string leafName = destronoiNode.Name + $"_fragment_{fragmentNumber}";

			List<MeshInstance3D> meshInstances = [];
			GetDeepestMeshInstances(meshInstances, newVSTRoot);
			MeshInstance3D overlappingCombinedMeshesToKeep = CombineMeshes(meshInstances);

			DestronoiNode newDestronoiNode = destronoiNode.CreateDestronoiNode(newVSTRoot,
																overlappingCombinedMeshesToKeep,
																leafName,
																fragmentMaterial);

			destronoiNode.fragmentContainer.AddChild(newDestronoiNode);
			destronoiNode.binaryTreeMapToActiveNodes.AddToActiveTree(newDestronoiNode);
		}

		Deactivate(destronoiNode);
	}

	// for now we dont queuefree as we need the original objects (i.e. the vst leafs) to stick around
	// would be fixed if we reused the old destronoiNode when creating new ones
	// rather than just creating new ones and deactivating the old one
	public static void Deactivate(DestronoiNode destronoiNode)
	{
		destronoiNode.Visible = false;
		destronoiNode.Freeze = true;
		destronoiNode.CollisionLayer = 0;
		destronoiNode.CollisionMask = 0;
		destronoiNode.Sleeping = true;

		// destronoiNode.GetParent()?.RemoveChild(destronoiNode);

		destronoiNode.binaryTreeMapToActiveNodes.RemoveFromActiveTree(destronoiNode);
	}

	/// <summary>
	/// orphan all vstnodes at the deepest depth of originalVSTRoot which are NOT part of vstNodeGroup
	/// </summary>
	public static void OrphanDeepestNonAdjacentNodesByID(VSTNode vstNode, List<int> nonAdjacentNodeIDs)
	{
		if (nonAdjacentNodeIDs.Contains(vstNode.ID))
		{
			Orphan(vstNode);
			TellParentThatChildChanged(vstNode);
		}
		// i feel like putting this bit in an else{} would be correct, but i think it might error sometimes? :/ idk why
		// it saves a shit ton of checks on deep trees if we else{} it
		// theoretically, if youve reached a non adjacent node to orphan, then I think it should be a node with no childrenChanged
		// and hence the destruction shouldnt need to check any deeper nodes
		// ideally, explosions would have a runtime independent of tree depth but that isnt the case rn still :/
		else
		{
			if (vstNode.left is not null)
			{
				OrphanDeepestNonAdjacentNodesByID(vstNode.left,nonAdjacentNodeIDs);
			}

			if (vstNode.right is not null)
			{
				OrphanDeepestNonAdjacentNodesByID(vstNode.right,nonAdjacentNodeIDs);
			}
		}
	}

	public static void GetDeepestMeshInstances(List<MeshInstance3D> meshInstances, VSTNode vstNode)
	{
		if (!vstNode.childrenChanged || vstNode.endPoint)
		{
			meshInstances.Add(vstNode.meshInstance);
		}

		if (vstNode.left is not null)
		{
			GetDeepestMeshInstances(meshInstances, vstNode.left);
		}
		if (vstNode.right is not null)
		{
			GetDeepestMeshInstances(meshInstances, vstNode.right);
		}
	}

	public static void GetDeepestVSTNodes(List<VSTNode> vstNodeList, VSTNode vstNode)
	{
		if (!vstNode.childrenChanged || vstNode.endPoint)
		{
			vstNodeList.Add(vstNode);
		}

		if (vstNode.left is not null)
		{
			GetDeepestVSTNodes(vstNodeList, vstNode.left);
		}
		if (vstNode.right is not null)
		{
			GetDeepestVSTNodes(vstNodeList, vstNode.right);
		}
	}

	/// <summary>must always be followed by TellParentsThatChildrenChanged(originally input vstNode)</summary>
	public static void Orphan(VSTNode vstNode)
	{
		// represents root node of body having no children and no parent
		// in this sitch we check later in main function and queuefree the destronoiNode
		if (vstNode.parent is null)
		{
			return;
		}

		// remove reference to this node from the parent's section of the tree
		if (vstNode.laterality == Laterality.LEFT)
		{
			vstNode.parent.left = null;
		}
		else
		{
			vstNode.parent.right = null;
		}

		if (vstNode.parent.left is null && vstNode.parent.right is null)
		{
			Orphan(vstNode.parent);
		}
	}

	public static void TellParentThatChildChanged(VSTNode vstNode)
	{
		if (vstNode.parent is not null)
		{
			vstNode.parent.childrenChanged = true;

			TellParentThatChildChanged(vstNode.parent);
		}
		
	}

	public static void InitialiseFragmentsAtGivenDepth(List<VSTNode> fragmentsAtGivenDepth,
												VSTNode currentVSTNode,
												int depth)
	{
		// represents a node not in the tree
		if (currentVSTNode is null)
		{
			return;
		}

		if (currentVSTNode.level == depth)
		{
			fragmentsAtGivenDepth.Add(currentVSTNode);
			return;
		}

		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, currentVSTNode.left, depth);
		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, currentVSTNode.right, depth);
	}

	// --- mesh combining schenanigans --- //

	// if mesh is adjacent to any node in group 1, append to group 1
	// repeat for all groups
	// if that group doesn't exist, create a new group and add the node to it
	// finds groups of VSTNodes who are adjacent
	public static List<List<VSTNode>> GetGroupedVSTNode(List<VSTNode> ungroupedVSTNodes)
	{
		if (ungroupedVSTNodes.Count == 0)
		{
			GD.PushWarning("ungroupedVSTNodes was passed to GetGroupedVSTNode with length 0. prolly wanna avoid that");
			return null;
		}

		List<VSTNode> group1 = [ungroupedVSTNodes[0]];

		List<List<VSTNode>> groups = [group1];

		// skip the first mesh, we already added it
		foreach (VSTNode vstNodeToCheck in ungroupedVSTNodes.Skip(1))
		{
			bool addedToGroup = false;

			foreach (List<VSTNode> group in groups)
			{
				foreach (VSTNode groupedVSTNode in group)
				{
					if (!IsAdjacent(vstNodeToCheck.meshInstance, groupedVSTNode.meshInstance))
					{
						continue;
					}

					group.Add(vstNodeToCheck);
					// skip to next meshToCheck;
					addedToGroup = true;
					break;
				}

				if (addedToGroup)
				{
					break;
				}
			}

			if (addedToGroup)
			{
				continue;
			}

			// if we're here, the meshToCheck is NOT adjacent to ANY mesh currently in a group
			// so we create a new group for it

			List<VSTNode> newGroup = [vstNodeToCheck];
			
			groups.Add(newGroup);
		}

		return groups;
	}

	// this is currently an ESTIMATOR for adjacency and NOT a true test. this function WILL be logically incorrect occasionally
	// (but probably in not a very noticeable way)
	// 2 meshes can be adjacent iff centre of mesh 2 - centre of mesh 1 <= (maxlength of mesh 2 / 2) + (max length of mesh 1 / 2)
	// i.e. we are checking a necessary but not sufficient condition
	// this test massively reduces the meshes we need to check, but may still group non adjacent meshes together
	public static bool IsAdjacent(MeshInstance3D mesh1, MeshInstance3D mesh2)
	{
		Aabb aabb1 = mesh1.GetAabb();
		Aabb aabb2 = mesh2.GetAabb();

		float distanceBetween = ( aabb2.GetCenter() - aabb1.GetCenter() ).Length();

		float maxLengthScale1 = Math.Max( Math.Max(aabb1.Size.X, aabb1.Size.Y), aabb1.Size.Z);

		float maxLengthScale2 = Math.Max( Math.Max(aabb2.Size.X, aabb2.Size.Y), aabb2.Size.Z);

		if (distanceBetween <= maxLengthScale1 / 2.0f + maxLengthScale2 / 2.0f)
		{
			return true;
		}

		return false;
	}

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
				surfaceTool.AppendFrom(mesh, s, meshInstance.Transform);
			}
		}

		var combinedArrayMesh = surfaceTool.Commit();

		return new MeshInstance3D
		{
			Mesh = combinedArrayMesh
		};
	}
}
