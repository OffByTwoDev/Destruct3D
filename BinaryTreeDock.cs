using Godot;
using System;

[Tool]
public partial class BinaryTreeDock : Control
{
	// Simple binary tree node class
	public class TreeNode
	{
		public int Value;
		public TreeNode Left;
		public TreeNode Right;

		public TreeNode(int value)
		{
			Value = value;
			Left = null;
			Right = null;
		}
	}

	private TreeNode root;

	public override void _Ready()
	{
		// Build a sample binary tree
		root = new TreeNode(1);
		root.Left = new TreeNode(2);
		root.Right = new TreeNode(3);
		root.Left.Left = new TreeNode(4);
		root.Left.Right = new TreeNode(5);
	}

	public override void _Draw()
	{
		if (root != null)
		{
			Vector2 startPos = new Vector2(GetViewportRect().Size.X / 2, 20);
			DrawTree(root, startPos, GetViewportRect().Size.X / 4, 50);
		}
	}

	private void DrawTree(TreeNode node, Vector2 pos, float xOffset, float yStep)
	{
		// Draw node circle
		DrawCircle(pos, 15, Colors.White);
		// Draw node value (adjust positioning for text)
		DrawString(GetThemeDefaultFont(), pos + new Vector2(-5, 5), node.Value.ToString());

		// Draw left child
		if (node.Left != null)
		{
			Vector2 leftPos = pos + new Vector2(-xOffset, yStep);
			DrawLine(pos, leftPos, Colors.White, 2);
			DrawTree(node.Left, leftPos, xOffset / 2, yStep);
		}

		// Draw right child
		if (node.Right != null)
		{
			Vector2 rightPos = pos + new Vector2(xOffset, yStep);
			DrawLine(pos, rightPos, Colors.White, 2);
			DrawTree(node.Right, rightPos, xOffset / 2, yStep);
		}
	}
}
