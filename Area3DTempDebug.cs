using Godot;
using System;

public partial class Area3DTempDebug : Area3D
{
	public override void _PhysicsProcess(double delta)
	{
		base._PhysicsProcess(delta);

		GD.Print(GetOverlappingBodies());
	}
}
