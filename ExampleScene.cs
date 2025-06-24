using Godot;
using System;

public partial class ExampleScene : Node3D
{
	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (Input.IsActionJustPressed("reset_example_scene"))
		{
			GD.Print("reloading example scene...");
			GetTree().ReloadCurrentScene(); 
		}

		else if (Input.IsActionJustPressed("close_window"))
		{
			GetTree().Quit();
		}   
	}
}
