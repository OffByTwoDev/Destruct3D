using Godot;
using System.Collections.Generic;
using System;

public enum Laterality
{
	NONE = 0,
	LEFT,
	RIGHT
}

/// <summary>
/// A node in a Voronoi Subdivision Tree.
/// Despite the name, VSTNode does not inherit from Node and therefore cannot be used as a scene object.
/// </summary>
/// <param name="laterality">whether this node is a left or right child of its parent</param>
public class VSTNode
{
	public MeshInstance3D meshInstance;
	public List<Vector3> sites;
	public VSTNode left;
	public VSTNode right;
	public VSTNode parent;
	// i believe the initial level is 0 (i.e. for the vstNode that represents the whole object)
	public int level;
	public Laterality laterality;

	// whether this fragment is the smallest initialised fragment for this body
	public bool endPoint;

	// IDS start at 1 (not 0, idk why, i actually believe it doesnt matter it probably doesn't change any behaviour)
	public int ID;
	public int ownerID;

	/// <summary>
	/// Initializes a VSTNode using mesh data, a depth level, and a laterality value.
	/// </summary>
	public VSTNode(MeshInstance3D inputMeshInstance,
					int inputID,
					int inputOwnerID,
					VSTNode inputParent,
					int lev,
					Laterality lat,
					bool inputEndPoint)
	{
		SurfaceTool surfaceTool = new();
		surfaceTool.CreateFrom(inputMeshInstance.Mesh, 0);
		ArrayMesh arrayMesh = surfaceTool.Commit();

        MeshInstance3D newMeshInstance = new()
        {
            Mesh = arrayMesh
        };


		parent = inputParent;

        meshInstance = newMeshInstance;
		level = lev;
		laterality = lat;

		endPoint = inputEndPoint;

		ID = inputID;
		ownerID = inputOwnerID;
	}

	/// <summary>Returns the override material at the given surface index, or null if out of range.</summary>
	public Material GetOverrideMaterial(int index = 0)
	{
		if (meshInstance.GetSurfaceOverrideMaterialCount() - 1 < index)
			return null;
		return meshInstance.GetSurfaceOverrideMaterial(index);
	}

	/// <summary>Returns the number of sites (should be 0 or 2).</summary>
	public int GetSiteCount()
	{
		return sites.Count;
	}

	/// <summary>
	/// Recursively populates outArr with each leaf VSTNode and returns the list of leaves.
	/// </summary>
	public static List<VSTNode> GetLeafNodes(VSTNode root = null, List<VSTNode> outArr = null)
	{
		if (outArr == null)
			outArr = [];
		if (root == null)
			return [];

		if (root.left == null && root.right == null)
		{
			outArr.Add(root);
			return outArr;
		}
		if (root.left != null)
            GetLeafNodes(root.left, outArr);
		if (root.right != null)
            GetLeafNodes(root.right, outArr);

		return outArr;
	}

	/// <summary>
	/// Recursively populates outArr with VSTNodes of right laterality at a certain depth.
	/// </summary>
	public static List<VSTNode> GetRightLeafNodes(VSTNode root = null, List<VSTNode> outArr = null, int lim = 1, int level = 0)
	{
		if (outArr == null)
			outArr = [];
		if (root == null)
			return [];

		if ((root.left == null && root.right == null) || level == lim)
		{
			outArr.Add(root);
			return outArr;
		}
		if (root.left != null && level > 0)
            GetRightLeafNodes(root.left, outArr, lim, level + 1);
		if (root.right != null)
            GetRightLeafNodes(root.right, outArr, lim, level + 1);

		return outArr;
	}

	/// <summary>
	/// Recursively populates outArr with VSTNodes of left laterality at a certain depth.
	/// </summary>
	public static List<VSTNode> GetLeftLeafNodes(VSTNode root = null, List<VSTNode> outArr = null, int lim = 1, int level = 0)
	{
		if (outArr == null)
			outArr = [];
		if (root == null)
			return [];

		if ((root.left == null && root.right == null) || level == lim)
		{
			outArr.Add(root);
			return outArr;
		}
		if (root.left != null)
            GetLeftLeafNodes(root.left, outArr, lim, level + 1);
		if (root.right != null && level > 0)
            GetLeftLeafNodes(root.right, outArr, lim, level + 1);

		return outArr;
	}

	public override string ToString()
	{
		return $"VSTNode {meshInstance}";
	}
}
