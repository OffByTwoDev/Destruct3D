using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Destruct3D;

// the area3d uses the collisionshape3d
// this script uses values from the meshinstance3d
// so make sure the collisionshape3d and meshinstance3d child of this area3d represent the same thing

// smaller mesh instance corresponds to the deeper explosion
public partial class VSTSplittingComponent : Area3D
{
	[Export] private MeshInstance3D explosionMeshSmall;
	[Export] private MeshInstance3D explosionMeshLarge;

	// these are relative to the level of the fragment that is being split
	// e.g. a relative depth of 2 applied to a fragment of level 6 will produce fragments of level 8  
	[Export] private int relativeExplosionTreeDepthDeep = 2;
	[Export] private int relativeExplosionTreeDepthShallow = 2;

	[Export] private bool ApplyImpulseOnSplit = true;
	[Export] private float ImpulseStrength = 1.0f;

	// these are to be set to be the radius of the sphere
	private float explosionDistancesSmall;
	private float explosionDistancesLarge;

	[Export] private bool DebugPrints = false;
	/// <summary>if true, the secondary explosion has a randomly coloured material (random for each explosion, i.e. one colour per explosion not per fragment)</summary>
	[Export] private bool DebugMaterialsOnSecondaryExplosion = false;

	// material to set for fragments
	private StandardMaterial3D fragmentMaterial = new();

	[Export] public StandardMaterial3D debugMaterial;

	/// <summary>
	/// used by IsAdjacentEstimatorOverlap. determines how much each aabb grows before testing for intersection.
	/// </summary>
	private const float ADJACENCY_ESTIMATOR_ABSOLUTE_GROWTH = 0.05f;

	private bool suppressCDestronoiWarnings = false;

	[Export] private bool useParticleEffectsUponDisintegration = false;

	public override void _Ready()
	{
		base._Ready();
	
		if ((relativeExplosionTreeDepthDeep <= 0) || (relativeExplosionTreeDepthShallow <= 0))
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
		
		if (Input.IsActionJustPressed("splitting_explosion"))
		{
			_ = Activate();
		}
	}

	/// <summary>
	/// carries out 2 explosions on 2 different spatial scales, 1 physics frame apart
	/// </summary>
	/// <remarks>
	/// if you await this function, it will return a task which will complete AFTER all the fragments have been instantiated into the scene tree (i.e. this function awaits an extra PhysicsFrame after calling <c>CloserExplosion()</c>)
	/// </remarks>
	public async Task Activate()
	{
		if (DebugPrints)
		{
			GD.Print("primary explosion");
		}

		Explosion(explosionDistancesLarge, relativeExplosionTreeDepthShallow, new StandardMaterial3D());

		// await so there's enough time for fragments to be instantiated
		await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);

		if (DebugPrints)
		{
			GD.Print("secondary explosion");
		}

		Explosion(explosionDistancesSmall, relativeExplosionTreeDepthDeep, new StandardMaterial3D());
	}

	/// <summary>carry out the explosion that goes to a tree depth of explosionTreeDepth and covers a radius explosionDistance</summary>
	///<remarks> calls SplitExplode, which fragments a DestructibleBody3D into its relevant fragments</remarks>
	private void Explosion(float explosionDistance, int relativeExplosionTreeDepth, StandardMaterial3D material)
	{
		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestructibleBody3D destructibleBody3D)
			{
				continue;
			}

			if (!destructibleBody3D.IsInsideTree())
			{
				// this corresponds to a destructibleBody3D which was part of GetOverlappingBodies() but is now not in the scene tree? kinda weird lmao. i think it might be if you spam Activate(), then destructibleBody3Ds are still being de-parented (by Deactivate()) (and hence removed from the scene tree) during another call to Activate().
				if (!suppressCDestronoiWarnings)
				{
					GD.PushWarning("destructibleBody3D node not in tree, skipping splitexplode for this fragment.");
				}
				continue;
			}

			SplitExplode(destructibleBody3D, explosionDistance, relativeExplosionTreeDepth, material);
		}
	}

	// probably can change for some cleverer way that doesn't involve lists of nodes and LINQ but i tried for like 2 hours and it kept going wrong so I'm just gonna use this
	public static void GetDeepestVSTNodes(List<VSTNode> vstNodeList, VSTNode vstNode)
	{
		if (vstNode.endPoint)
		{
			vstNodeList.Add(vstNode);
			return;
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

	private async void Disintegrate(DestructibleBody3D destructibleBody3D)
	{
		if (useParticleEffectsUponDisintegration)
		{
			PackedScene scene = ResourceLoader.Load<PackedScene>(((Resource)destructibleBody3D.GetScript()).ResourcePath + destructibleBody3D.CUSTOM_PARTICLE_EFFECTS_SCENE_RELATIVE_PATH);
			Node instantiatedScene = scene.Instantiate();
			destructibleBody3D.fragmentContainer.AddChild(instantiatedScene);
			GpuParticles3D emitter = instantiatedScene.GetChild(0) as GpuParticles3D;
			emitter.OneShot = true;
			destructibleBody3D.meshInstance.Visible = false;
			destructibleBody3D.LinearDamp = 1000f;

			emitter.GlobalPosition = destructibleBody3D.GlobalPosition;

			destructibleBody3D.Deactivate();

			emitter.Restart();
			await Task.Delay(5_000);
			emitter.QueueFree();
		}
		else
		{
			destructibleBody3D.Deactivate();
		}
	}

	/// <summary>
	/// carries out an explosion on the DestructibleBody3D by removing & instantiating fragments from its VST
	/// </summary>
	public void SplitExplode(DestructibleBody3D destructibleBody3D,
							float explosionDistanceMax,
							int relativeExplosionTreeDepth,
							StandardMaterial3D fragmentMaterial)
	{
		VSTNode originalVSTRoot = destructibleBody3D.vstRoot;

		// desired explosionTreeDepth is relative to a body's root node, hence we add the depth of the root node
		int absoluteExplosionTreeDepth = relativeExplosionTreeDepth + originalVSTRoot.level;

		List<VSTNode> deepestDestructibleBody3Ds = [];
		GetDeepestVSTNodes(deepestDestructibleBody3Ds, originalVSTRoot);
		var deepestAccessibleLevel = deepestDestructibleBody3Ds.Max(vstNode => vstNode.level);

		if (absoluteExplosionTreeDepth > deepestAccessibleLevel)
		{
			// GD.Print($"absoluteExplosionTreeDepth = {absoluteExplosionTreeDepth}, deepestLevel = {deepestAccessibleLevel}");
			Disintegrate(destructibleBody3D);
			return;
		}

		// might also want to queuefree fragments and instantiate some temporary explosion looking particle effects if all sides of the aabb of the fragment are less than some value (or maybe the volume of the aabb is less than some value)
		// add early return condition for if originalVSTRoot depth is 0?

		// the node has to be in the tree in order to check for inclusion in the explosion areas
		if (!destructibleBody3D.IsInsideTree())
		{
			// this corresponds to a destructibleBody3D which was part of GetOvelappingBodies() but is now not in the scene tree? kinda weird lmao
			GD.PushWarning("[Destruct3D] destructibleBody3D node not in tree, returning early from splitexplode (this is the later IsInsideTree check)");
			return;
		}

		List<VSTNode> fragmentsToRemove = GetFragmentsToRemove(destructibleBody3D, originalVSTRoot, absoluteExplosionTreeDepth, explosionDistanceMax);

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
		CreateSmallFragments(fragmentsToRemove, destructibleBody3D);

		// if this node now has no children, all its mass has been removed and it doesn't represent anything physical anymore
		if (originalVSTRoot.left is null &&
			originalVSTRoot.right is null)
		{
			Orphan(destructibleBody3D.vstRoot);
			TellParentThatChildChanged(destructibleBody3D.vstRoot);

			destructibleBody3D.Deactivate();

			return;
		}

		if (!originalVSTRoot.childrenChanged)
		{
			GD.PushWarning($"I didnt expect this to be possible. if this vstroot has no changed children, then i would expect fragmentsToRemove.Count to be 0 above, and hence for the program to early return before this point.\n. I'm just gonna set children changed to true and hope it fixes it lmao... (it seems to for now at least)\n fragmentsToRemove = {fragmentsToRemove.Count}");

			originalVSTRoot.childrenChanged = true;
			// return;
		}

		// --- create parent fragments --- //
		// update single body by redrawing originalVSTroot // this destructibleBody3D, (given that now lots of the children are null)
		// (assumes originalVSTRoot.childrenChanged is true, and hence we combine the relevant meshes)
		CreateParentFragments(originalVSTRoot, destructibleBody3D);

		destructibleBody3D.Deactivate();
	}

	/// <summary>
	/// finds all VSTNodes of a specified depth within a given VST root Node, that are within explosionDistanceMax from this VSTSplittingComponent
	/// </summary>
	public List<VSTNode> GetFragmentsToRemove(DestructibleBody3D destructibleBody3D, VSTNode originalVSTRoot, int absoluteExplosionTreeDepth, float explosionDistanceMax)
	{
		List<VSTNode> fragmentsAtGivenDepth = [];

		InitialiseFragmentsAtGivenLevel(fragmentsAtGivenDepth, originalVSTRoot, absoluteExplosionTreeDepth);

		if (absoluteExplosionTreeDepth == originalVSTRoot.level)
		{
			GD.PushError("explosionTreeDepth = originalVSTRoot.level. this might lead to issues idk, i can't work it out lmao");
		}

		if (fragmentsAtGivenDepth.Count == 0)
		{
			if (DebugPrints)
			{
				GD.Print("fragmentsAtGivenDepth.Count is 0, returning early to avoid unnecessary computation");
			}
			
			return [];
		}

		
		List<VSTNode> fragmentsToRemove = [];

		foreach (VSTNode vstNode in fragmentsAtGivenDepth)
		{
			// if the node is not an end point, its children says something about what it represents
			if (vstNode.endPoint && !(vstNode.left is null && vstNode.right is null) )
			{
				GD.PushWarning("all endpoints should have 2 null children, this one has at least 1 non null child. This is unexpected.");
				// i think just continuing is the best option for error handling here, to avoid fucking up everything if just one node is broken
				continue;
			}
			else if (!vstNode.endPoint)
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
			
			// test for fragment centre's inclusion in explosion region
			Vector3 globalLeafPosition = destructibleBody3D.GlobalTransform * vstNode.meshInstance.GetAabb().GetCenter();

			if ((globalLeafPosition - GlobalPosition).Length() < explosionDistanceMax)
			{
				fragmentsToRemove.Add(vstNode);
			}
		}

		return fragmentsToRemove;
	}

	/// <summary>
	/// this is the main function which creates fragments. it creates destructibleBody3D nodes for each VSTNode in fragmentsToRemove.
	/// </summary>
	public void CreateSmallFragments(List<VSTNode> fragmentsToRemove, DestructibleBody3D destructibleBody3D)
	{
		int currentFragmentNumber = 0;

		foreach (VSTNode leaf in fragmentsToRemove)
		{
			Orphan(leaf);
			TellParentThatChildChanged(leaf);

			string leafName = destructibleBody3D.Name + $"_child_fragment_{currentFragmentNumber}";

			MeshInstance3D meshToInstantate = new();

			List<MeshInstance3D> meshInstances = [];
			if (leaf.childrenChanged)
			{
				GD.Print("created combined mesh for this fragment.");
				GetDeepestMeshInstances(meshInstances, leaf);
				
			}
			else
			{
				meshInstances = [leaf.meshInstance];
			}

			meshToInstantate = MeshPruning.CombineMeshesAndPrune(meshInstances, destructibleBody3D.hasTexturedMaterial, destructibleBody3D.materialRegistry, destructibleBody3D.fragmentMaterial, destructibleBody3D.TextureScale);

			DestructibleBody3D newDestructibleBody3D = destructibleBody3D.CreateDestructibleBody3D(leaf,
																meshToInstantate,
																leafName,
																fragmentMaterial);
			
			destructibleBody3D.fragmentContainer.AddChild(newDestructibleBody3D);
			destructibleBody3D.binaryTreeMapToActiveNodes.AddToActiveTree(newDestructibleBody3D);


			if (ApplyImpulseOnSplit)
			{
				// GD.Print("applying impulse on split");
				newDestructibleBody3D.ApplyCentralImpulse(new Vector3(
													GD.Randf()-0.5f,
													GD.Randf()-0.5f,
													GD.Randf()-0.5f
													).Normalized() * ImpulseStrength);
			}

			currentFragmentNumber++;
		}
	}

	/// <summary>
	/// recalculates the meshes of the originalVSTRoot (i.e. the thing that fragments have been removed from by CreateSmallFramgents())
	/// <para>
	/// fragmentation can (theoretically) split the original root DestructibleBody3D in arbitrarily many "parent sections". If we did not test for adjacency, then if an explosion e.g. splits a long bar in half, our code would still see both those sections as the same body. Hence we must group the originalVSTRoot into collections of fragments which are adjacent, and then instantiate each group as a new DestructibleBody3D.
	/// </para>
	/// </summary>
	public void CreateParentFragments(VSTNode originalVSTRoot, DestructibleBody3D destructibleBody3D)
	{
		if (DebugPrints)
		{
			GD.Print("Creating Combined DN");
		}

		List<VSTNode> vstNodes = [];
		GetDeepestUnchangedVSTNodes(vstNodes, originalVSTRoot);

		if (vstNodes.Count == 0)
		{
			GD.PushError("this doesnt make sense, we have a non zero number of fragments to remove but then no valid vstNodes were found by GetDeepestVSTNodes");
			originalVSTRoot.DebugPrint();
			return;
		}

		List<List<VSTNode>> groupedVSTNodes = GetSpatiallyGroupedVSTNodes(vstNodes);

		if (DebugPrints)
		{
			foreach (List<VSTNode> list in groupedVSTNodes)
			{
				GD.Print("--- list change ---");

				foreach(VSTNode node in list)
				{
					GD.Print($"{node.meshInstance.GetAabb().GetCenter()} with level {node.level}");
				}
			}
		}
		
		// could save 1 DestructibleBody3D creation
		// by having this node become groupedMeshes[0], orphan non adjacent vstNodes
		// and for the rest, we create a new DestructibleBody3D with a copy of the vstNode, orphan non adjacent vstNodes
		// i.e. create n-1 new destructibleBody3D nodes rather than n
		
		if (DebugPrints)
		{
			GD.Print($"creating {groupedVSTNodes.Count} groups of nodes");
		}

		int currentFragmentNumber = 0;

		if (DebugPrints) { GD.Print($"number of parent groups: {groupedVSTNodes.Count}"); }

		foreach (List<VSTNode> currentVSTNodeGroup in groupedVSTNodes)
		{

			if (DebugPrints) { GD.Print($"new group, number of nodes in group: {currentVSTNodeGroup.Count}"); }

			if (originalVSTRoot is null)
			{
				GD.PushWarning("hmm this is unexpected");
				return;
			}

			VSTNode newVSTRoot = originalVSTRoot.DeepCopy(newParent: null, originalVSTRoot.permanentParent);
			
			// now we can create a list of non adjacent nodes. HOWEVER this list of VSTNodes is of DISTINCT objects compared to the ones in newVSTRoot
			// as we just created a deepcopy of originalVSTRoot
			// hence when orphaning, we have to check against IDs (which are not allowed to change after initialisation), not object references themselves
			List<int> nonAdjacentNodeIDs = groupedVSTNodes
				.Where(vstNodeGroup => !ReferenceEquals(vstNodeGroup, currentVSTNodeGroup))
				.SelectMany(innerList => innerList)    // flatten all nodes in the other groups
				.Select(vstNode => vstNode.ID)         // get their IDs
				.ToList();

			OrphanDeepestNonAdjacentNodesByID(newVSTRoot, nonAdjacentNodeIDs);

			string leafName = destructibleBody3D.Name + $"_parent_fragment_{currentFragmentNumber}";

			List<MeshInstance3D> meshInstances = [];

			GetDeepestMeshInstances(meshInstances, newVSTRoot);

			MeshInstance3D overlappingCombinedMeshesToKeep = MeshPruning.CombineMeshesAndPrune(meshInstances, destructibleBody3D.hasTexturedMaterial, destructibleBody3D.materialRegistry, destructibleBody3D.fragmentMaterial, destructibleBody3D.TextureScale);
			
			overlappingCombinedMeshesToKeep.SetSurfaceOverrideMaterial(0, debugMaterial);

			DestructibleBody3D newDestructibleBody3D = destructibleBody3D.CreateDestructibleBody3D(newVSTRoot,
																overlappingCombinedMeshesToKeep,
																leafName,
																fragmentMaterial);

			destructibleBody3D.fragmentContainer.AddChild(newDestructibleBody3D);
			destructibleBody3D.binaryTreeMapToActiveNodes.AddToActiveTree(newDestructibleBody3D);

			currentFragmentNumber++;
		}
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
			return;
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

	public static void GetDeepestUnchangedVSTNodes(List<VSTNode> vstNodeList, VSTNode vstNode)
	{
		if (!vstNode.childrenChanged || vstNode.endPoint)
		{
			vstNodeList.Add(vstNode);
			return;
		}

		if (vstNode.left is not null)
		{
			GetDeepestUnchangedVSTNodes(vstNodeList, vstNode.left);
		}
		if (vstNode.right is not null)
		{
			GetDeepestUnchangedVSTNodes(vstNodeList, vstNode.right);
		}
	}

	/// <summary>must always be followed by TellParentsThatChildrenChanged(originally input vstNode)</summary>
	public static void Orphan(VSTNode vstNode)
	{
		// represents root node of body having no children and no parent
		// in this sitch we check later in main function and queuefree the DestructibleBody3D
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

	public static void InitialiseFragmentsAtGivenLevel(List<VSTNode> fragmentsAtGivenLevel,
												VSTNode currentVSTNode,
												int desiredLevel)
	{
		// represents a node not in the tree
		if (currentVSTNode is null)
		{
			return;
		}

		if (currentVSTNode.level == desiredLevel)
		{
			fragmentsAtGivenLevel.Add(currentVSTNode);
			return;
		}

		InitialiseFragmentsAtGivenLevel(fragmentsAtGivenLevel, currentVSTNode.left, desiredLevel);
		InitialiseFragmentsAtGivenLevel(fragmentsAtGivenLevel, currentVSTNode.right, desiredLevel);
	}

	// --- mesh combining schenanigans --- //

	// if mesh is adjacent to any node in group 1, append to group 1
	// repeat for all groups
	// if that group doesn't exist, create a new group and add the node to it
	// finds groups of VSTNodes who are adjacent
	public static List<List<VSTNode>> GetSpatiallyGroupedVSTNodes(List<VSTNode> ungroupedVSTNodes)
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
					if (!IsAdjacentEstimatorOverlap(vstNodeToCheck.meshInstance, groupedVSTNode.meshInstance))
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

	/// <summary>
	/// this is currently an estimator for adjacency and <i>not</i> a true test. this function will be logically incorrect occasionally
	/// (but probably in not a very noticeable way).<br></br><br></br>
	/// 2 meshes can be adjacent iff centre of mesh 2 - centre of mesh 1 &lt;= (maxlength of mesh 2 / 2) + (max length of mesh 1 / 2)
	/// i.e. we are checking a necessary but not sufficient condition.<br></br><br></br>
	/// this test massively reduces the meshes we need to check, but may still group non adjacent meshes together
	/// </summary>
	/// <returns>whether or not 2 meshinstances are touching</returns>
	public static bool IsAdjacentEstimatorAABB(MeshInstance3D mesh1, MeshInstance3D mesh2)
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

	/// <summary>
	/// same idea as IsAdjacentEstimatorAABB, but grows the aabbs by a small amount and tests for intersection
	/// </summary>
	/// <returns>whether or not 2 meshinstances are touching</returns>
	public static bool IsAdjacentEstimatorOverlap(MeshInstance3D mesh1, MeshInstance3D mesh2)
	{
		Aabb aabb1 = mesh1.GetAabb();
		Aabb aabb2 = mesh2.GetAabb();

		// Expand slightly to allow near-adjacency (tweak as needed)
		aabb1 = aabb1.Grow(ADJACENCY_ESTIMATOR_ABSOLUTE_GROWTH);
		aabb2 = aabb2.Grow(ADJACENCY_ESTIMATOR_ABSOLUTE_GROWTH);

		bool intersects = aabb1.Intersects(aabb2);

		return intersects;
	}
}






	// prune in 2 steps:
	// step 1:
	// raycast in both d/r from centre of any given face
	// if both raycasts intersect with one of the other faces in the mesh then that face is INSIDE
	// and can be removed
	// step 2:
	// group faces by normals being (approx) equal
	// in those groups remove vertices that are the same and reform the face
