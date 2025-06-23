using Godot;
using System.Collections.Generic;
using System.Linq;

// the area3d uses the collisionshape3d
// this script uses values from the meshinstance3d
// so make sure the collisionshape3d and meshinstance3d child of this area3d represent the same thing

public partial class VSTSplittingComponent : Area3D
{
	// the layer of the VST that the fragments will be removed from, where the vstroot is the 0th layer
	[Export(PropertyHint.Range, "1,8")] int explosionDepth = 2;

	[Export] MeshInstance3D meshInstance3D;

	// this is set to be the radius of the sphere for now
	float explosionDistance = 1;

	public override void _Ready()
	{
		base._Ready();
		
		if (!(0 < explosionDepth))
		{
			GD.PrintErr("explosionDepth has to be greater than 0");
			return;
		}

		if (meshInstance3D.Mesh is not SphereMesh sphereMesh)
		{
			GD.PrintErr("only spheremeshes are supported for VSTSplittingComponenets for now. i.e. only spherically symmetric explosions can split VSTNodes.");
			return;
		}

		explosionDistance = sphereMesh.Radius;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (Input.IsActionJustPressed("splitting_explosion"))
		{
			GD.Print("splitting_explosion");
			SplitExplode();
		}
	}

	public void SplitExplode()
	{
		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node is not DestronoiNode destronoiNode)
			{
				continue;
			}

			VSTNode vstRoot = destronoiNode.vstRoot;
			List<VSTNode> fragmentsAtGivenDepth = [];

			InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, vstRoot);

			List<VSTNode> fragmentsToRemove = [];
			List<VSTNode> fragmentsToKeep = [];

			foreach (VSTNode vstnode in fragmentsAtGivenDepth)
			{
				Vector3 leafPosition = destronoiNode.GlobalTransform * vstnode.meshInstance.GetAabb().GetCenter();

				if ((leafPosition - GlobalPosition).Length() < explosionDistance)
				{
					fragmentsToRemove.Add(vstnode);
				}
				else
				{
					fragmentsToKeep.Add(vstnode);
				}
			}

			// remove original object
			destronoiNode.QueueFree();

			// create small fragments

			int fragmentNumber = 0;

			foreach (VSTNode leaf in fragmentsToRemove)
			{
				RigidBody3D body = destronoiNode.CreateBody(leaf.meshInstance, $"Fragment_{fragmentNumber}");

				destronoiNode.fragmentContainer.AddChild(body);

				// body.ApplyCentralImpulse(new Vector3(1,1,1));

				fragmentNumber++;
			}

			// create single body from fragmentstokeep

			var meshInstances = fragmentsToKeep
			.Select(f => f.meshInstance)
			.ToList();

			MeshInstance3D combinedMeshesToKeep = CombineMeshes(meshInstances);
			RigidBody3D rigidBodyToKeep = destronoiNode.CreateBody(combinedMeshesToKeep,"combined_fragment");

			destronoiNode.fragmentContainer.AddChild(rigidBodyToKeep);

			// rigidBodyToKeep.Freeze = true;

			// rigidBodyToKeep.GlobalPosition = destronoiNode.GlobalPosition;
		}
	}

	public void InitialiseFragmentsAtGivenDepth(List<VSTNode> fragmentsAtGivenDepth, VSTNode currentVSTNode)
	{
		if (currentVSTNode.level == explosionDepth)
		{
			fragmentsAtGivenDepth.Add(currentVSTNode);
			return;
		}

		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, currentVSTNode.left);
		InitialiseFragmentsAtGivenDepth(fragmentsAtGivenDepth, currentVSTNode.right);
	}

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

			// Keep the same material as the first mesh instance
			// var material = meshInstance.GetActiveMaterial(0);
			// if (material is not null)
			// {
			// 	surfaceTool.SetMaterial(material);
			// }

			// Append all surfaces from this mesh with the MeshInstance's transform
			// THIS IS NOT NECESSARILY A GOOD WAY TO DO THINGS
			// should prolly only be adding the external faces lmao... but this works for now
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
}
