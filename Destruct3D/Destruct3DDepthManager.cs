using Godot;
using System;
using System.Collections.Generic;

namespace Destruct3D;

/// <summary>
/// if you make all your destructibleBody3Ds a child of a node, then this manager can forcibly change destructibleBody3D treeDepths.
/// </summary>
/// <remarks>
/// this is useful for forcing all destructibleBody3Ds to have small treeDepths (to speed up startup times during testing etc) without having to change them all individually.<br></br>
/// make sure to place this manager physically before the destructibleBody3Ds' parent in the editor scene tree, so that the _Ready is called before any destructibleBody3Ds are initialized
/// </remarks>

[GlobalClass]
public partial class Destruct3DDepthManager : Node
{
	[Export] Node destructibleBody3DsParent;

	/// <summary>
	/// if debugMode is true, then this node will force all destructibleBody3Ds that are a direct child of <paramref name="destructibleBody3DsParent"/> to have a treeHeight of <paramref name="forceTreeDepth"/>.
	/// </summary>
	/// <remarks>
	/// this does not affect any editor settings, i.e. it doesnt overwrite the treeDepth stored in your .tscns. (it writes to stuff after the level is loaded in, so its not destructive. This way you can just turn debugMode off and your level will load will all your destructibleBody3Ds heights set to whatever they are.)
	/// </remarks>
	[Export] private bool debugMode;

	[Export] private int forceTreeDepth = 3;

	public override void _Ready()
	{
		base._Ready();
		
		if (!debugMode)
		{
			return;
		}

		foreach (Node child in destructibleBody3DsParent.GetChildren())
		{
			if (child is not DestructibleBody3D destructibleBody3D)
			{
				GD.PushWarning("non destructibleBody3D found as child of destructibleBody3DsParent by Destruct3DDepthManager");
				continue;
			}

			destructibleBody3D.treeHeight = forceTreeDepth;
		}
	}
}
