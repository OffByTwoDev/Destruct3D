using Godot;
using System.Collections.Generic;
using System;

namespace Fractonoi;

// anytime you create or destroy a destronoi node
// find the (1) binarytreemap which represents the topmost ancestor of said created or destroyed destronoiNode
// and tell it "hey, i have this ID, i exist / have been queuefreed() now"
// so we are left with a map from any fragment to all instanced destronoiNodes which represent its children somehow
// and destronoiNodes present at startup will create one of these maps for their VSTs and pass a reference to this map to all destronoiNode children that VST creates
public class BinaryTreeMapToActiveNodes
{
	public RepresentativeNode rootNode;
	public int rootID = 1;

	public Dictionary<int, RepresentativeNode> IDToRepresentativeNodeMap = [];

	private RepresentativeNode BuildSubtree(RepresentativeNode parent,
													Laterality laterality,
													int inputID,
													int height)
	{
		RepresentativeNode node = new(parent, laterality, inputID);
		IDToRepresentativeNodeMap.Add(inputID, node);

		if (height > 0)
		{
			node.left.Value = BuildSubtree(node, Laterality.LEFT, inputID * 2, height - 1);
			node.right.Value = BuildSubtree(node, Laterality.RIGHT, inputID * 2 + 1, height - 1);
		}

		return node;
	}

	public BinaryTreeMapToActiveNodes(int treeHeight, DestructibleBody3D rootDestronoiNode)
	{
		rootNode = BuildSubtree(null, Laterality.NONE, rootID, treeHeight);

		rootNode.activeNodesWhichRepresentThisLeafID = [rootDestronoiNode];
	}

	/// <summary>
	/// get the relevant representativeNode for this destronoiNode
	/// and then adds the input destronoiNode to the activeNodeList for that RN / ID
	/// </summary>
	public void AddToActiveTree(DestructibleBody3D destronoiNode)
	{
		RepresentativeNode representativeNode = IDToRepresentativeNodeMap[destronoiNode.vstRoot.ID];
		representativeNode.activeNodesWhichRepresentThisLeafID.Add(destronoiNode);

		if (!destronoiNode.IsInsideTree())
		{
			GD.PushError("a destronoi Node which is not inside a scene tree was added to the BinaryTreeMapToActiveNodes. this will cause errors in unsplitting & splitting components (probably)");
		}
	}

	/// <summary>
	/// removes a destronoiNode from the activeNodeList for a representative node
	/// </summary>
	public void RemoveFromActiveTree(DestructibleBody3D destronoiNode)
	{
		RepresentativeNode representativeNode = IDToRepresentativeNodeMap[destronoiNode.vstRoot.ID];
		representativeNode.activeNodesWhichRepresentThisLeafID.Remove(destronoiNode);
	}

	/// <summary>
	/// includes the node which is passed into the function initially
	/// </summary>
	public List<DestructibleBody3D> GetFragmentsInstantiatedChildren(int vstRootID)
	{
		RepresentativeNode representativeNode = IDToRepresentativeNodeMap[vstRootID];

		List<DestructibleBody3D> instantiatedChildren = [];

		if (representativeNode is not null)
		{
			RecursivelyAddActiveNodes(representativeNode, instantiatedChildren);
		}

		return instantiatedChildren;
	}

	// maybe can stop this recursion on last node which doesnt have childrenChanged flag == true or smthn
	// tbh prolly makes like 0 difference to runtime
	public static void RecursivelyAddActiveNodes(RepresentativeNode representativeNode, List<DestructibleBody3D> instantiatedChildren)
	{
		if (representativeNode.activeNodesWhichRepresentThisLeafID is not null)
		{
			instantiatedChildren.AddRange(representativeNode.activeNodesWhichRepresentThisLeafID);
		}

		if (representativeNode.left?.Value is not null)
		{
			RecursivelyAddActiveNodes(representativeNode.left.Value, instantiatedChildren);
		}
		if (representativeNode.right?.Value is not null)
		{
			RecursivelyAddActiveNodes(representativeNode.right.Value, instantiatedChildren);
		}
	}
}

// a simplified version of a vstNode
public class RepresentativeNode(RepresentativeNode inputParent,
							Laterality inputLaterality,
							int inputID)
{
	public readonly RepresentativeNode parent = inputParent;
	public readonly Laterality laterality = inputLaterality;
	public readonly int ID = inputID;

	public WriteOnce<RepresentativeNode> left = new();
	public WriteOnce<RepresentativeNode> right = new();

	// this should be always editable
	public List<DestructibleBody3D> activeNodesWhichRepresentThisLeafID = [];
}





















// public sealed class WriteOnce<T>
// {
// 	private T value;
// 	private bool hasValue;

// 	public override string ToString()
// 	{
// 		return hasValue ? Convert.ToString(value) : "";
// 	}
	
// 	public T Value
// 	{
// 		get
// 		{
// 			if (!hasValue) throw new InvalidOperationException("Value not set");
// 			return value;
// 		}
// 		set
// 		{
// 			if (hasValue) throw new InvalidOperationException("Value already set");
// 			this.value = value;
// 			this.hasValue = true;
// 		}
// 	}
