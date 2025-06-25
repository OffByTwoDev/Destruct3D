using Godot;
using System.Collections.Generic;
using System.Linq;

// the area3d uses the collisionshape3d
// this script uses values from the meshinstance3d
// so make sure the collisionshape3d and meshinstance3d child of this area3d represent the same thing

// smaller mesh instance corresponds to the deeper explosion
public partial class VSTSplittingComponent : Area3D
{
	// the layer of the VST that the fragments will be removed from, where the vstroot is the 0th layer
	// [Export(PropertyHint.Range, "1,8")] int explosionDepth = 2;

	[Export] MeshInstance3D explosionMeshSmall;
	[Export] MeshInstance3D explosionMeshLarge;

	[Export] int explosionTreeDepthDeep = 2;
	[Export] int explosionTreeDepthShallow = 4;

	[Export] bool ApplyImpulseOnSplit = false;
	[Export] float ImpulseStrength = 10.0f;

	// these are to be set to be the radius of the sphere
	float explosionDistancesSmall;
	float explosionDistancesLarge;

	int framesUntilCloseExplosion = -10;

	[Export] MeshInstance3D point;

	[Export] bool DebugPrints = false;

	public override void _Ready()
	{
		base._Ready();
	
		if ((explosionTreeDepthDeep <= 0) || (explosionTreeDepthDeep <= 0))
		{
			GD.PrintErr("explosionDepths have to be strictly greater than 0");
			return;
		}

		if (explosionMeshSmall.Mesh is not SphereMesh sphereMeshSmall ||
			explosionMeshLarge.Mesh is not SphereMesh sphereMeshLarge)
		{
			GD.PrintErr("only spheremeshes are supported for VSTSplittingComponenets for now. i.e. only spherically symmetric explosions can split VSTNodes.");
			return;
		}

		explosionDistancesSmall = sphereMeshSmall.Radius;
		explosionDistancesLarge = sphereMeshLarge.Radius;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (!Input.IsActionJustPressed("splitting_explosion"))
		{
			return;
		}

		if (DebugPrints) { GD.Print("splitting (large scale)"); }

		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestronoiNode destronoiNode)
			{
				continue;
			}

			SplitExplode(destronoiNode, explosionDistancesLarge, explosionTreeDepthShallow, new StandardMaterial3D());
		}

		framesUntilCloseExplosion = 5;
	}

	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

		framesUntilCloseExplosion -= 1;

		if (framesUntilCloseExplosion == 0)
		{
			if (DebugPrints) { GD.Print("2ndary explosion"); }
			
			CloserExplosion();
		}
	}

	public void CloserExplosion()
	{
		StandardMaterial3D material = new()
		{
			AlbedoColor = new Color(GD.Randf(), GD.Randf(), GD.Randf())
		};

		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestronoiNode destronoiNode)
			{
				continue;
			}

			SplitExplode(destronoiNode, explosionDistancesSmall, explosionTreeDepthDeep, material);
		}
	}

	public void SplitExplode(DestronoiNode destronoiNode,
							float explosionDistanceMax,
							int explosionTreeDepth,
							StandardMaterial3D debugMaterial)
	{
		VSTNode originalVSTRoot = destronoiNode.vstRoot;

		
		// desired explosionTreeDepth is relative to a body's root node, hence we add the depth of the root node
		explosionTreeDepth += originalVSTRoot.level;

		// if (explosionTreeDepth > deepestNode)
		// {
		//		set explosionTreeDepth to deepestNode i.e. just remove the smallest thing we have
		//		OR create a particle effect for the fragments to remove
		// }

		List<VSTNode> fragmentsAtGivenDepth = [];

		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, originalVSTRoot, explosionTreeDepth);

		List<VSTNode> fragmentsToRemove = [];

		foreach (VSTNode vstNode in fragmentsAtGivenDepth)
		{
			// if the node is not an end point, its children says something about what it represents
			if (!vstNode.endPoint)
			{
				if (vstNode.left is null && vstNode.right is null)
				{
					GD.PushWarning("this shouldn't be possible, no references to unphysical nodes should be present after orphaning");
					continue;
				}

				if (vstNode.left is null || vstNode.right is null)
				{
					// this node has been split; no node of the required depth is available as it has already been split in 2
					continue;
				}
			}
			// redundant check
			else
			{
				if (! (vstNode.left is null && vstNode.right is null))
				{
					GD.PushWarning("all endpoints should have 2 null children, this one has at least 1 non null child. This is unexpected.");
				}
			}

			Vector3 globalLeafPosition = destronoiNode.GlobalTransform * vstNode.meshInstance.GetAabb().GetCenter();

			if ((globalLeafPosition - GlobalPosition).Length() < explosionDistanceMax)
			{
				fragmentsToRemove.Add(vstNode);
				Orphan(vstNode);
			}
		}

		// if we are removing no fragments, then the rest of this code is just gonna create a duplicate of the current node
		// ie in this case, nothing would be changed. nothing about the vst either i believe (hence no need to clean the VST)
		// so we can save some computation and skip it. also prevents bodies moving about weirdly (duplicating them resets their position)
		if (fragmentsToRemove.Count == 0)
		{
			if (DebugPrints) { GD.Print("no fragments to remove"); }
			return;
		}

		// create small fragments

		int fragmentNumber = 0;

		foreach (VSTNode leaf in fragmentsToRemove)
		{
			string leafName = destronoiNode.Name + $"_fragment_{fragmentNumber}";

			DestronoiNode body = destronoiNode.CreateDestronoiNode(leaf,
																leaf.meshInstance,
																leafName,
																debugMaterial);

			destronoiNode.fragmentContainer.AddChild(body);

			if (ApplyImpulseOnSplit)
			{
				body.ApplyCentralImpulse(new Vector3(1,1,1) * ImpulseStrength);
			}

			fragmentNumber++;
		}

		if (originalVSTRoot.parent is null &&
			originalVSTRoot.left is null &&
			originalVSTRoot.right is null)
		{
			destronoiNode.QueueFree();
			return;
		}

		// update single body by redrawing originalVSTroot // this destronoinode, (given that now lots of the children are null)
		if (DebugPrints) { GD.Print("Creating Combined DN"); }

		List<MeshInstance3D> meshInstances = [];

		InitialiseMeshInstances(meshInstances, originalVSTRoot);

		MeshInstance3D overlappingCombinedMeshesToKeep = CombineMeshes(meshInstances);
		CollisionShape3D collisionShape = new()
		{
			Name = "CollisionShape3D",
			Shape = overlappingCombinedMeshesToKeep.Mesh.CreateConvexShape(false, false)
		};

		foreach (Node child in destronoiNode.GetChildren())
		{
			child.Free();
		}

		destronoiNode.AddChild(overlappingCombinedMeshesToKeep);
		destronoiNode.AddChild(collisionShape);
	}

	public static void InitialiseMeshInstances(List<MeshInstance3D> meshInstances, VSTNode vstNode)
	{
		if (vstNode.endPoint == true)
		{
			meshInstances.Add(vstNode.meshInstance);
		}

		if (vstNode.left is not null)
		{
			InitialiseMeshInstances(meshInstances, vstNode.left);
		}
		if (vstNode.right is not null)
		{
			InitialiseMeshInstances(meshInstances, vstNode.right);
		}
	}

	public static void Orphan(VSTNode vstNode)
	{
		if (vstNode.parent is null)
		{
			// represents root node of body having no children and no parent
			// in this sitch we check later in main function and queuefree the destronoiNode
			return;
		}

		// remove reference to this node from the parent's section of the tree
		if (vstNode.laterality == Laterality.LEFT)
		{
			vstNode.parent.left = null;
		}
		else
		{
			vstNode.parent.right = null;
		}

		if (vstNode.parent.left is null && vstNode.parent.right is null)
		{
			Orphan(vstNode.parent);
		}
	}

	public static void InitialiseFragmentsAtGivenDepth(List<VSTNode> fragmentsAtGivenDepth,
												VSTNode currentVSTNode,
												int depth)
	{
		// represents a node not in the tree
		if (currentVSTNode is null)
		{
			return;
		}

		if (currentVSTNode.level == depth)
		{
			fragmentsAtGivenDepth.Add(currentVSTNode);
			return;
		}

		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, currentVSTNode.left, depth);
		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, currentVSTNode.right, depth);
	}

	// --- mesh combining schenanigans --- //

	public static MeshInstance3D CombineMeshes(List<MeshInstance3D> meshInstances)
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		foreach (var meshInstance in meshInstances)
		{
			var mesh = meshInstance.Mesh;
			if (mesh is null)
			{
				continue;
			}

			// Append all surfaces from this mesh with the MeshInstance's transform
			int surfaceCount = mesh.GetSurfaceCount();
			for (int s = 0; s < surfaceCount; s++)
			{
				// maybe this should be baseObject.GlobalPosition + meshInstance.Transform?
				// or maybe its fine as is idk...
				surfaceTool.AppendFrom(mesh, s, meshInstance.Transform);
			}
		}

		var combinedArrayMesh = surfaceTool.Commit();

		return new MeshInstance3D
		{
			Mesh = combinedArrayMesh
		};
	}

	public static MeshInstance3D RemoveDuplicateSurfaces(MeshInstance3D meshInstance3D)
	{
		if (meshInstance3D.Mesh is not ArrayMesh arrayMesh)
		{
			GD.PrintErr("mesh passed to RemoveDuplicateSurfaces must have an arraymesh mesh. returning early");
			return null;
		}

		HashSet<int> surfaceIndicesToRemove = [];

		for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
		{
			for (int j = 0; j < arrayMesh.GetSurfaceCount(); j++)
			{
				if (MeshUtils.AreMeshVerticesEqual(arrayMesh, i, j))
				{
					surfaceIndicesToRemove.Add(i);
				}
			}
		}

		ArrayMesh newArrayMesh = new();

		for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
		{
			if (surfaceIndicesToRemove.Contains(i))
			{
				continue;
			}

			var arrays = arrayMesh.SurfaceGetArrays(i);
			var primitive = arrayMesh.SurfaceGetPrimitiveType(i);

			newArrayMesh.AddSurfaceFromArrays(primitive, arrays);

		}

		MeshInstance3D newMesh = new()
		{
			Mesh = newArrayMesh
		};

		return newMesh;
	}
}
