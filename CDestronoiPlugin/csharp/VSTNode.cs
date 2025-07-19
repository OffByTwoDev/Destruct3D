using Godot;
using System;
using System.Collections.Generic;

namespace CDestronoi;

// none laterality applies only to a root node
public enum Laterality
{
	NONE,
	LEFT,
	RIGHT
}

/// <summary>
/// A node in a Voronoi Subdivision Tree.
/// Despite the name, VSTNode does not inherit from Node and therefore cannot be used as a scene object.
/// </summary>
public class VSTNode
{
	/// <summary>this meshInstance represents the originally calculated mesh. it never changes. what changes is the DestronoiNode's meshInstance</summary>
	public readonly MeshInstance3D meshInstance;
	public List<Vector3> sites;
	public VSTNode left;
	public VSTNode right;
	public VSTNode parent;
	/// <summary>i believe the initial level is 0 (i.e. for the vstNode that represents the whole object)</summary>
	public readonly int level;
	/// <summary>whether this node is a left or right child of its parent</summary>
	public readonly Laterality laterality;

	/// <summary>just used for unfragmentation</summary>
	public readonly VSTNode permanentParent;

	// cant just be readonly as these are set in _ready() of a destronoinode
	public WriteOnce<VSTNode> PermanentLeft = new();
	public WriteOnce<VSTNode> PermanentRight = new();

	/// <summary>whether this fragment is the smallest initialised fragment for this body<br></br>
	/// not actually necessary, if logic is good then childrenChanged would always be false for endPoints anyways<br></br>
	/// so I believe this bool and any check upon it can be removed and replaced by checking childrenChanged<br></br></summary>
	public readonly bool endPoint;


	/// <summary>by convention, IDS start at 1 (it doesnt matter it probably doesn't change any behaviour)<br></br>
	/// they are used in BinaryTreeMapToActiveNodes (and also for making stuff readable when using DebugPrint<br></br>
	/// IDs can only be set on initialisation<br></br>
	/// this must stay readonly for BinaryTreeMapToActiveNodes to always link to the correct IDs</summary>
	public readonly int ID;


	/// <summary>
	/// when a node is fragmented / orphaned, we tell its parent &amp; its parent's parent etc that one of its children has changed
	/// i.e. that the meshInstance of those parents dont accurately reflect the union of their children anymore
	/// </summary>
	public bool childrenChanged = false;

	/// <summary>Initializes a VSTNode using mesh data, a depth level, and a laterality value.</summary>
	public VSTNode(MeshInstance3D inputMeshInstance,
					int inputID,
					VSTNode inputParent,
					int inputLevel,
					Laterality inputLaterality,
					bool inputEndPoint)
	{
		if (inputMeshInstance.Mesh is not ArrayMesh)
		{
			SurfaceTool surfaceTool = new();
			surfaceTool.CreateFrom(inputMeshInstance.Mesh, 0);
			ArrayMesh arrayMesh = surfaceTool.Commit();

			MeshInstance3D newMeshInstance = new()
			{
				Mesh = arrayMesh
			};

			meshInstance = newMeshInstance;
		}
		else
		{
			meshInstance = inputMeshInstance;
		}

		parent = inputParent;
		permanentParent = inputParent;
		
		level = inputLevel;
		laterality = inputLaterality;
		endPoint = inputEndPoint;
		ID = inputID;
	}

	/// <summary>Returns the override material at the given surface index, or null if out of range.</summary>
	public Material GetOverrideMaterial(int index = 0)
	{
		if (meshInstance.GetSurfaceOverrideMaterialCount() - 1 < index)
		{
			return null;
		}
		
		return meshInstance.GetSurfaceOverrideMaterial(index);
	}

	/// <summary>Returns the number of sites (should be 0 or 2).</summary>
	public int GetSiteCount()
	{
		return sites.Count;
	}

	/// <summary>
	/// Recursively populates outArr with VSTNodes at an optional given depth. Leave desiredlevel unspecified to get the deepest possible nodes
	/// </summary>
	public static List<VSTNode> GetLeafNodes(VSTNode root, int? desiredLevel = null, int currentLevel = 0, List<VSTNode> outArr = null)
	{
		if (currentLevel < 0 || desiredLevel < 0)
		{
			GD.PushError("cannot use a negative currentlevel, nor desired level, in GetLeftLeafNodes. returning null");
			return null;
		}

		if (root is null)
		{
			GD.PushError("GetLeftLeafNodes was passed a null vstNode root");
			return [];
		}

		if (outArr is null)
		{
			outArr = [];
		}


		bool isLeaf = root.left is null && root.right is null;
		bool atTargetLevel;

		if (desiredLevel is null)
		{
			atTargetLevel = false;
		}
		else
		{
			atTargetLevel = currentLevel == desiredLevel;
		}
		
		if (isLeaf || atTargetLevel)
		{
			outArr.Add(root);
			return outArr;
		}

		GetLeafNodes(root.left, desiredLevel, currentLevel + 1, outArr);
		GetLeafNodes(root.right, desiredLevel, currentLevel + 1, outArr);

		return outArr;
	}

	public override string ToString()
	{
		return $"VSTNode {meshInstance}";
	}

	public void DebugPrint(bool recursive = false)
	{
		GD.Print("ID: ", ID);
		GD.Print("left: ", left?.ID);
		GD.Print("right: ", right?.ID);
		GD.Print("parent: ", parent?.ID);
		GD.Print("level: ", level);
		GD.Print("laterality: ", laterality);
		GD.Print("---");

		if (recursive)
		{
			left?.DebugPrint(true);
			right?.DebugPrint(true);
		}
	}

	// meshInstances are shared between all deepcopies, this is fine I dont think it affects behaviour
	// the only things we need to deepcopy really are the VSTNodes themself and the references
	// as we will be nullifying some references in sibling nodes when splitting nodes in 2 etc
	public VSTNode DeepCopy()
	{
		if (this.meshInstance is null)
		{
			GD.PushError("hmm that aint valid dawg. deepcopy has been called on a vstnode whose meshInstance is null :/");
			return null;
		}

		if (!GodotObject.IsInstanceValid(this.meshInstance))
		{
			GD.PushError($"Cannot deep-copy VSTNode {ID}: meshInstance has been freed");
			return null;
		}

        VSTNode copy = new(
            this.meshInstance,
            this.ID,
            this.parent,
            this.level,
            this.laterality,
            this.endPoint
        )
        {
            childrenChanged = this.childrenChanged,
			
            left = this.left?.DeepCopy(),
            right = this.right?.DeepCopy(),
            PermanentLeft = this.PermanentLeft,
            PermanentRight = this.PermanentRight
        };

        return copy;
	}

	/// <summary>
	/// fully cleans a node and all its children
	/// to be as if that fragment has just been cleanly initialised
	/// </summary>
	public void Reset()
	{
		left = PermanentLeft;
		right = PermanentRight;
		parent = permanentParent;
		childrenChanged = false;

		left?.Reset();
		right?.Reset();
	}
}