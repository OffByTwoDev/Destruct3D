using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Fractonoi;

/// <summary>
/// used for reverse fragmentation (i.e. combining a group of destronoiNodes back into one of their parent nodes)
/// </summary>
/// <remarks>
/// for whatever reason, you might want to unfragment a node.
/// That is, find all the instantiated children of a node, queufree them
/// and then reinstantiate that node (with its whole original mesh intact).
/// This component takes an input destronoiNode and does exactly that
/// </remarks>
public partial class VSTUnsplittingComponent : Node
{
	[Export] public int unexplosionLevelsToGoUp = 2;
	[Export] public Node3D fragmentContainer;

	public readonly float UnsplittingDurationSeconds = 1.5f;

	/// <summary>
	/// calls unsplit on a given DestronoiNode fragment and a specified final transform
	/// </summary>
	public async Task Activate(Transform3D? unfragmentationTransform, DestructibleBody3D fragmentToUnexplode)
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
			GD.PushWarning("[CDestronoi] unfragmentationTransform is null in VSTUnsplittingComponent; will instantiate parent fragment at the origin");
			Vector3 defaultGlobalPosition = Vector3.Zero;
			Transform3D defaultTransform = new(Basis.Identity, defaultGlobalPosition);
			await Unsplit(fragmentToUnexplode, defaultTransform);
		}
	}
	/// <summary>
	/// interpolates all children of a parent node to <paramref name="destronoiNode"/> towards reversedExplosionCentre
	/// </summary>
	/// <remarks>
	/// returns a Task which completes once all nodes have been interpolated, and the new combined fragment added to the scene and activated safely. The "parent node" is specified as being unexplosionLevelsToGoUp levels above the input destronoiNode
	/// </remarks>
	/// <param name="reversedExplosionCentre">in global coordinates</param>
	public async Task Unsplit(DestructibleBody3D destronoiNode, Transform3D reversedExplosionCentre)
	{
		VSTNode topmostParent;
		List<DestructibleBody3D> instantiatedChildren;

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
		DestructibleBody3D freshDestronoiNodeFragment = CreateFreshDestronoiNode(topmostParent, reversedExplosionCentre, destronoiNode);
		freshDestronoiNodeFragment.fragmentContainer.AddChild(freshDestronoiNodeFragment);
		freshDestronoiNodeFragment.binaryTreeMapToActiveNodes.AddToActiveTree(freshDestronoiNodeFragment);

		// DebugPrintValidDepth(topmostParent);
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

	/// <summary>
	/// takes a list of destronoinodes and interpolates their position and rotation towards reversedExplosionCentre
	/// </summary>
	/// <returns>a Task which completes when all destronoiNodes have finished their interpolation</returns>
	public async Task InterpolateDestronoiNodesThenDeactivate(List<DestructibleBody3D> instantiatedChildren, Transform3D reversedExplosionCentre)
	{
		var completionSources = new List<TaskCompletionSource<bool>>();

		foreach (DestructibleBody3D destronoiNode in instantiatedChildren)
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
			
			tween.TweenProperty(destronoiNode, "global_transform", reversedExplosionCentre, UnsplittingDurationSeconds);
			tween.TweenCallback(Callable.From(() => destronoiNode.Deactivate()));
			tween.TweenCallback(Callable.From(() => taskCompletionSource.SetResult(true)));
		}

		await Task.WhenAll(completionSources.Select(t => t.Task));
	}

	/// <summary>
	/// creates a Destronoi Node from the given meshInstance and vstnode
	/// </summary>
	/// <param name="anyChildDestronoiNode">
	/// used for density, binarytreemap reference, and fragment container reference<br></br>
	/// i.e. stuff that any child would agree on; it doesnt have to be some specific parent or anything
	/// </param>
	public static DestructibleBody3D CreateFreshDestronoiNode(VSTNode vstNode, Transform3D globalTransform, DestructibleBody3D anyChildDestronoiNode)
	{
		string name = "unfragmented_with_ID_" + vstNode.ID.ToString();

		MeshInstance3D meshInstanceToSet = vstNode.meshInstance;
		meshInstanceToSet.Name = $"{name}_MeshInstance3D";

		meshInstanceToSet = MeshPruning.CombineMeshesAndPrune([meshInstanceToSet], anyChildDestronoiNode.hasTexturedMaterial, anyChildDestronoiNode.materialRegistry, anyChildDestronoiNode.fragmentMaterial, anyChildDestronoiNode.TextureScale);

		DestructibleBody3D destronoiNode = new(
			inputName: name,
			inputGlobalTransform: globalTransform,
			inputMeshInstance: meshInstanceToSet,
			inputFragmentContainer: anyChildDestronoiNode.fragmentContainer,
			inputVSTRoot: vstNode,
			inputDensity: anyChildDestronoiNode.baseObjectDensity,
			inputNeedsInitialising: false,
			inputBinaryTreeMapToActiveNodes: anyChildDestronoiNode.binaryTreeMapToActiveNodes,
			inputShaderMaterial: anyChildDestronoiNode.shaderMaterial,
			inputOriginalUntexturedMaterial: anyChildDestronoiNode.originalUntexturedMaterial,
			inputHasTexturedMaterial: anyChildDestronoiNode.hasTexturedMaterial,
			inputTreatTopMostLevelAsStatic: anyChildDestronoiNode.treatTopMostLevelAsStatic
		);

		return destronoiNode;
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
}



