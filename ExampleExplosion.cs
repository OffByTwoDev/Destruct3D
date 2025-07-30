using Destruct3D;
using Godot;
using System;

public partial class ExampleExplosion : Node3D
{
	[Export] private VSTSplittingComponent vstSplittingComponent;

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (Input.IsActionJustPressed("splitting_explosion"))
		{
			_ = vstSplittingComponent.Activate();
		}
	}
}
