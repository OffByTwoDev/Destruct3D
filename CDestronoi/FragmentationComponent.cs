using Godot;

namespace CDestronoi;

public partial class FragmentationComponent : Node
{
	[Export] DestronoiNode destronoiNode;

	[Export(PropertyHint.Range, "1,8")] public int depth = 1;

	[Export] public float combustionStrength;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Input.IsActionJustPressed("cs_debug_explode"))
		{
			destronoiNode.Destroy(depth, combustionStrength);
		}
	}
}
