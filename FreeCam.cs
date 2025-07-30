using Godot;

namespace Destruct3D;

public partial class FreeCam : Camera3D
{
	// if true, this script will deactivate all collisions for the player after the first use of the freecam
	[Export] private bool PlayerDebugMode = false;

	[Export] private float Speed = 15.0f;

	[Export] private float SPRINT_FACTOR = 5.0f;

	private bool Sprinting = false;

	//should prolly be the same as the player's main camera sensitivity
	private const float CameraSensitivity = 0.005f;

	public override void _Ready()
	{
		base._Ready();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);

		if (Current)
		{
			Vector2 inputVector = new(Input.GetAxis("left", "right"), Input.GetAxis("backward", "forward"));

			Position += Basis * new Vector3(inputVector.X * Speed * (float)delta, 0, -inputVector.Y * Speed * (float)delta);
			
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!Current) { return; }

		base._UnhandledInput(@event);

		if (@event is InputEventMouseButton mouseButtonEvent)
		{
			if (mouseButtonEvent.ButtonIndex == MouseButton.Left)
			{
				Input.SetMouseMode(Input.MouseModeEnum.Captured);
			}
		}

		else if (Input.IsActionPressed("ui_cancel"))
		{
			Input.SetMouseMode(Input.MouseModeEnum.Visible);
		}

		if (Input.GetMouseMode() == Input.MouseModeEnum.Captured &&
			@event is InputEventMouseMotion mouseMotionEvent)
		{
			GlobalRotate(Vector3.Up, -mouseMotionEvent.Relative.X * CameraSensitivity);
			RotateObjectLocal(new Vector3(1, 0, 0), -mouseMotionEvent.Relative.Y * CameraSensitivity);
		}

		if (Input.IsActionJustPressed("sprint_toggle"))
		{
			if (Sprinting)
			{
				Speed /= SPRINT_FACTOR;
			}
			else
			{
				Speed *= SPRINT_FACTOR;
			}

			Sprinting = !Sprinting;
		}
	}
}
