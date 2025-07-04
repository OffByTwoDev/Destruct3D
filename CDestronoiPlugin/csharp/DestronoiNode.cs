using Godot;
using System.Collections.Generic;
using System;
using System.Linq;

/// <summary>
/// Subdivides a convex ArrayMesh belonging to a RigidBody3D by generating a Voronoi Subdivision Tree (VST).
/// </summary>
public partial class DestronoiNode : RigidBody3D
{
	// --- Exports --- //
	[Export] public MeshInstance3D meshInstance;
	// node under which fragments will be instanced
	[Export] public Node fragmentContainer;
	// Generates 2^n fragments, where n is treeHeight.
	[Export] public int treeHeight = 1;
	
	// only relevant for DestronoiNodes which are present on level startup
	[Export] Node binaryTreeMapContainer;
	public BinaryTreeMapToActiveNodes binaryTreeMapToActiveNodes;

	// --- internal variables --- //
	public VSTNode vstRoot;
	private float baseObjectDensity;
	// this should be true if the node needs its own vstRoot created
	// if you are creating a fragment and are passing in a known vstRoot, mesh etc, then this flag should be false
	private bool needsInitialising = true;

	// --- meta variables --- //
	public int MAX_PLOTSITERANDOM_TRIES = 5_000;

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (Input.IsActionJustPressed("debug_print_vst"))
		{
			DebugPrintVST(vstRoot);
		}
	}

	public static void DebugPrintVST(VSTNode vstNode)
	{
		if (vstNode is null)
		{
			return;
		}

		GD.Print("ID: ", vstNode.ID);
		GD.Print("left: ", vstNode.left?.ID);
		GD.Print("right: ", vstNode.right?.ID);
		GD.Print("parent: ", vstNode.parent?.ID);
		GD.Print("level: ", vstNode.level);
		GD.Print("laterality: ", vstNode.laterality);
		GD.Print("ownerID: ", vstNode.ownerID);
		GD.Print("---");

		DebugPrintVST(vstNode.left);
		DebugPrintVST(vstNode.right);
	}

	public void ErrorChecks()
	{
		if (!(GetChildCount() == 2))
		{
			GD.PushError($"CDestronoiNodes must have only 2 children. The node named {Name} does not");
			return;
		}

		if (!(
			(GetChildren()[0] is MeshInstance3D && GetChildren()[1] is CollisionShape3D) ||
			(GetChildren()[1] is MeshInstance3D && GetChildren()[0] is CollisionShape3D)
			))
		{
			GD.PushError($"CDestronoiNodes must have 1 collisionshape3d child and 1 meshinstance3d child. The node named {Name} does not");
			return;
		}

		foreach (Node3D child in GetChildren().Cast<Node3D>())
		{
			if (child.Transform != Transform3D.Identity)
			{
				// if the transform is modified, then fragments will be instantiated in incorrect positions
				// just move the CDestronoi node directly and keep the mesh and collisionshape centered around the origin
				GD.PushError("the collisionshape and mesh children of a CDestronoi node must have an unmodified transform");
			}
		}
	}

	public override void _Ready()
	{
		base._Ready();

		if (!needsInitialising)
		{
			return;
		}

		// --- do some error checks, but only after children have been added to scene i.e. we wait one frame --- //

		CallDeferred(nameof(ErrorChecks));

		// --- create filled VST --- //

		if (meshInstance is null)
		{
			GD.PushError("[Destronoi] No MeshInstance3D set");
			return;
		}

		// the topmost node has no parent hence null & no laterality & not endPoint
		vstRoot = new VSTNode(meshInstance, 1, 1, null, 0, Laterality.NONE, false);

		// Plot 2 sites for the subdivision
		PlotSitesRandom(vstRoot);
		// Generate 2 children from the root
		Bisect(vstRoot, false);
		// Perform additional subdivisions depending on tree height
		for (int i = 0; i < treeHeight - 1; i++)
		{
			List<VSTNode> leaves = [];
			VSTNode.GetLeafNodes(vstRoot, leaves);
			foreach (VSTNode leaf in leaves)
			{
				PlotSitesRandom(leaf);
				// on final pass, set children as endPoints
				if (i == treeHeight - 2)
				{
					Bisect(leaf, true);
				}
				else
				{
					Bisect(leaf, false);
				}
				
			}
		}

		// --- find density --- //
		float volume = meshInstance.Mesh.GetAabb().Size.X *
								 meshInstance.Mesh.GetAabb().Size.Y *
								 meshInstance.Mesh.GetAabb().Size.Z;
		
		baseObjectDensity = Mass / volume;

		// --- create a binarytreemap and set its root to be this node --- //

		binaryTreeMapToActiveNodes = new(treeHeight, this);
		binaryTreeMapContainer.AddChild(binaryTreeMapToActiveNodes);
	}

	public static void PlotSites(VSTNode node, Vector3 site1, Vector3 site2)
	{
		node.sites = [node.meshInstance.Position + site1, node.meshInstance.Position + site2];
	}

	public void PlotSitesRandom(VSTNode node)
	{
		node.sites = [];
		var mdt = new MeshDataTool();

		if (node.meshInstance.Mesh is not ArrayMesh arrayMesh)
		{
			GD.PushError("arraymesh must be passed to plotsitesrandom, not any other type of mesh");
			return;
		}

		mdt.CreateFromSurface(arrayMesh, 0);

		if (mdt.GetFaceCount() == 0)
		{
			GD.PushWarning("no faces found in meshdatatool, plotsitesrandom will loop forever. returning early");
			return;
		}

		int tries = 0;

		var direction = Vector3.Up;

		// seems weird to me to have a hardcoded deviation
		// as it could lead to a lot of non overlapping tries for fragments much less than 0.1 meters in size
		// so i've removed that and changed it to generate points within the aabb of the meshInstance
		// i believe this also has the benefit of being uniformly distributed throughout the fragment's meshInstance
		var aabb = node.meshInstance.GetAabb();

		while (node.sites.Count < 2)
		{
			tries++;

			if (tries > MAX_PLOTSITERANDOM_TRIES)
			{
				GD.PushWarning($"over {MAX_PLOTSITERANDOM_TRIES} tries exceeded, exiting PlotSitesRandom");
				return;
			}
			
			var site = new Vector3(
				(float)GD.RandRange(aabb.Position.X, aabb.End.X),
				(float)GD.RandRange(aabb.Position.Y, aabb.End.Y),
				(float)GD.RandRange(aabb.Position.Z, aabb.End.Z)
			);

			int intersections = 0;

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

			// if number of intersections is 1, its inside
			if (intersections == 1)
			{
				node.sites.Add(site);
			}
		}
	}

	public static bool Bisect(VSTNode node, bool endPoint)
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
		node.left = new VSTNode(meshUp, node.ID * 2, node.ownerID, node, node.level + 1, Laterality.LEFT, endPoint);

		var meshDown = new MeshInstance3D { Mesh = surfB.Commit() };
		node.right = new VSTNode(meshDown, node.ID * 2 + 1, node.ownerID, node, node.level + 1, Laterality.RIGHT, endPoint);

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
				var dir = meshInstance.Mesh.GetAabb().Position - Position;
				// was .axisvelocity, i just replaced it with linearvelocity idk if thats correct
				body.LinearVelocity = dir.Normalized() * combustVelocity;
			}
			// add to scene
			fragmentContainer.AddChild(body);

			fragmentNumber++;
		}

		QueueFree();
	}
	
	// DEPRECATED
	/// <summary>
	/// creates a rigidbody from the given meshInstance
	/// </summary>
	public RigidBody3D CreateBody(MeshInstance3D leafMeshInstance, String name)
	{
			// initialise rigidbody
			RigidBody3D body = new()
			{
				Name = name,
				Position = GlobalPosition
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

	/// <summary>
	/// creates a Destronoi Node from the given meshInstance and vstnode
	/// </summary>
	public DestronoiNode CreateDestronoiNode(VSTNode subVST,
											MeshInstance3D subVSTmeshInstance,
											String name,
											StandardMaterial3D material)
	{
			DestronoiNode destronoiNode = new()
			{
				Name = name,
				GlobalTransform = this.GlobalTransform
			};

			// --- rigidbody initialisation --- //

			// mesh instance
			MeshInstance3D meshInstance = subVSTmeshInstance;
			meshInstance.Name = $"{name}_MeshInstance3D";

			meshInstance.SetSurfaceOverrideMaterial(0, material);

			destronoiNode.AddChild(meshInstance);

			// collisionshape
			var shape = new CollisionShape3D
			{
				Name = "CollisionShape3D",
				Shape = meshInstance.Mesh.CreateConvexShape(false, false)
			};
			destronoiNode.AddChild(shape);

			// mass
			float volume =  meshInstance.Mesh.GetAabb().Size.X *
							meshInstance.Mesh.GetAabb().Size.Y *
							meshInstance.Mesh.GetAabb().Size.Z;
			destronoiNode.Mass = Math.Max(baseObjectDensity * volume, 0.01f);

			// needed (idk why lmao ?) for detecting explosions from RPGs
			destronoiNode.ContactMonitor = true;
			destronoiNode.MaxContactsReported = 5_000;


			// --- destronoi node initialisation --- //

			destronoiNode.meshInstance = subVSTmeshInstance;
			destronoiNode.fragmentContainer = fragmentContainer;
			destronoiNode.vstRoot = subVST;
			destronoiNode.baseObjectDensity = baseObjectDensity;
			// setting this to true will break everything,
			// this flag must be false as the vstRoot is being reused and must not be regenerated for fragments
			destronoiNode.needsInitialising = false;

			// finally, tell the relevant binarytreemap that this node has been created //
			// and also set the relevant binaryTreeMap to be this one
			destronoiNode.binaryTreeMapToActiveNodes = this.binaryTreeMapToActiveNodes;
			destronoiNode.binaryTreeMapToActiveNodes.Activate(this);
			
			return destronoiNode;
	}
}
