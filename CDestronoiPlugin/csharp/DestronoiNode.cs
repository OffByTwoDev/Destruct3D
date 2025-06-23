using Godot;
using System.Collections.Generic;
using System;
using System.Linq;

/// <summary>
/// Subdivides a convex ArrayMesh belonging to a RigidBody3D by generating a Voronoi Subdivision Tree (VST).
/// </summary>
public partial class DestronoiNode : Node3D
{
	// The root node of the VST.
	public VSTNode vstRoot;

	// Generates 2^n fragments, where n is treeHeight.
	[Export(PropertyHint.Range, "1,8")] public int treeHeight = 1;

	[Export] public MeshInstance3D meshInstance;
	[Export] public Node fragmentContainer;
	[Export] public RigidBody3D baseObject;

	[Export] MeshInstance3D baseObjectMeshInstance;
	float baseObjectDensity;

	public override void _Ready()
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
			VSTNode.GetLeafNodes(vstRoot, leaves);
			foreach (VSTNode leaf in leaves)
			{
				PlotSitesRandom(leaf);
				Bisect(leaf);
			}
		}

		float baseObjectVolume = baseObjectMeshInstance.Mesh.GetAabb().Size.X *
								 baseObjectMeshInstance.Mesh.GetAabb().Size.Y *
								 baseObjectMeshInstance.Mesh.GetAabb().Size.Z;
		
		baseObjectDensity = baseObject.Mass / baseObjectVolume;

	}

	public void PlotSites(VSTNode node, Vector3 site1, Vector3 site2)
	{
		node.sites = [node.meshInstance.Position + site1, node.meshInstance.Position + site2];
	}

	public static void PlotSitesRandom(VSTNode node)
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
		// aabb.GetCenter();

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

	public static bool Bisect(VSTNode node)
	{
		if (node.GetSiteCount() != 2)
			return false;

		var siteA = node.sites[0];
		var siteB = node.sites[1];
		var planeNormal = (siteB - siteA).Normalized();
		var planePosition = siteA + (siteB - siteA) * 0.5f;
		var plane = new Plane(planeNormal, planePosition);

		MeshDataTool dataTool = new();
		dataTool.CreateFromSurface(node.meshInstance.Mesh as ArrayMesh, 0);

		SurfaceTool surfA = new();
		surfA.Begin(Mesh.PrimitiveType.Triangles);
		surfA.SetMaterial(node.GetOverrideMaterial());
		surfA.SetSmoothGroup(UInt32.MaxValue);

		SurfaceTool surfB = new();
		surfB.Begin(Mesh.PrimitiveType.Triangles);
		surfB.SetMaterial(node.GetOverrideMaterial());
		surfB.SetSmoothGroup(UInt32.MaxValue);
		
		// GENERATE SUB MESHES
		// ITERATE OVER EACH FACE OF THE BASE MESH
		// 2 iterations for 2 sub meshes (above/below)
		for (int side = 0; side < 2; side++)
		{
			SurfaceTool surfaceTool = new();
			surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
			surfaceTool.SetMaterial(node.meshInstance.GetActiveMaterial(0));
			surfaceTool.SetSmoothGroup(UInt32.MaxValue);

			if (side == 1)
			{
				plane.Normal = -plane.Normal;
				plane.D = -plane.D;
			}

			// for new vertices which intersect the plane
			List<Vector3?> coplanar = [];

			for (int f = 0; f < dataTool.GetFaceCount(); f++)
			{
				var faceVertices = new int[] { dataTool.GetFaceVertex(f, 0), dataTool.GetFaceVertex(f, 1), dataTool.GetFaceVertex(f, 2) };
				List<int> verticesAbovePlane = [];
				List<Vector3?> intersects = [];

				// ITERATE OVER EACH VERTEX AND DETERMINE "ABOVENESS"
				foreach (var vertexIndex in faceVertices)
				{
					if (plane.IsPointOver(dataTool.GetVertex(vertexIndex)))
					{
						verticesAbovePlane.Add(vertexIndex);
					}
				}

				// INTERSECTION CASE 0/0.5: ALL or NOTHING above the plane
				if (verticesAbovePlane.Count == 0)
					continue;
				if (verticesAbovePlane.Count == 3)
				{
					foreach (var vid in faceVertices)
						surfaceTool.AddVertex(dataTool.GetVertex(vid));
					continue;
				}

				// INTERSECTION CASE 1: ONE point above the plane
				// Find intersection points and append them in cw winding order
				if (verticesAbovePlane.Count == 1)
				{
					int vid = verticesAbovePlane[0];
					int indexAfter = (Array.IndexOf(faceVertices, vid) + 1) % 3;
					int indexBefore = (Array.IndexOf(faceVertices, vid) + 2) % 3;
					Vector3? intersectionAfter = plane.IntersectsSegment(dataTool.GetVertex(vid), dataTool.GetVertex(faceVertices[indexAfter]));
					Vector3? intersectionBefore = plane.IntersectsSegment(dataTool.GetVertex(vid), dataTool.GetVertex(faceVertices[indexBefore]));
					intersects.Add(intersectionAfter);
					intersects.Add(intersectionBefore);
					coplanar.Add(intersectionAfter);
					coplanar.Add(intersectionBefore);

					// TRIANGLE CREATION
					surfaceTool.AddVertex(dataTool.GetVertex(vid));
					surfaceTool.AddVertex((Vector3)intersects[0]);
					surfaceTool.AddVertex((Vector3)intersects[1]);
					continue;
				}

				if (verticesAbovePlane.Count == 2)
				{
					int indexRemaining;
					if (verticesAbovePlane[0] != faceVertices[1] &&
						verticesAbovePlane[1] != faceVertices[1])
					{
						verticesAbovePlane.Reverse();
						indexRemaining = 1;
					}
					else if (verticesAbovePlane[0] != faceVertices[0] &&
						verticesAbovePlane[1] != faceVertices[0])
					{
						indexRemaining = 0;
					}
					else
					{
						indexRemaining = 2;
					}

					var intersectionAfter = plane.IntersectsSegment(
						dataTool.GetVertex(verticesAbovePlane[1]),
						dataTool.GetVertex(faceVertices[indexRemaining]));

					var intersectionBefore = plane.IntersectsSegment(
						dataTool.GetVertex(verticesAbovePlane[0]),
						dataTool.GetVertex(faceVertices[indexRemaining]));

					intersects.Add(intersectionAfter);
					intersects.Add(intersectionBefore);
					coplanar.Add(intersectionAfter);
					coplanar.Add(intersectionBefore);

					// find shortest 'cross-length' to make 2 triangles from 4 points

					int indexShortest = 0;

					float distance0 = dataTool.GetVertex(verticesAbovePlane[0]).DistanceTo((Vector3)intersects[0]);
					float distance1 = dataTool.GetVertex(verticesAbovePlane[1]).DistanceTo((Vector3)intersects[1]);

					if (distance1 > distance0)
					{
						indexShortest = 1;
					}

					// TRIANGLE 1
					surfaceTool.AddVertex(dataTool.GetVertex(verticesAbovePlane[0]));
					surfaceTool.AddVertex(dataTool.GetVertex(verticesAbovePlane[1]));

					surfaceTool.AddVertex((Vector3)intersects[indexShortest]);

					// TRIANGLE 2

					surfaceTool.AddVertex((Vector3)intersects[0]);
					surfaceTool.AddVertex((Vector3)intersects[1]);

					surfaceTool.AddVertex(dataTool.GetVertex(verticesAbovePlane[indexShortest]));
					continue;
				}
			}
			// END for face in range(data_tool.get_face_count())

			// cap polygon
			var center = Vector3.Zero;
			foreach (Vector3 v in coplanar.Select(v => (Vector3)v))
				center += v;
			center /= coplanar.Count;
			for (int i = 0; i < coplanar.Count - 1; i += 2)
			{
				surfaceTool.AddVertex((Vector3)coplanar[i + 1]);
				surfaceTool.AddVertex((Vector3)coplanar[i]);
				surfaceTool.AddVertex(center);
			}

			if (side == 0) surfA = surfaceTool; else surfB = surfaceTool;
		}

		surfA.Index(); surfA.GenerateNormals();
		surfB.Index(); surfB.GenerateNormals();

		var meshUp = new MeshInstance3D { Mesh = surfA.Commit() };
		node.left = new VSTNode(meshUp, node.level + 1, Laterality.LEFT);

		var meshDown = new MeshInstance3D { Mesh = surfB.Commit() };
		node.right = new VSTNode(meshDown, node.level + 1, Laterality.RIGHT);

		return true;
	}

	public void Destroy(int leftVal = 1, int rightVal = 1, float combustVelocity = 0f)
	{
		List<VSTNode> leaves = [];
		VSTNode.GetLeftLeafNodes(vstRoot, leaves, leftVal);
		VSTNode.GetRightLeafNodes(vstRoot, leaves, rightVal);

		int fragmentNumber = 0;

		foreach (VSTNode leaf in leaves)
		{
			RigidBody3D body = CreateBody(leaf.meshInstance, $"Fragment_{fragmentNumber}");


			// destruction velocity
			if (!Mathf.IsZeroApprox(combustVelocity))
			{
				// simple outward velocity
				var dir = meshInstance.Mesh.GetAabb().Position - baseObject.Position;
				// was .axisvelocity, i just replaced it with linearvelocity idk if thats correct
				body.LinearVelocity = dir.Normalized() * combustVelocity;
			}
			// add to scene
			fragmentContainer.AddChild(body);

			fragmentNumber++;
		}

		baseObject.QueueFree();
	}

	/// <summary>
	/// creates a rigidbody from the given meshInstance
	/// </summary>
	public RigidBody3D CreateBody(MeshInstance3D leafMeshInstance, String name)
	{
			// initialise rigidbody
			RigidBody3D body = new()
			{
				Name = name,
				Position = baseObject.GlobalPosition
			};

			// mesh instance
			MeshInstance3D meshInstance = leafMeshInstance;
			meshInstance.Name = "MeshInstance3D";
			body.AddChild(meshInstance);

			// collisionshape
			var shape = new CollisionShape3D
			{
				Name = "CollisionShape3D",
				Shape = meshInstance.Mesh.CreateConvexShape(false, false)
			};
			body.AddChild(shape);

			// mass
			float volume =  meshInstance.Mesh.GetAabb().Size.X *
							meshInstance.Mesh.GetAabb().Size.Y *
							meshInstance.Mesh.GetAabb().Size.Z;
			body.Mass = baseObjectDensity * volume;

			// needed (idk why lmao ?) for detecting explosions from RPGs
			body.ContactMonitor = true;
			body.MaxContactsReported = 5_000;

			return body;
	}
}
