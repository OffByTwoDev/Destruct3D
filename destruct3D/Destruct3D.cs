#if TOOLS
using Godot;
using System;

namespace Destruct3D;

[Tool]
public partial class Destruct3D : EditorPlugin
{
	public override void _EnterTree()
	{
		var script = GD.Load<Script>("res://addons/MyCustomNode/MyButton.cs");
		var texture = GD.Load<Texture2D>("res://addons/MyCustomNode/Icon.png");
		AddCustomType("DestructibleBody3D", "RigidBody3D", script, texture);
	}

	public override void _ExitTree()
	{
		RemoveCustomType("DestructibleBody3D");
	}
}

#endif

