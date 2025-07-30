using Godot;
using System.Collections.Generic;
using System;
using System.Linq;

namespace Destruct3D;

/// <summary>
/// Subdivides a convex ArrayMesh belonging to a RigidBody3D by generating a Voronoi Subdivision Tree (VST).
/// </summary>
[GlobalClass]
public partial class DestructibleBody3D : RigidBody3D
{
	// --- Exports --- //

	/// <summary>this meshInstance (which is also set in CreateDestronoiNode) is the actual meshInstance of the instantiated rigidbody. ie its the sum of all relevant endPoints / nodes with unchanged children</summary>
	[Export] public MeshInstance3D meshInstance;
	/// <summary>node under which fragments will be instanced</summary>
	[Export] public Node fragmentContainer;
	/// <summary>Generates 2^n fragments, where n is treeHeight.</summary>
	[Export] public int treeHeight = 8;

	/// <summary>for now, this is the material set on all faces of any fragments (parent or child) made from a destructibleBody3D</summary>
	/// <remarks> rn the implementation is not that ideal as faces seemingly on the original object will still be changed to this. this is public as its used in CreateFreshDestronoiNode, which references (a random child of the VST)'s fragmentMaterial</remarks>
	[Export] public Material fragmentMaterial;
	/// <summary>
	/// <para>set this to true if you want the original mesh texture to be applied to the relevant fragments, and the fragmentMaterial to be applied to interior faces of the object</para>
	/// <para>set this to be false if you have an object which you want every face (interior, exterior, fragmented, etc) to be the same uniform material (e.g. you just want a completely red object) or if your material is seamless</para>
	/// </summary>
	/// <remarks>setting this to false saves some computation, and you may get better performance when producing a lot of fragments</remarks>
	[Export] public bool hasTexturedMaterial = true;
	/// <summary>only used if hasTexturedMaterial is false</summary>
	public Material originalUntexturedMaterial;
	public MaterialRegistry materialRegistry;
	public ShaderMaterial shaderMaterial;
	[Export] private NodePath CUSTOM_MATERIAL_SHADER_PATH = "res://addons/CDestronoi-Submodule/Destruct3D/materials/CustomMaterials.gdshader";
	private NodePath  CUSTOM_MATERIAL_SHADER_RELATIVE_PATH = "/../materials/CustomMaterials.gdshader";
	// this must agree with the shader, specifically "uniform vec3 exteriorSurfaceNormals[100];"
	private const int MAX_NUMBER_OF_EXTERIOR_SURFACES = 100;
	public readonly float TextureScale = 10.0f;

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
	private const float MIN_OBJECT_MASS_KILOGRAMS = 0.01f;
	// private const int MAX_CONTACTS_REPORTED = 5_000;

	[Export] public bool treatTopMostLevelAsStatic = false;

	// particle effects
	// the path of the particle_effects .tscn relative to the path of this script.
	// this is probably fragile. The reason I'm doing this is so I can still use a submodule of this repo, without breaking PackedScene loadings. (if there's a better method feel free to make a github issue)
	public NodePath CUSTOM_PARTICLE_EFFECTS_SCENE_RELATIVE_PATH = "/../particle_effects/DisintegrationParticleEffects.tscn";

	// required for godot
	public DestructibleBody3D() { }

	public DestructibleBody3D(	string inputName,
							Transform3D inputGlobalTransform,
							MeshInstance3D inputMeshInstance,
							Node inputFragmentContainer,
							VSTNode inputVSTRoot,
							float inputDensity,
							bool inputNeedsInitialising,
							BinaryTreeMapToActiveNodes inputBinaryTreeMapToActiveNodes,
							ShaderMaterial inputShaderMaterial,
							Material inputOriginalUntexturedMaterial,
							bool inputHasTexturedMaterial,
							bool inputTreatTopMostLevelAsStatic)
	{
		Name = inputName;
		GlobalTransform = inputGlobalTransform;

		meshInstance = (MeshInstance3D)inputMeshInstance.Duplicate();
		AddChild(meshInstance);

		CollisionShape3D shape = new()
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
		Mass = Math.Max(baseObjectDensity * volume, MIN_OBJECT_MASS_KILOGRAMS);

		// setting this to true will break everything. this flag must be false as the vstRoot is being reused and must not be regenerated for fragments. it should only be used when the scene is being loaded
		needsInitialising = inputNeedsInitialising;

		binaryTreeMapToActiveNodes = inputBinaryTreeMapToActiveNodes;

		// needed for detecting explosions from moving objects - okay idk why this is here
		// ContactMonitor = true;
		// MaxContactsReported = MAX_CONTACTS_REPORTED;

		LinearDamp = LINEAR_DAMP;
		AngularDamp = ANGULAR_DAMP;

		// material stuff

		hasTexturedMaterial = inputHasTexturedMaterial;

		if (hasTexturedMaterial)
		{
			shaderMaterial = inputShaderMaterial;
			meshInstance.Mesh.SurfaceSetMaterial(0, inputShaderMaterial);
		}
		else
		{
			originalUntexturedMaterial = inputOriginalUntexturedMaterial;
			meshInstance.Mesh.SurfaceSetMaterial(0, originalUntexturedMaterial);
		}

		if (vstRoot.level == 0 && treatTopMostLevelAsStatic)
		{
			Freeze = true;
			FreezeMode = FreezeModeEnum.Kinematic;
		}
	}
	
	// --- godot specific implementation --- //

	public override void _Ready()
	{
		base._Ready();

		if (!needsInitialising)
		{
			return;
		}

		// meshInstance.Mesh.SetLocalToScene(true);
		// meshInstance.Mesh.SurfaceGetMaterial(0).SetLocalToScene(true);

		// --- do some error checks, but only after children have been added to scene i.e. we wait one frame --- //

		CallDeferred(nameof(ErrorChecks));

		// --- create filled VST --- //

		if (meshInstance is null)
		{
			GD.PushError("[Destronoi] No MeshInstance3D set");
			return;
		}

		// the topmost node has no parent hence null & no laterality & not endPoint
		vstRoot = new VSTNode(	inputMeshInstance: (MeshInstance3D)meshInstance.Duplicate(),
								inputID: 1,
								inputParent: null,
								inputLevel: 0,
								inputLaterality: Laterality.NONE,
								inputEndPoint: false);

		// Perform subdivisions, depending on tree height
		for (int depthIndex = 0; depthIndex < treeHeight; depthIndex++)
		{
			List<VSTNode> leaves = [];
			VSTNode.GetLeafNodes(vstRoot, leaves);
			
			foreach (VSTNode leaf in leaves)
			{
				PlotSitesRandom(leaf);

				// on final pass, set children as endPoints
				if (depthIndex == treeHeight - 1)
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

		// needed for detecting explosions from RPGs - okay idk why this is here
		// ContactMonitor = true;
		// MaxContactsReported = 5_000;

		LinearDamp = LINEAR_DAMP;
		AngularDamp = ANGULAR_DAMP;

		if (treatTopMostLevelAsStatic)
		{
			Freeze = true;
			FreezeMode = FreezeModeEnum.Kinematic;
		}

		// --- material stuff only --- //

		if (!hasTexturedMaterial)
		{
			originalUntexturedMaterial = meshInstance.GetActiveMaterial(0);
			return;
		}

		/// new() VSTNode converts its mesh into an ArrayMesh already
		materialRegistry = new(vstRoot.meshInstance.Mesh as ArrayMesh, meshInstance.GetActiveMaterial(0));

		// create the shader material which will be used by all children of this destructibleBody3D
		shaderMaterial = new()
		{
			Shader = ResourceLoader.Load<Shader>(((Resource)GetScript()).ResourcePath + CUSTOM_MATERIAL_SHADER_RELATIVE_PATH)
		};

		Vector3[] exteriorSurfaceNormals = new Vector3[MAX_NUMBER_OF_EXTERIOR_SURFACES];
		
		int i = 0;
		foreach ( var (normal, _) in materialRegistry.normalToUVMap)
		{
			exteriorSurfaceNormals[i] = normal;
			i++;
		}

		shaderMaterial.SetShaderParameter("exteriorSurfaceNormals", exteriorSurfaceNormals);

		Texture2D exteriorTexture = (meshInstance.GetActiveMaterial(0) as StandardMaterial3D).AlbedoTexture;
		shaderMaterial.SetShaderParameter("exteriorSurfaceMaterial", exteriorTexture);
		
		Texture2D interiorTexture = (fragmentMaterial as StandardMaterial3D).AlbedoTexture;
		shaderMaterial.SetShaderParameter("interiorSurfaceMaterial", interiorTexture);

		Color albedoColor = (meshInstance.GetActiveMaterial(0) as StandardMaterial3D).AlbedoColor;
		shaderMaterial.SetShaderParameter("albedo_multiplier", albedoColor);
		
		// this is a bodge that means that meshInstance.Mesh does NOT have to be made unique between objects
		// if we didn't duplicate this, then if you've accidentally shared a mesh between objects, it would set the material to be the shader material for both objects, then fail on the _Ready() of the 2nd object
		meshInstance.Mesh = (Mesh)meshInstance.Mesh.Duplicate();

		meshInstance.Mesh.SurfaceSetMaterial(0, shaderMaterial);
	}

	/// <summary>
	/// error checks for the ready function. probably should just be enforced in the editor but idk how to do that
	/// </summary>
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

	/// <summary>
	/// adds 2 vector3's to the <paramref name="node"/>'s .sites list, where both points are within the <paramref name="node"/>'s meshInstance
	/// </summary>
	/// <remarks>
	/// uniformly randomly samples a point within the AABB of the meshInstance, then tests for inclusion in the meshInstance using a raycast. Maybe there's a quicker way, but this way is uniformly random within the mesh too (i.e. any subvolume of a uniformly sampled volume will also in turn be uniformly sampled) and I can't think of a simple performant alternative that achieves this too.
	/// </remarks>
	public void PlotSitesRandom(VSTNode node)
	{
		node.sites = [];

		MeshDataTool mdt = new();

		if (node.meshInstance.Mesh is not ArrayMesh arrayMesh)
		{
			GD.PushError("arraymesh must be passed to plotsitesrandom, not any other type of mesh");
			return;
		}

		if (arrayMesh.GetSurfaceCount() == 0)
		{
			GD.PushWarning($"Mesh has no surfaces (surface count = 0), cannot run PlotSitesRandom on this node, returning early. node that has this meshinstance has ID {node.ID} and level {node.level}");
			return;
		}

		mdt.CreateFromSurface(arrayMesh, 0);

		if (mdt.GetFaceCount() == 0)
		{
			GD.PushWarning("no faces found in meshdatatool, plotsitesrandom will loop forever. returning early");
			return;
		}

		int tries = 0;

		Vector3 direction = Vector3.Up;

		Aabb aabb = node.meshInstance.GetAabb();

		while (node.sites.Count < 2)
		{
			tries++;

			if (tries > MAX_PLOTSITERANDOM_TRIES)
			{
				GD.PushWarning($"over {MAX_PLOTSITERANDOM_TRIES} tries exceeded, exiting PlotSitesRandom");
				return;
			}

			Vector3 site = new(
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
				Vector3 p0 = mdt.GetVertex(v0);
				Vector3 p1 = mdt.GetVertex(v1);
				Vector3 p2 = mdt.GetVertex(v2);

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

	/// <summary>
	/// splits the input <paramref name="node"/>'s mesh into 2, and populates the input <paramref name="node"/>'s left and right fields with 2 new VSTNodes which represent those 2 "halves"
	/// </summary>
	/// <returns>a bool but idk why lmao</returns>
	public static bool Bisect(VSTNode node, bool endPoint)
	{
		if (node.GetSiteCount() != 2)
		{
			return false;
		}

		Vector3 siteA = node.sites[0];
		Vector3 siteB = node.sites[1];
		Vector3 planeNormal = (siteB - siteA).Normalized();
		Vector3 planePosition = siteA + (siteB - siteA) * 0.5f;
		Plane plane = new(planeNormal, planePosition);

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

			for (int faceIndex = 0; faceIndex < dataTool.GetFaceCount(); faceIndex++)
			{
				int[] faceVertices = [	dataTool.GetFaceVertex(faceIndex, 0),
										dataTool.GetFaceVertex(faceIndex, 1),
										dataTool.GetFaceVertex(faceIndex, 2)	];
				List<int> verticesAbovePlane = [];
				List<Vector3?> intersects = [];

				// ITERATE OVER EACH VERTEX AND DETERMINE "ABOVENESS"
				foreach (int vertexIndex in faceVertices)
				{
					if (plane.IsPointOver(dataTool.GetVertex(vertexIndex)))
					{
						verticesAbovePlane.Add(vertexIndex);
					}
				}

				// INTERSECTION CASE 0/0.5: ALL or NOTHING above the plane
				if (verticesAbovePlane.Count == 0)
				{
					continue;
				}
				if (verticesAbovePlane.Count == 3)
				{
					// foreach (int vid in faceVertices)
					// {
					// 	AddVertexAndUV(surfaceTool, dataTool.GetVertex(vid));
					// }
					MeshPruning.AddFaceWithProjectedUVs(surfaceTool,
											dataTool.GetVertex(faceVertices[0]),
											dataTool.GetVertex(faceVertices[1]),
											dataTool.GetVertex(faceVertices[2]));
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

					if (!intersectionAfter.HasValue || !intersectionBefore.HasValue)
					{
						GD.PushWarning($"[CDestronoi] one or more intersects has no value. skipping this face. (from bisecting node with ID {node.ID} and level {node.level})");
						continue;
					}

					intersects.Add(intersectionAfter);
					intersects.Add(intersectionBefore);
					coplanar.Add(intersectionAfter);
					coplanar.Add(intersectionBefore);

					// TRIANGLE CREATION
					// AddVertexAndUV(surfaceTool, dataTool.GetVertex(vid));
					// AddVertexAndUV(surfaceTool, (Vector3)intersects[0]);
					// AddVertexAndUV(surfaceTool, (Vector3)intersects[1]);
					MeshPruning.AddFaceWithProjectedUVs(surfaceTool, dataTool.GetVertex(vid), (Vector3)intersects[0], (Vector3)intersects[1]);
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
					else if (	verticesAbovePlane[0] != faceVertices[0] &&
								verticesAbovePlane[1] != faceVertices[0])
					{
						indexRemaining = 0;
					}
					else
					{
						indexRemaining = 2;
					}

					Vector3? intersectionAfter = plane.IntersectsSegment(
						dataTool.GetVertex(verticesAbovePlane[1]),
						dataTool.GetVertex(faceVertices[indexRemaining]));

					Vector3? intersectionBefore = plane.IntersectsSegment(
						dataTool.GetVertex(verticesAbovePlane[0]),
						dataTool.GetVertex(faceVertices[indexRemaining]));

					if (!intersectionAfter.HasValue || !intersectionBefore.HasValue)
					{
						GD.PushWarning($"[CDestronoi] one or more intersects has no value. skipping this face. (from bisecting node with ID {node.ID} and level {node.level})");
						continue;
					}

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
					// AddVertexAndUV(surfaceTool, dataTool.GetVertex(verticesAbovePlane[0]));
					// AddVertexAndUV(surfaceTool, dataTool.GetVertex(verticesAbovePlane[1]));
					// AddVertexAndUV(surfaceTool, (Vector3)intersects[indexShortest]);
					MeshPruning.AddFaceWithProjectedUVs(surfaceTool, dataTool.GetVertex(verticesAbovePlane[0]), dataTool.GetVertex(verticesAbovePlane[1]), (Vector3)intersects[indexShortest]);

					// TRIANGLE 2
					// AddVertexAndUV(surfaceTool, (Vector3)intersects[0]);
					// AddVertexAndUV(surfaceTool, (Vector3)intersects[1]);
					// AddVertexAndUV(surfaceTool, dataTool.GetVertex(verticesAbovePlane[indexShortest]));
					MeshPruning.AddFaceWithProjectedUVs(surfaceTool, (Vector3)intersects[0], (Vector3)intersects[1], dataTool.GetVertex(verticesAbovePlane[indexShortest]));
					continue;
				}
			}
			// END for face in range(data_tool.get_face_count())

			// cap polygon
			Vector3 center = Vector3.Zero;
			foreach (Vector3 v in coplanar.Select(v => (Vector3)v))
			{
				center += v;
			}
			center /= coplanar.Count;
			for (int i = 0; i < coplanar.Count - 1; i += 2)
			{
				// AddVertexAndUV(surfaceTool, (Vector3)coplanar[i + 1]);
				// AddVertexAndUV(surfaceTool, (Vector3)coplanar[i]);
				// AddVertexAndUV(surfaceTool, center);
				MeshPruning.AddFaceWithProjectedUVs(surfaceTool, (Vector3)coplanar[i + 1], (Vector3)coplanar[i], center);
			}

			if (side == 0)
			{
				surfA = surfaceTool;
			}
			else
			{
				surfB = surfaceTool;
			}
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

	// public static void AddVertexAndUV(SurfaceTool surfaceTool, Vector3 vertex)
	// {
	// 	surfaceTool.SetUV(new Vector2(vertex.X, vertex.Z));
	// 	surfaceTool.AddVertex(vertex);
	// }

	/// <summary>
	/// creates a Destronoi Node from the given meshInstance and vstnode
	/// </summary>
	public DestructibleBody3D CreateDestructibleBody3D(VSTNode subVST,
											MeshInstance3D subVSTmeshInstance,
											String name,
											StandardMaterial3D material)
	{
		// a newly created node has no parent (but we leave its permanentParent alone of course, so it can be reset if this node is recreated / unfragmented later on)
		subVST.parent = null;

		MeshInstance3D meshInstanceToSet = subVSTmeshInstance;
		meshInstanceToSet.Name = $"{name}_MeshInstance3D";

		DestructibleBody3D destructibleBody3D = new(
			inputName: name,
			inputGlobalTransform: this.GlobalTransform,
			inputMeshInstance: meshInstanceToSet,
			inputFragmentContainer: this.fragmentContainer,
			inputVSTRoot: subVST,
			inputDensity: baseObjectDensity,
			inputNeedsInitialising: false,
			inputBinaryTreeMapToActiveNodes: this.binaryTreeMapToActiveNodes,
			inputShaderMaterial: this.shaderMaterial,
			inputOriginalUntexturedMaterial: this.originalUntexturedMaterial,
			inputHasTexturedMaterial: this.hasTexturedMaterial,
			inputTreatTopMostLevelAsStatic: this.treatTopMostLevelAsStatic
		);

		return destructibleBody3D;
	}

	/// <summary>
	/// removes a destructibleBody3D from the scene safely
	/// </summary>
	/// <remarks>
	/// this function queuefrees() the meshInstance child of the destructibleBody3D, but that is a duplicate of the meshInstance from the VST (see paramaterised constructor above), so we aren't removing information from the VST.
	/// </remarks>
	public void Deactivate()
	{
		Visible = false;
		Freeze = true;
		CollisionLayer = 0;
		CollisionMask = 0;
		Sleeping = true;

		SetProcessUnhandledInput(false);

		binaryTreeMapToActiveNodes.RemoveFromActiveTree(this);

		QueueFree();
	}
}
