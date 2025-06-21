using Godot;
using System.Collections.Generic;

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
public class VSTNode
{
	public MeshInstance3D meshInstance;
	public List<Vector3> sites;
	public VSTNode left;
	public VSTNode right;
	public int level;
	public Laterality laterality;

	/// <summary>
	/// Initializes a VSTNode using mesh data, a depth level, and a laterality value.
	/// </summary>
	public VSTNode(MeshInstance3D inputMeshInstance, int lev = 0, Laterality lat = Laterality.NONE)
	{
		SurfaceTool surfaceTool = new();
		surfaceTool.CreateFrom(inputMeshInstance.Mesh, 0);
		ArrayMesh arrayMesh = surfaceTool.Commit();

		MeshInstance3D newMeshInstance = new();
		newMeshInstance.Mesh = arrayMesh;


		meshInstance = newMeshInstance;
		level = lev;
		laterality = lat;
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
	public List<VSTNode> GetLeafNodes(VSTNode root = null, List<VSTNode> outArr = null)
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
	public List<VSTNode> GetRightLeafNodes(VSTNode root = null, List<VSTNode> outArr = null, int lim = 1, int level = 0)
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
	public List<VSTNode> GetLeftLeafNodes(VSTNode root = null, List<VSTNode> outArr = null, int lim = 1, int level = 0)
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
