using Godot;
using System;
using System.Collections.Generic;

// for whatever reason, you might want to unfragment a node
// that is, find all the instantiated children of a node, queufree them
// and then reinstantiate that node (with its whole original mesh intact)
// this component simply takes an input destronoiNode and does exactly that
public partial class VSTUnsplittingComponent : Node
{
	/// <param name="reversedExplosionCentre">in global coordinates</param>
	public void Unsplit(DestronoiNode destronoiNode, Transform3D reversedExplosionCentre)
	{
		// get the topmost parent from the permanentparent of the node to be unexploded (might be wrong idk lmao)
		VSTNode topmostParent = GetTopMostParent(destronoiNode.vstRoot.permanentParent);

		List<DestronoiNode> instantiatedChildren = destronoiNode.binaryTreeMapToActiveNodes.GetFragmentsInstantiatedChildren(topmostParent.ID);

		// now we need to interpolate all instantiated children towards the reverse explosion
		// and then queuefree them alls
		// and then instantiate the parent as a brand new, fresh, fragment with all its children and vstStuff reset

		InterpolateDestronoiNodesThenQueueFree(instantiatedChildren, reversedExplosionCentre);

		topmostParent.Reset();

		// create fresh parent destronoiNode and add to scene
		DestronoiNode freshDestronoiNodeFragment = CreateFreshDestronoiNode(topmostParent, reversedExplosionCentre, destronoiNode);
		freshDestronoiNodeFragment.fragmentContainer.AddChild(freshDestronoiNodeFragment);

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

	public void InterpolateDestronoiNodesThenQueueFree(List<DestronoiNode> instantiatedChildren, Transform3D reversedExplosionCentre)
	{
		foreach (DestronoiNode destronoiNode in instantiatedChildren)
		{
			Tween tween = GetTree().CreateTween();
			tween.TweenProperty(destronoiNode, "global_transform", reversedExplosionCentre, 1.0f);
			tween.TweenCallback(Callable.From(destronoiNode.QueueFree));
		}
	}

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
		string name = "tempname";
		StandardMaterial3D material = new();

		DestronoiNode destronoiNode = new()
		{
			Name = name,
			GlobalTransform = globalTransform
		};

		// --- rigidbody initialisation --- //

		// mesh instance
		MeshInstance3D meshInstance = vstNode.meshInstance;
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
		destronoiNode.binaryTreeMapToActiveNodes.Activate(destronoiNode);
		
		return destronoiNode;
	}
}
