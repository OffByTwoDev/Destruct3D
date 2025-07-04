using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

// for whatever reason, you might want to unfragment a node
// that is, find all the instantiated children of a node, queufree them
// and then reinstantiate that node (with its whole original mesh intact)
// this component simply takes an input destronoiNode and does exactly that
public partial class VSTUnsplittingComponent : Node
{
	public RayCast3D unfragmentationRayCast;

	public Player player;

	public override void _Ready()
	{
		base._Ready();
		
		LevelSwitcher levelSwitcher = GetNode<LevelSwitcher>("/root/Main/LevelSwitcher");

		player = levelSwitcher.player;

		unfragmentationRayCast = player.unfragmentationHighlighting;
	}

	public void Activate()
	{
		if (unfragmentationRayCast.GetCollider() is DestronoiNode fragment)
		{
			Vector3 halfwayBetweenPlayerAndObject = (player.GlobalPosition + fragment.GlobalPosition) / 2.0f;

			Transform3D transform3D = new(player.GlobalTransform.Basis, halfwayBetweenPlayerAndObject);

			Unsplit(fragment, transform3D);
		}
	}
	/// <param name="reversedExplosionCentre">in global coordinates</param>
	public async void Unsplit(DestronoiNode destronoiNode, Transform3D reversedExplosionCentre)
	{
		VSTNode topmostParent;
		List<DestronoiNode> instantiatedChildren;

		// if there are no parent (i.e. this fragment itself is the topmost fragment), then just use the original vstNode
		if (destronoiNode.vstRoot.permanentParent is null)
		{
			topmostParent = destronoiNode.vstRoot;
			instantiatedChildren = destronoiNode.binaryTreeMapToActiveNodes.GetFragmentsInstantiatedChildren(topmostParent.ID);
		}
		else
		{
			topmostParent = VSTUnsplittingComponent.GetTopMostParent(destronoiNode.vstRoot.permanentParent);
			instantiatedChildren = destronoiNode.binaryTreeMapToActiveNodes.GetFragmentsInstantiatedChildren(topmostParent.ID);
		}

		// now we need to interpolate all instantiated children towards the reverse explosion
		// and then queuefree them alls
		// and then instantiate the parent as a brand new, fresh, fragment with all its children and vstStuff reset

		await InterpolateDestronoiNodesThenQueueFree(instantiatedChildren, reversedExplosionCentre);
		
		topmostParent.Reset();

		// create fresh parent destronoiNode and add to scene
		DestronoiNode freshDestronoiNodeFragment = CreateFreshDestronoiNode(topmostParent, reversedExplosionCentre, destronoiNode);
		freshDestronoiNodeFragment.fragmentContainer.AddChild(freshDestronoiNodeFragment);

		// foreach (DestronoiNode child in instantiatedChildren)
		// {
		// 	child.QueueFree();
		// }
	}

	public static VSTNode GetTopMostParent(VSTNode vstNode)
	{
		// just a null check
		if (vstNode is null)
		{
			GD.PushError("a null vstNode was passed to GetTopMostParent");
			return null;
		}

		// actual logic begins
		if (vstNode.parent is null) { return vstNode; }

		return GetTopMostParent(vstNode.parent);
	}

	public async Task InterpolateDestronoiNodesThenQueueFree(List<DestronoiNode> instantiatedChildren, Transform3D reversedExplosionCentre)
	{
		var completionSources = new List<TaskCompletionSource<bool>>();

		foreach (DestronoiNode destronoiNode in instantiatedChildren)
		{
			var tcs = new TaskCompletionSource<bool>();
			completionSources.Add(tcs);

			Tween tween = GetTree().CreateTween();
			VSTSplittingComponent.Deactivate(destronoiNode);
			destronoiNode.Visible = true;

			tween.TweenProperty(destronoiNode, "global_transform", reversedExplosionCentre, 1.0f);
			tween.TweenCallback(Callable.From(() => destronoiNode.Visible = false));
			// tween.TweenCallback(Callable.From(destronoiNode.QueueFree));
			tween.TweenCallback(Callable.From(() => tcs.SetResult(true)));
		}

		// Wait for all interpolations to complete
		await Task.WhenAll(completionSources.Select(t => t.Task));
	}

	// public void InterpolateDestronoiNodesThenQueueFree(List<DestronoiNode> instantiatedChildren,
	// 												Transform3D reversedExplosionCentre)
	// {
	// 	foreach (DestronoiNode destronoiNode in instantiatedChildren)
	// 	{
	// 		Tween tween = GetTree().CreateTween();

	// 		// disable everything about the destronoiNode but keep it visible
	// 		VSTSplittingComponent.Deactivate(destronoiNode);
	// 		destronoiNode.Visible = true;

	// 		tween.TweenProperty(destronoiNode, "global_transform", reversedExplosionCentre, 1.0f);

	// 		tween.TweenCallback(Callable.From(() => destronoiNode.Visible = false));
	// 	}
	// }

	/// <summary>
	/// creates a Destronoi Node from the given meshInstance and vstnode
	/// </summary>
	/// <param name="anyChildDestronoiNode">
	/// used for density, binarytreemap reference, and fragment container reference
	/// i.e. stuff that any child would agree on, it doesnt have to be some specific parent or anything
	/// </param>
	public DestronoiNode CreateFreshDestronoiNode(VSTNode vstNode, Transform3D globalTransform, DestronoiNode anyChildDestronoiNode)
	{
		// temporary shit
		string name = "unfragmented_with_ID_" + vstNode.ID.ToString();
		StandardMaterial3D material = new();

		DestronoiNode destronoiNode = new()
		{
			Name = name,
			GlobalTransform = globalTransform
		};

		// --- rigidbody initialisation --- //

		// mesh instance
		MeshInstance3D meshInstance = (MeshInstance3D)vstNode.meshInstance.Duplicate();
		meshInstance.Name = $"{name}_MeshInstance3D";

		meshInstance.SetSurfaceOverrideMaterial(0, material);

		destronoiNode.AddChild(meshInstance);

		// collisionshape
		var shape = new CollisionShape3D
		{
			Name = "CollisionShape3D",
			Shape = meshInstance.Mesh.CreateConvexShape(false, false)
		};
		destronoiNode.AddChild(shape);

		// mass
		float volume =  meshInstance.Mesh.GetAabb().Size.X *
						meshInstance.Mesh.GetAabb().Size.Y *
						meshInstance.Mesh.GetAabb().Size.Z;
		destronoiNode.Mass = Math.Max(anyChildDestronoiNode.baseObjectDensity * volume, 0.01f);

		// needed (idk why lmao ?) for detecting explosions from RPGs
		destronoiNode.ContactMonitor = true;
		destronoiNode.MaxContactsReported = 5_000;


		// --- destronoi node initialisation --- //

		destronoiNode.meshInstance = vstNode.meshInstance;
		destronoiNode.fragmentContainer = anyChildDestronoiNode.fragmentContainer;
		destronoiNode.vstRoot = vstNode;
		destronoiNode.baseObjectDensity = anyChildDestronoiNode.baseObjectDensity;
		// setting this to true will break everything,
		// this flag must be false as the vstRoot is being reused and must not be regenerated for fragments
		destronoiNode.needsInitialising = false;

		// finally, tell the relevant binarytreemap that this node has been created //
		// and also set the relevant binaryTreeMap to be this one
		destronoiNode.binaryTreeMapToActiveNodes = anyChildDestronoiNode.binaryTreeMapToActiveNodes;
		destronoiNode.binaryTreeMapToActiveNodes.AddToActiveTree(destronoiNode);
		
		return destronoiNode;
	}
}
