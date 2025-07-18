using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inversion;

namespace CDestronoi;

// for whatever reason, you might want to unfragment a node
// that is, find all the instantiated children of a node, queufree them
// and then reinstantiate that node (with its whole original mesh intact)
// this component simply takes an input destronoiNode and does exactly that
public partial class VSTUnsplittingComponent : Node
{
	[Export] public int unexplosionLevelsToGoUp = 2;
	[Export] public Node3D fragmentContainer;

	public Player player;

	public readonly float UnsplittingDuration = 1.5f;

	public override void _Ready()
	{
		base._Ready();
		
		LevelSwitcher levelSwitcher = GetNode<LevelSwitcher>("/root/Main/LevelSwitcher");

		player = levelSwitcher.player;
	}

	public async Task Activate(Transform3D? unfragmentationTransform, DestronoiNode fragmentToUnexplode)
	{
		// if (player.unfragmentationHighlighting.GetCollider() is not DestronoiNode fragment)
		// {
		// 	return;
		// }
		
		if (unfragmentationTransform is Transform3D nonNullUnfragmentationTransform)
		{
			await Unsplit(fragmentToUnexplode, nonNullUnfragmentationTransform);
		}
		else
		{
			Vector3 halfwayBetweenPlayerAndObject = (player.GlobalPosition + fragmentToUnexplode.GlobalPosition) / 2.0f;
			Transform3D transformBetweenPlayerAndObject = new(player.GlobalTransform.Basis, halfwayBetweenPlayerAndObject);
			await Unsplit(fragmentToUnexplode, transformBetweenPlayerAndObject);
		}
	}

	/// <param name="reversedExplosionCentre">in global coordinates</param>
	public async Task Unsplit(DestronoiNode destronoiNode, Transform3D reversedExplosionCentre)
	{
		VSTNode topmostParent;
		List<DestronoiNode> instantiatedChildren;

		// if there is no parent (i.e. this fragment itself is the topmost fragment), then just use the original vstNode
		if (destronoiNode.vstRoot.permanentParent is null)
		{
			topmostParent = destronoiNode.vstRoot;
		}
		else
		{
			topmostParent = GetParentAGivenNumberOfLevelsUp(destronoiNode.vstRoot.permanentParent, unexplosionLevelsToGoUp);
		}

		instantiatedChildren = destronoiNode.binaryTreeMapToActiveNodes.GetFragmentsInstantiatedChildren(topmostParent.ID);

		// now we need to interpolate all instantiated children towards the reverse explosion
		// and then queuefree them alls
		// and then instantiate the parent as a brand new, fresh, fragment with all its children and vstStuff reset

		// reversedExplosionCentre = new(reversedExplosionCentre.Basis, reversedExplosionCentre.Origin - topmostParent.meshInstance.GetAabb().GetCenter());

		await InterpolateDestronoiNodesThenDeactivate(instantiatedChildren, reversedExplosionCentre);

		topmostParent.Reset();

		// create fresh parent destronoiNode and add to scene
		DestronoiNode freshDestronoiNodeFragment = CreateFreshDestronoiNode(topmostParent, reversedExplosionCentre, destronoiNode);
		freshDestronoiNodeFragment.fragmentContainer.AddChild(freshDestronoiNodeFragment);
		freshDestronoiNodeFragment.binaryTreeMapToActiveNodes.AddToActiveTree(freshDestronoiNodeFragment);

		DebugPrintValidDepth(topmostParent);
	}

	public static void DebugPrintValidDepth(VSTNode vstNode)
	{
		if (vstNode.left is null && vstNode.right is null)
		{
			GD.Print($"max valid tree level is: {vstNode.level}");
		}
		else
		{
			if (vstNode.left is not null)
			{
				DebugPrintValidDepth(vstNode.left);
			}
			else if (vstNode.right is not null)
			{
				DebugPrintValidDepth(vstNode.right);
			}
		}
	}

	public static VSTNode GetParentAGivenNumberOfLevelsUp(VSTNode vstNode, int levelsToGoUp)
	{
		if (vstNode is null)
		{
			GD.PushError("a null vstNode was passed to GetTopMostParent");
			return null;
		}

		// actual logic begins
		// if no higher parent, or the node is at the height we want, return the current node
		if (vstNode.permanentParent is null || levelsToGoUp == 0)
		{
			return vstNode;
		}

		return GetParentAGivenNumberOfLevelsUp(vstNode.permanentParent,levelsToGoUp - 1);
	}

	public async Task InterpolateDestronoiNodesThenDeactivate(List<DestronoiNode> instantiatedChildren, Transform3D reversedExplosionCentre)
	{
		var completionSources = new List<TaskCompletionSource<bool>>();

		foreach (DestronoiNode destronoiNode in instantiatedChildren)
		{
			var taskCompletionSource = new TaskCompletionSource<bool>();
			completionSources.Add(taskCompletionSource);

			Tween tween = GetTree().CreateTween();

			tween.SetEase(Tween.EaseType.In);
			tween.SetTrans(Tween.TransitionType.Expo);

			if (!destronoiNode.IsInsideTree())
			{
				GD.PushError($"{destronoiNode.Name} not inside tree, returning early in unsplitting component");
				return;
			}
			
			tween.TweenProperty(destronoiNode, "global_transform", reversedExplosionCentre, UnsplittingDuration);
			tween.TweenCallback(Callable.From(() => VSTSplittingComponent.Deactivate(destronoiNode)));
			tween.TweenCallback(Callable.From(() => taskCompletionSource.SetResult(true)));
		}

		await Task.WhenAll(completionSources.Select(t => t.Task));
	}

	/// <summary>
	/// creates a Destronoi Node from the given meshInstance and vstnode
	/// </summary>
	/// <param name="anyChildDestronoiNode">
	/// used for density, binarytreemap reference, and fragment container reference
	/// i.e. stuff that any child would agree on, it doesnt have to be some specific parent or anything
	/// </param>
	public static DestronoiNode CreateFreshDestronoiNode(VSTNode vstNode, Transform3D globalTransform, DestronoiNode anyChildDestronoiNode)
	{
		string name = "unfragmented_with_ID_" + vstNode.ID.ToString();

		MeshInstance3D meshInstanceToSet = vstNode.meshInstance;
		meshInstanceToSet.Name = $"{name}_MeshInstance3D";

		DestronoiNode destronoiNode = new(
			inputName: name,
			inputGlobalTransform: globalTransform,
			inputMeshInstance: meshInstanceToSet,
			inputFragmentContainer: anyChildDestronoiNode.fragmentContainer,
			inputVSTRoot: vstNode,
			inputDensity: anyChildDestronoiNode.baseObjectDensity,
			inputNeedsInitialising: false,
			inputBinaryTreeMapToActiveNodes: anyChildDestronoiNode.binaryTreeMapToActiveNodes
		);

		return destronoiNode;
	}
}
