using Godot;
using System.Collections.Generic;

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
			SplitExplode();
		}
	}

	public void SplitExplode()
	{
		foreach (Node3D node in GetOverlappingBodies())
		{
			if (node.GetParent() is not DestronoiNode destronoiNode)
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
				Vector3 leafPosition = destronoiNode.baseObject.GlobalTransform * vstnode.meshInstance.GetAabb().GetCenter();

				if ((leafPosition - GlobalPosition).Length() < explosionDistance)
				{
					fragmentsToRemove.Add(vstnode);
				}
				else
				{
					fragmentsToKeep.Add(vstnode);
				}
			}

			int fragmentNumber = 0;

			foreach (VSTNode leaf in fragmentsToRemove)
			{
				RigidBody3D body = destronoiNode.CreateBody(leaf, $"Fragment_{fragmentNumber}");

				destronoiNode.fragmentContainer.AddChild(body);

				fragmentNumber++;
			}

			// create body from fragmentstokeep

			destronoiNode.baseObject.QueueFree();
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
}
