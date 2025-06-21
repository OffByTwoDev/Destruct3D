using Godot;
using System.Collections.Generic;
using System;

/// <summary>
/// Subdivides a convex ArrayMesh belonging to a RigidBody3D by generating a Voronoi Subdivision Tree (VST).
/// </summary>
public partial class DestronoiNode : Node3D
{
	// The root node of the VST.
	private VSTNode vstRoot;

	// Generates 2^n fragments, where n is treeHeight.
	[Export(PropertyHint.Range, "1,8")] public int treeHeight = 1;

	[Export] public MeshInstance3D meshInstance;
	[Export] public Node fragmentContainer;
	[Export] public RigidBody3D baseObject;

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Input.IsActionJustPressed("debug_explode"))
		{
			Destroy(2, 2, 1f);
		}

		if (Input.IsActionJustPressed("debug_ready"))
		{
			TempFunc();
		}
	}

	public void TempFunc()
	{
		if (meshInstance is null)
		{
			GD.Print("[Destronoi] No MeshInstance3D set");
			return;
		}

		vstRoot = new VSTNode(meshInstance);

		// Plot 2 sites for the subdivision
		PlotSitesRandom(vstRoot);
		// Generate 2 children from the root
		Bisect(vstRoot);
		// Perform additional subdivisions depending on tree height
		for (int i = 0; i < treeHeight - 1; i++)
		{
			List<VSTNode> leaves = [];
			vstRoot.GetLeafNodes(vstRoot, leaves);
			foreach (VSTNode leaf in leaves)
			{
				PlotSitesRandom(leaf);
				Bisect(leaf);
			}
		}
	}

	public void PlotSites(VSTNode node, Vector3 site1, Vector3 site2)
	{
		node.sites = new List<Vector3> { node.meshInstance.Position + site1, node.meshInstance.Position + site2 };
	}

	public void PlotSitesRandom(VSTNode node)
	{
		node.sites = [];
		var mdt = new MeshDataTool();

		if (node.meshInstance.Mesh is not ArrayMesh arrayMesh)
		{
			GD.Print("arraymesh must be passed to plotsitesrandom, not any other type of mesh");
			return;
		}

		mdt.CreateFromSurface(arrayMesh, 0);

		var aabb = node.meshInstance.GetAabb();
		var min = aabb.Position;
		var max = aabb.End;
		aabb.GetCenter();

		float avgX = (min.X + max.X) * 0.5f;
		float avgY = (min.Y + max.Y) * 0.5f;
		float avgZ = (min.Z + max.Z) * 0.5f;

		float deviation = 0.1f;

		if (mdt.GetFaceCount() == 0)
		{
			GD.Print("no faces found in meshdatatool, plotsitesrandom will loop forever. returning early");
			return;
		}

		int tries = 0;

		while (node.sites.Count < 2)
		{
			tries++;
			if (tries > 5000)
			{
				GD.Print("over 5k tries exceeded, exiting PlotSitesRandom");
				return;
			}

			var site = new Vector3(
				(float)GD.Randfn(avgX, deviation),
				(float)GD.Randfn(avgY, deviation),
				(float)GD.Randfn(avgZ, deviation)
			);

			int intersections = 0;

			var direction = Vector3.Up;

			for (int face = 0; face < mdt.GetFaceCount(); face++)
			{
				int v0 = mdt.GetFaceVertex(face, 0);
				int v1 = mdt.GetFaceVertex(face, 1);
				int v2 = mdt.GetFaceVertex(face, 2);
				var p0 = mdt.GetVertex(v0);
				var p1 = mdt.GetVertex(v1);
				var p2 = mdt.GetVertex(v2);

				Variant intersectionPoint = Geometry3D.RayIntersectsTriangle(site, direction, p0, p1, p2);
				if (intersectionPoint.VariantType != Variant.Type.Nil)
				{
					intersections++;
				}
			}

			
			// if number of intersections is 1, its inside (???)
			if (intersections == 1)
			{
				node.sites.Add(site);
			}
		}
	}

	public bool Bisect(VSTNode node)
	{
		if (node.GetSiteCount() != 2)
			return false;

		var a = node.sites[0];
		var b = node.sites[1];
		var normal = (b - a).Normalized();
		var pos = a + (b - a) * 0.5f;
		var plane = new Plane(normal, pos);

		var data = new MeshDataTool();
		data.CreateFromSurface(node.meshInstance.Mesh as ArrayMesh, 0);

		var surfA = new SurfaceTool();
		surfA.Begin(Mesh.PrimitiveType.Triangles);
		surfA.SetMaterial(node.GetOverrideMaterial());
		surfA.SetSmoothGroup(UInt32.MaxValue);

		var surfB = new SurfaceTool();
		surfB.Begin(Mesh.PrimitiveType.Triangles);
		surfB.SetMaterial(node.GetOverrideMaterial());
		surfB.SetSmoothGroup(UInt32.MaxValue);

		for (int side = 0; side < 2; side++)
		{
			var st = new SurfaceTool();
			st.Begin(Mesh.PrimitiveType.Triangles);
			st.SetMaterial(node.meshInstance.GetActiveMaterial(0));
			st.SetSmoothGroup(UInt32.MaxValue);

			if (side == 1)
			{
				plane.Normal = -plane.Normal;
				plane.D = -plane.D;
			}

			List<Vector3?> coplanar = [];

			for (int f = 0; f < data.GetFaceCount(); f++)
			{
				var face = new int[] { data.GetFaceVertex(f, 0), data.GetFaceVertex(f, 1), data.GetFaceVertex(f, 2) };
				List<int> above = [];
				List<Vector3?> intersects = [];
				foreach (var vid in face)
				{
					if (plane.IsPointOver(data.GetVertex((int)vid)))
						above.Add(vid);
				}
				if (above.Count == 0)
					continue;
				if (above.Count == 3)
				{
					foreach (var vid in face)
						st.AddVertex(data.GetVertex(vid));
					continue;
				}
				if (above.Count == 1)
				{
					int vid = (int)above[0];
					int i0 = (Array.IndexOf(face,vid) + 1) % 3;
					int i1 = (Array.IndexOf(face,vid) + 2) % 3;
					Vector3? pA = plane.IntersectsSegment(data.GetVertex(vid), data.GetVertex(face[i0]));
					Vector3? pB = plane.IntersectsSegment(data.GetVertex(vid), data.GetVertex(face[i1]));
					intersects.Add(pA);
					intersects.Add(pB);
					coplanar.Add(pA);
					coplanar.Add(pB);
					st.AddVertex(data.GetVertex(vid));
					st.AddVertex((Vector3)intersects[0]);
					st.AddVertex((Vector3)intersects[1]);
					continue;
				}
				if (above.Count == 2)
				{
					int belowIdx = face[0] != (int)above[0] && face[0] != (int)above[1] ? 0 :
								   face[1] != (int)above[0] && face[1] != (int)above[1] ? 1 : 2;
					var pA = plane.IntersectsSegment(data.GetVertex((int)above[1]), data.GetVertex(face[belowIdx]));
					var pB = plane.IntersectsSegment(data.GetVertex((int)above[0]), data.GetVertex(face[belowIdx]));
					intersects.Add(pA);
					intersects.Add(pB);
					coplanar.Add(pA);
					coplanar.Add(pB);

					st.AddVertex(data.GetVertex((int)above[0]));
					st.AddVertex(data.GetVertex((int)above[1]));
					st.AddVertex((Vector3)intersects[0]);
					st.AddVertex((Vector3)intersects[0]);
					st.AddVertex((Vector3)intersects[1]);
					st.AddVertex(data.GetVertex((int)above[1]));
					continue;
				}
			}

			// cap polygon
			var center = Vector3.Zero;
			foreach (Vector3 v in coplanar)
				center += v;
			center /= coplanar.Count;
			for (int i = 0; i < coplanar.Count - 1; i += 2)
			{
				st.AddVertex((Vector3)coplanar[i + 1]);
				st.AddVertex((Vector3)coplanar[i]);
				st.AddVertex(center);
			}

			if (side == 0) surfA = st; else surfB = st;
		}

		surfA.Index(); surfA.GenerateNormals();
		surfB.Index(); surfB.GenerateNormals();

		var meshUp = new MeshInstance3D { Mesh = (ArrayMesh)surfA.Commit() };
		node.left = new VSTNode(meshUp, node.level + 1, Laterality.LEFT);

		var meshDown = new MeshInstance3D { Mesh = (ArrayMesh)surfB.Commit() };
		node.right = new VSTNode(meshDown, node.level + 1, Laterality.RIGHT);

		return true;
	}

	public void Destroy(int leftVal = 1, int rightVal = 1, float combustVelocity = 0f)
	{
		List<VSTNode> leaves = [];
		vstRoot.GetLeftLeafNodes(vstRoot, leaves, leftVal);
		vstRoot.GetRightLeafNodes(vstRoot, leaves, rightVal);

		List<RigidBody3D> fragments = [];
		float totalMass = 0f;

		foreach (VSTNode leaf in leaves)
		{
			var body = new RigidBody3D();
			body.Name = $"VFragment_{fragments.Count}";
			body.Position = Position;

			var mi = leaf.meshInstance;
			mi.Name = "MeshInstance3D";
			body.AddChild(mi);

			var shape = new CollisionShape3D { Name = "CollisionShape3D" };

			float mass = Mathf.Max(mi.Mesh.GetAabb().Size.Length(), 0.1f);
			body.Mass = mass; totalMass += mass;

			if (!Mathf.IsZeroApprox(combustVelocity))
			{
				// simple outward velocity
				var dir = mi.Mesh.GetAabb().Position - baseObject.Position;
				// was .axisvelocity, i just replaced it with linearvelocity idk if thats correct
				body.LinearVelocity = dir.Normalized() * combustVelocity;
			}

			shape.Shape = mi.Mesh.CreateConvexShape(false, false);
			body.AddChild(shape);
			fragmentContainer.AddChild(body);
			fragments.Add(body);
		}

		foreach (RigidBody3D frag in fragments)
			frag.Mass *= baseObject.Mass / totalMass;

		baseObject.QueueFree();
	}
}
