using Godot;
using System.Collections.Generic;
using System;

namespace CDestronoi;

// anytime you create or destroy a destronoi node
// find the (1) binarytreemap which represents the topmost ancestor of said created or destroyed destronoiNode
// and tell it "hey, i have this ID, i exist / have been queuefreed() now"
// so we are left with a map from any fragment to all instanced destronoiNodes which represent its children somehow
public partial class BinaryTreeMapToActiveNodes : Node
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
			node.left = BuildSubtree(node, Laterality.LEFT, inputID * 2, height - 1);
			node.right = BuildSubtree(node, Laterality.RIGHT, inputID * 2 + 1, height - 1);
		}

		return node;
	}

	// legit no clue why treeHeight is needed here
	public BinaryTreeMapToActiveNodes(int treeHeight, DestronoiNode rootDestronoiNode)
	{
		rootNode = BuildSubtree(null, Laterality.NONE, rootID, treeHeight);

		rootNode.activeNodesWhichRepresentThisLeafID = [rootDestronoiNode];
	}

	/// <summary>
	/// get the relevant representativeNode for this destronoiNode
	/// and then adds the input destronoiNode to the activeNodeList for that RN / ID
	/// </summary>
	/// <param name="destronoiNode"></param>
	public void AddToActiveTree(DestronoiNode destronoiNode)
	{
		RepresentativeNode representativeNode = IDToRepresentativeNodeMap[destronoiNode.vstRoot.ID];
		representativeNode.activeNodesWhichRepresentThisLeafID.Add(destronoiNode);
	}

	/// <summary>
	/// removes a destronoiNode from the activeNodeList for a representative node
	/// </summary>
	/// <param name="destronoiNode"></param>
	public void RemoveFromActiveTree(DestronoiNode destronoiNode)
	{
		RepresentativeNode representativeNode = IDToRepresentativeNodeMap[destronoiNode.vstRoot.ID];
		representativeNode.activeNodesWhichRepresentThisLeafID.Remove(destronoiNode);
	}

	public List<DestronoiNode> GetFragmentsInstantiatedChildren(int vstRootID)
	{
		RepresentativeNode representativeNode = IDToRepresentativeNodeMap[vstRootID];
		List<DestronoiNode> instantiatedChildren = [];

		RecursivelyAddActiveNodes(representativeNode, instantiatedChildren);

		return instantiatedChildren;
	}

	// maybe can stop this recursion on last node which doesnt have childrenChanged flag == true or smthn
	// tbh prolly makes like 0 difference to runtime
	public static void RecursivelyAddActiveNodes(RepresentativeNode representativeNode, List<DestronoiNode> instantiatedChildren)
	{
		instantiatedChildren.AddRange(representativeNode.activeNodesWhichRepresentThisLeafID);

		if (representativeNode.left is not null) { RecursivelyAddActiveNodes(representativeNode.left, instantiatedChildren); }
		if (representativeNode.right is not null) { RecursivelyAddActiveNodes(representativeNode.right, instantiatedChildren); }
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
	
	// these should be writeonce ideally
	public RepresentativeNode left;
	public RepresentativeNode right;

	// this should be always editable
	public List<DestronoiNode> activeNodesWhichRepresentThisLeafID = [];
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
