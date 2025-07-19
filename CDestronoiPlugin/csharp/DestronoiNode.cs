using Godot;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Diagnostics;

namespace CDestronoi;

/// <summary>
/// Subdivides a convex ArrayMesh belonging to a RigidBody3D by generating a Voronoi Subdivision Tree (VST).
/// This code prolly fails if treeHeight is set to 1
/// </summary>
public partial class DestronoiNode : RigidBody3D
{
	// --- Exports --- //

	/// <summary>this meshInstance (which is also set in CreateDestronoiNode) is the actual meshInstance of the instantiated rigidbody. ie its the sum of all relevant endPoints / nodes with unchanged children</summary>
	[Export] public MeshInstance3D meshInstance;
	/// <summary>node under which fragments will be instanced</summary>
	[Export] public Node fragmentContainer;
	/// <summary>Generates 2^n fragments, where n is treeHeight.</summary>
	[Export] public int treeHeight = 2;
	
	/// <summary>only relevant for DestronoiNodes which are present on level startup</summary>
	public BinaryTreeMapToActiveNodes binaryTreeMapToActiveNodes;

	// --- internal variables --- //
	public VSTNode vstRoot;
	public float baseObjectDensity;
	/// <summary>this should be true if the node needs its own vstRoot created. if you are creating a fragment and are passing in a known vstRoot, mesh etc, then this flag should be false</summary>
	public bool needsInitialising = true;

	// --- meta variables --- //
	public readonly int MAX_PLOTSITERANDOM_TRIES = 5_000;
	public readonly float LINEAR_DAMP = 1.0f;
	public readonly float ANGULAR_DAMP = 1.0f;

	// required for godot
	public DestronoiNode() { }

	public DestronoiNode(	string inputName,
							Transform3D inputGlobalTransform,
							MeshInstance3D inputMeshInstance,
							Node inputFragmentContainer,
							VSTNode inputVSTRoot,
							float inputDensity,
							bool inputNeedsInitialising,
							BinaryTreeMapToActiveNodes inputBinaryTreeMapToActiveNodes)
	{
		Name = inputName;
		GlobalTransform = inputGlobalTransform;

		meshInstance = (MeshInstance3D)inputMeshInstance.Duplicate();

		// meshInstance = inputMeshInstance;
		// if (meshInstance.GetParent() is not null)
		// {
		// 	GD.PushWarning("reparenting meshinstance");
		// 	meshInstance.GetParent().RemoveChild(meshInstance);
		// }

		AddChild(meshInstance);

		var shape = new CollisionShape3D
		{
			Name = "CollisionShape3D",
			Shape = meshInstance.Mesh.CreateConvexShape(false, false),
		};

		AddChild(shape);

		fragmentContainer = inputFragmentContainer;

		vstRoot = inputVSTRoot;

		// mass
		baseObjectDensity = inputDensity;
		float volume =  meshInstance.Mesh.GetAabb().Volume;
		Mass = Math.Max(baseObjectDensity * volume, 0.01f);

		// setting this to true will break everything. this flag must be false as the vstRoot is being reused and must not be regenerated for fragments. it should only be used when the scene is being loaded
		needsInitialising = inputNeedsInitialising;

		binaryTreeMapToActiveNodes = inputBinaryTreeMapToActiveNodes;

		// needed for detecting explosions from RPGs
		ContactMonitor = true;
		MaxContactsReported = 5_000;

		LinearDamp = LINEAR_DAMP;
		AngularDamp = ANGULAR_DAMP;
	}
	
	// --- //

	public override void _UnhandledInput(InputEvent @event)
	{
		base._UnhandledInput(@event);
		
		if (Input.IsActionJustPressed("debug_print_vst"))
		{
			DebugPrintVST(vstRoot);
		}

		if (Input.IsActionJustPressed("debug_print_thing"))
		{
			GD.Print(vstRoot.childrenChanged);
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
		vstRoot = new VSTNode(	inputMeshInstance: meshInstance,
								inputID: 1,
								inputParent: null,
								inputLevel: 0,
								inputLaterality: Laterality.NONE,
								inputEndPoint: false);

		// Plot 2 sites for the subdivision
		PlotSitesRandom(vstRoot);
		// Generate 2 children from the root
		Bisect(vstRoot, false);

		// Perform additional subdivisions depending on tree height
		for (int depthIndex = 0; depthIndex < treeHeight - 1; depthIndex++)
		{
			List<VSTNode> leaves = [];

			VSTNode.GetLeafNodes(vstRoot, outArr: leaves);
			
			foreach (VSTNode leaf in leaves)
			{
				PlotSitesRandom(leaf);
				// on final pass, set children as endPoints
				if (depthIndex == treeHeight - 2)
				{
					Bisect(leaf, true);
				}
				else
				{
					Bisect(leaf, false);
				}
			}
		}

		baseObjectDensity = Mass / meshInstance.Mesh.GetAabb().Volume;

		// --- create a binarytreemap and set its root to be this node --- //
		binaryTreeMapToActiveNodes = new(treeHeight, this);

		LinearDamp = LINEAR_DAMP;
		AngularDamp = ANGULAR_DAMP;
	}

	// public static void PlotSites(VSTNode node, Vector3 site1, Vector3 site2)
	// {
	// 	node.sites = [node.meshInstance.Position + site1, node.meshInstance.Position + site2];
	// }

	public void PlotSitesRandom(VSTNode node)
	{
		node.sites = [];

		MeshDataTool mdt = new();

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

			// if number of intersections % 2 is 1, its inside
			if (intersections % 2 == 1)
			{
				node.sites.Add(site);
			}
		}
	}

	public static bool Bisect(VSTNode node, bool endPoint)
	{
		if (node.GetSiteCount() != 2)
		{
			return false;
		}
		
		var siteA = node.sites[0];
		var siteB = node.sites[1];
		var planeNormal = (siteB - siteA).Normalized();
		var planePosition = siteA + (siteB - siteA) * 0.5f;
		var plane = new Plane(planeNormal, planePosition);

		MeshDataTool dataTool = new();
		dataTool.CreateFromSurface(node.meshInstance.Mesh as ArrayMesh, 0);

		SurfaceTool surfA = new();
		surfA.Begin(Mesh.PrimitiveType.Triangles);
		// surfA.SetMaterial(node.GetOverrideMaterial());
		surfA.SetSmoothGroup(UInt32.MaxValue);

		SurfaceTool surfB = new();
		surfB.Begin(Mesh.PrimitiveType.Triangles);
		// surfB.SetMaterial(node.GetOverrideMaterial());
		surfB.SetSmoothGroup(UInt32.MaxValue);
		
		// GENERATE SUB MESHES
		// ITERATE OVER EACH FACE OF THE BASE MESH
		// 2 iterations for 2 sub meshes (above/below)
		for (int side = 0; side < 2; side++)
		{
			SurfaceTool surfaceTool = new();
			surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
			// surfaceTool.SetMaterial(node.meshInstance.GetActiveMaterial(0));
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
					if (intersects[0] is null || intersects[1] is null) { GD.Print("intersects is null (?)"); }
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

		MeshInstance3D meshUp = new()
		{
			Mesh = surfA.Commit()
		};
		node.left = new VSTNode(meshUp, node.ID * 2, node, node.level + 1, Laterality.LEFT, endPoint);
		node.PermanentLeft.Value = node.left;

		MeshInstance3D meshDown = new()
		{
			Mesh = surfB.Commit()
		};
		node.right = new VSTNode(meshDown, node.ID * 2 + 1, node, node.level + 1, Laterality.RIGHT, endPoint);
		node.PermanentRight.Value = node.right;

		return true;
	}

	/// <summary>
	/// creates a Destronoi Node from the given meshInstance and vstnode
	/// </summary>
	public DestronoiNode CreateDestronoiNode(VSTNode subVST,
											MeshInstance3D subVSTmeshInstance,
											String name,
											StandardMaterial3D material)
	{
		// a newly created node has no parent (but we leave its permanentParent alone of course, so it can be reset if this node is recreated / unfragmented later on)
		subVST.parent = null;

		MeshInstance3D meshInstanceToSet = subVSTmeshInstance;
		meshInstanceToSet.Name = $"{name}_MeshInstance3D";

		DestronoiNode destronoiNode = new(
			inputName: name,
			inputGlobalTransform: this.GlobalTransform,
			inputMeshInstance: meshInstanceToSet,
			inputFragmentContainer: this.fragmentContainer,
			inputVSTRoot: subVST,
			inputDensity: baseObjectDensity,
			inputNeedsInitialising: false,
			inputBinaryTreeMapToActiveNodes: this.binaryTreeMapToActiveNodes
		);

		return destronoiNode;
	}





















	// --- functions below here are deprecated --- //


	public void Destroy(int depth, float combustVelocity = 0f)
	{
		List<VSTNode> leaves = [];
		VSTNode.GetLeafNodes(vstRoot, depth, outArr: leaves);

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

			body.Mass = baseObjectDensity * meshInstance.Mesh.GetAabb().Volume;

			// needed (idk why lmao ?) for detecting explosions from RPGs
			body.ContactMonitor = true;
			body.MaxContactsReported = 5_000;

			return body;
	}
}
