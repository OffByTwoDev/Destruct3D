using Godot;
using System;
using System.Collections.Generic;

namespace Destruct3D;

/// <summary>
/// if you make all your destronoiNodes a child of a node, then this manager can forcibly change destronoiNode treeDepths.
/// </summary>
/// <remarks>
/// this is useful for forcing all destronoiNodes to have small treeDepths (to speed up startup times during testing etc) without having to change them all individually.<br></br>
/// make sure to place this manager physically before the destronoiNodes' parent in the editor scene tree, so that the _Ready is called before any destronoiNodes are initialized
/// </remarks>

[GlobalClass]
public partial class Destruct3DDepthManager : Node
{
	[Export] Node destronoiNodesParent;

    /// <summary>
    /// if debugMode is true, then this node will force all destronoiNodes that are a direct child of <paramref name="destronoiNodesParent"/> to have a treeHeight of <paramref name="forceTreeDepth"/>.
    /// </summary>
    /// <remarks>
    /// this does not affect any editor settings, i.e. it doesnt overwrite the treeDepth stored in your .tscns. (it writes to stuff after the level is loaded in, so its not destructive. This way you can just turn debugMode off and your level will load will all your destronoiNode heights set to whatever they are.)
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

		foreach (Node child in destronoiNodesParent.GetChildren())
		{
			if (child is not DestructibleBody3D destronoiNode)
			{
				GD.PushWarning("non destronoiNode found as child of destronoiNodesParent by DestronoiDepthManager");
				continue;
			}

			destronoiNode.treeHeight = forceTreeDepth;
		}
	}
}
