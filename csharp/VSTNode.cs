using Godot;
using Godot.Collections;
using System.Collections.Generic;

/// <summary>
/// A node in a Voronoi Subdivision Tree.
/// Despite the name, VSTNode does not inherit from Node and therefore cannot be used as a scene object.
/// </summary>
public class VSTNode
{
	/// <summary>Enum to define laterality.</summary>
	public enum Laterality
	{
		NONE = 0,
		LEFT,
		RIGHT
	}

	public MeshInstance3D _meshInstance;
	public Vector3[] _sites;
	private VSTNode _left;
	private VSTNode _right;
	private int _level;
	private Laterality _laterality;

	/// <summary>
	/// Initializes a VSTNode using mesh data, a depth level, and a laterality value.
	/// </summary>
	public VSTNode(MeshInstance3D meshInstance, int level = 0, Laterality lat = Laterality.NONE)
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.CreateFrom(meshInstance.Mesh, 0);
		var arrayMesh = surfaceTool.Commit() as ArrayMesh;

		var newMeshInstance = new MeshInstance3D
		{
			Mesh = arrayMesh
		};

		_meshInstance = newMeshInstance;
		_level = level;
		_laterality = lat;

		_sites = [];
	}

	/// <summary>Returns the override material at the given surface index, or null if out of range.</summary>
	public Material GetOverrideMaterial(int index = 0)
	{
		if (_meshInstance.GetSurfaceOverrideMaterialCount() - 1 < index)
			return null;
		return _meshInstance.GetSurfaceOverrideMaterial(index);
	}

	/// <summary>Returns the number of sites (should be 0 or 2).</summary>
	public int GetSiteCount()
	{
		return _sites.Length;
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

		if (root._left == null && root._right == null)
		{
			outArr.Add(root);
			return outArr;
		}
		if (root._left != null)
			GetLeafNodes(root._left, outArr);
		if (root._right != null)
			GetLeafNodes(root._right, outArr);

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

		if ((root._left == null && root._right == null) || level == lim)
		{
			outArr.Add(root);
			return outArr;
		}
		if (root._left != null && level > 0)
			GetRightLeafNodes(root._left, outArr, lim, level + 1);
		if (root._right != null)
			GetRightLeafNodes(root._right, outArr, lim, level + 1);

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

		if ((root._left == null && root._right == null) || level == lim)
		{
			outArr.Add(root);
			return outArr;
		}
		if (root._left != null)
			GetLeftLeafNodes(root._left, outArr, lim, level + 1);
		if (root._right != null && level > 0)
			GetLeftLeafNodes(root._right, outArr, lim, level + 1);

		return outArr;
	}

	public override string ToString()
	{
		return $"VSTNode {_meshInstance}";
	}
}