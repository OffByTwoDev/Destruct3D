#if TOOLS
using Godot;
using System;

namespace Destruct3D;

// for now I'm just going to [GlobalClass] the node scripts
// nothing is a [Tool] or anything so I dont see why this would break things
// and it means I don't have to keep updating relative_paths whenever I refactor
// if anyone disagrees with this approach let me know or open a github issue and I can use the standard way of defining classes using _EnterTree() and ExitTree()

// im using relative paths so it doesnt break the submodule I use to develop this plugin
// if someone knows a better way to make plugins still work with submodules lmk

[Tool]
public partial class Destruct3D : EditorPlugin
{
	// private readonly NodePath DESTRUCTIBLE_BODY_3D_RELATIVE_PATH = "/../nodes/DestructibleBody3D.cs";
	
	// public override void _EnterTree()
	// {
	// 	Script destructibleBody3DScript = ResourceLoader.Load<Script>(((Resource)GetScript()).ResourcePath + DESTRUCTIBLE_BODY_3D_RELATIVE_PATH);
	// 	AddCustomType("DestructibleBody3D", "RigidBody3D", destructibleBody3DScript, null);
	// }

	// public override void _ExitTree()
	// {
	// 	RemoveCustomType("DestructibleBody3D");
	// }
}

#endif

