using Godot;
using System;

[Tool]
public partial class BinaryTreeVisualizerPlugin : EditorPlugin
{
    private Control dock;

    public override void _EnterTree()
    {
        var scene = GD.Load<PackedScene>("res://addons/binary_tree_visualizer/binary_tree_dock.tscn");
        dock = (Control)scene.Instantiate();
        AddControlToDock(DockSlot.RightUl, dock);
        dock.Visible = true;
    }

    public override void _ExitTree()
    {
        RemoveControlFromDocks(dock);
        dock.QueueFree();
    }
}