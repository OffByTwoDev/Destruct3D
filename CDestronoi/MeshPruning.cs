using Godot;
using System.Collections.Generic;
using System;

namespace CDestronoi;

// notes: currently meshes arent consistently oriented
// and ofc like the original outside material isnt kept at all 

// issues
// subfaces from old fragments are not kept
// weird edge highlight thing on some fragments?
// also organise
// slow af

public static class MeshPruning
{
    private const float TextureScale = 40.0f;

	/// <summary>
	/// tangents need to be generated if the material is normal mapped. set this flag to true to enforce tangents to be set when calculating new fragments
	/// </summary>
	// private const bool MaterialIsNormalMapped = false;

	/// <summary>
	/// takes in a list of meshes and combines them into 1 mesh. if <paramref name="texturedMaterial"/> is true, then UVs are mapped correctly so a non uniform texture can be displayed.
	/// </summary>
    public static MeshInstance3D CombineMeshesAndPrune( List<MeshInstance3D> meshInstances,
														bool texturedMaterial,
														MaterialRegistry materialRegistry,
														Material fragmentMaterial)
	{
		ArrayMesh combinedMesh = CombineMeshes(meshInstances).Mesh as ArrayMesh;

		// remove interior surfaces from mesh (which doesn't actually seem necessary lmao)
		// ArrayMesh prunedCombinedMesh = RemoveInteriorFaces(combinedMesh);

		if (texturedMaterial)
		{
			ArrayMesh UVGroupedMesh = CombineFaceGroupUVs(combinedMesh, materialRegistry, fragmentMaterial);

			return new MeshInstance3D
			{
				Mesh = UVGroupedMesh
			};
		}
		else
		{
			return new MeshInstance3D
			{
				Mesh = combinedMesh
			};
		}
	}

	private static ArrayMesh CombineFaceGroupUVs(ArrayMesh arrayMesh, MaterialRegistry materialRegistry, Material fragmentMaterial)
	{
		MeshDataTool mdt = new();
		mdt.CreateFromSurface(arrayMesh, 0);

		List<List<int>> faceGroups = GroupParallelFaces(arrayMesh);

		// GD.Print($"faceGroups[0].Count = {faceGroups[0].Count}");

		SurfaceTool surfaceTool = new();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
		surfaceTool.SetSmoothGroup(UInt32.MaxValue);

		foreach (List<int> faceGroup in faceGroups)
		{
			int firstFaceIndex = faceGroup[0];

			Vector3 firstVertex = mdt.GetVertex(mdt.GetFaceVertex(firstFaceIndex, 0));
			Vector3 secondVertex = mdt.GetVertex(mdt.GetFaceVertex(firstFaceIndex, 1));
			Vector3 thirdVertex = mdt.GetVertex(mdt.GetFaceVertex(firstFaceIndex, 2));

			// Create local basis (u, v) for face group
			Vector3 normal = (secondVertex - firstVertex).Cross(thirdVertex - firstVertex).Normalized();
			Vector3 tangent = (secondVertex - firstVertex).Normalized();
			Vector3 bitangent = normal.Cross(tangent).Normalized();
			Vector3 origin = firstVertex;

			foreach (int faceIndex in faceGroup)
			{
				Vector3 v1 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 0));
				Vector3 v2 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 1));
				Vector3 v3 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 2));

				AddFaceWithProjectedUVsToSpecifiedUV(surfaceTool, v1, v2, v3, origin, normal, tangent, bitangent);
			}
		}

		surfaceTool.GenerateNormals();
		surfaceTool.GenerateTangents();

		ArrayMesh meshWithGroupedUVs = surfaceTool.Commit();

		return meshWithGroupedUVs;
	}

	// function to group faces into parallel-surfaces / face-groups
	private static List<List<int>> GroupParallelFaces(ArrayMesh arrayMesh)
	{
		MeshDataTool mdt = new();
		mdt.CreateFromSurface(arrayMesh, 0);

		List<Vector3> faceCenters = [];
		List<Vector3> faceNormals = [];

		// GD.Print($"original face count: {mdt.GetFaceCount()}");

		for (int faceIndex = 0; faceIndex < mdt.GetFaceCount(); faceIndex++)
		{
			Vector3 a = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 0));
			Vector3 b = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 1));
			Vector3 c = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 2));

			Vector3 center = (a + b + c) / 3.0f;
			faceCenters.Add(center);

			Vector3 normal = ((b - a).Cross(c - a)).Normalized();
			faceNormals.Add(normal);
		}

		List<int> FirstFaceGroup = [0];
		List<List<int>> FaceGroups = [FirstFaceGroup];

		for (int faceIndexToGroup = 1; faceIndexToGroup < mdt.GetFaceCount(); faceIndexToGroup++)
		{
			bool addedToGroup = false;

			foreach (List<int> faceGroup in FaceGroups)
			{
				foreach (int faceIndex in faceGroup)
				{
					if (IsApproxSameDirectionSimplified( faceNormals[faceIndexToGroup], faceNormals[faceIndex]))
					{
						faceGroup.Add(faceIndexToGroup);
						addedToGroup = true;
						break;
					}
				}

				if (addedToGroup)
				{
					break;
				}
			}

			if (!addedToGroup)
			{
				List<int> newFaceGroup = [faceIndexToGroup];
				FaceGroups.Add(newFaceGroup);
			}
		}

		return FaceGroups;
	}

	private static bool IsApproxSameDirectionSimplified(Vector3 firstVector, Vector3 secondVector)
	{
		var ThresholdAngleRadians = 0.01f;

		if (firstVector.AngleTo(secondVector) < ThresholdAngleRadians)
		{
			return true;
		}

		return false;
	}

	private static void AddFaceWithProjectedUVsToSpecifiedUV(SurfaceTool surfaceTool,
															Vector3 v1,
															Vector3 v2,
															Vector3 v3,
															Vector3 origin,
															Vector3 normal,
															Vector3 tangent,
															Vector3 bitangent)
	{
        // Function to convert 3D point to 2D UV in triangle plane
        Vector2 GetUV(Vector3 p)
		{
			Vector3 local = p - origin;
			return new Vector2(local.Dot(tangent), local.Dot(bitangent)) * new Vector2(1.0f/TextureScale, 1.0f/TextureScale);
		}

		// Set data for each vertex
		surfaceTool.SetUV(GetUV(v1));
		surfaceTool.AddVertex(v1);

		surfaceTool.SetUV(GetUV(v2));
		surfaceTool.AddVertex(v2);

		surfaceTool.SetUV(GetUV(v3));
		surfaceTool.AddVertex(v3);
	}












	// --- simplified components that are still used --- //

	public static MeshInstance3D CombineMeshes(List<MeshInstance3D> meshInstances)
	{
		var surfaceTool = new SurfaceTool();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

		foreach (MeshInstance3D meshInstance in meshInstances)
		{
			Mesh mesh = meshInstance.Mesh;

			if (mesh is null)
			{
				continue;
			}

			// Append all surfaces from this mesh with the MeshInstance's transform
			for (int surfaceIndex = 0; surfaceIndex < mesh.GetSurfaceCount(); surfaceIndex++)
			{
				surfaceTool.AppendFrom(mesh, surfaceIndex, meshInstance.Transform);
			}
		}

		ArrayMesh combinedArrayMesh = surfaceTool.Commit();

		return new MeshInstance3D
		{
			Mesh = combinedArrayMesh
		};
	}


	/// <summary>
	/// v1 is the 1st vertex (of the triangle), v2 is the 2nd vertex, v3 is the 3rd vertex.
	/// </summary>
	/// <remarks>it might improve performance to not set UVs here but it might actually be slower.
	/// depends on the frequency with which !childrenChanged fragments are instantiated (and if thats non negligible it would be worth calculating correct UVs normals etc for all meshes in the VST, and then using them at runtime instead of calculating stuff on the fly). But i suspect that its probably not worth it as a significant amount of time the meshes we are instantiating are probably combined from more than 1 VST mesh anyways.</remarks>
	public static void AddFaceWithProjectedUVs(SurfaceTool surfaceTool, Vector3 v1, Vector3 v2, Vector3 v3)
	{
		// Compute face normal
		Vector3 normal = (v2 - v1).Cross(v3 - v1).Normalized();

		// Create local basis (u, v) on triangle's plane
		Vector3 tangent = (v2 - v1).Normalized();
		Vector3 bitangent = normal.Cross(tangent).Normalized();

		// Origin for local UV space
		Vector3 origin = v1;

		// Function to convert 3D point to 2D UV in triangle plane
		Vector2 GetUV(Vector3 p)
		{
			Vector3 local = p - origin;
			return new Vector2(local.Dot(tangent), local.Dot(bitangent)) * new Vector2(1/4.0f, 1/4.0f);
		}

		// Set data for each vertex

		surfaceTool.SetUV(GetUV(v1));
		surfaceTool.AddVertex(v1);

		surfaceTool.SetUV(GetUV(v2));
		surfaceTool.AddVertex(v2);

		surfaceTool.SetUV(GetUV(v3));
		surfaceTool.AddVertex(v3);
	}













	/// --- currently unused --- ///
	
	public static bool IsApproxSameDirection(Vector3 firstVector, Vector3 secondVector)
	{
		var ThresholdAngleRadians = 0.01f;
		var cosThresholdAngleRadians = Math.Cos(ThresholdAngleRadians);

		var normalisedFirstVector = firstVector.Normalized();
		var normalisedSecondVector = secondVector.Normalized();

		var cosAngleBetweenVectorsRadians = normalisedFirstVector.Dot(normalisedSecondVector);

		if (cosAngleBetweenVectorsRadians > cosThresholdAngleRadians)
		{
			return true;
		}

		return false;
	}

	public static ArrayMesh RemoveInteriorFaces(ArrayMesh arrayMesh)
	{
		MeshDataTool mdt = new();
		mdt.CreateFromSurface(arrayMesh, 0);

		List<Vector3> faceCenters = [];
		List<Vector3> faceNormals = [];

		// GD.Print($"original face count: {mdt.GetFaceCount()}");

		for (int faceIndex = 0; faceIndex < mdt.GetFaceCount(); faceIndex++)
		{
			Vector3 a = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 0));
			Vector3 b = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 1));
			Vector3 c = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 2));

			Vector3 center = (a + b + c) / 3.0f;
			faceCenters.Add(center);

			Vector3 normal = ((b - a).Cross(c - a)).Normalized();
			faceNormals.Add(normal);
		}

		int numberOfFacesToKeep = 0;

		SurfaceTool surfaceTool = new();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);
		surfaceTool.SetSmoothGroup(UInt32.MaxValue);

		for (int faceIndex = 0; faceIndex < mdt.GetFaceCount(); faceIndex++)
		{
			if (!FaceIsInterior(mdt, faceIndex, faceCenters, faceNormals))
			{
				Vector3 v1 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 0));
				Vector3 v2 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 1));
				Vector3 v3 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 2));

				AddFaceWithProjectedUVs(surfaceTool, v1, v2, v3);

				numberOfFacesToKeep++;
			}
		}

		GD.Print($"pruned face count: {numberOfFacesToKeep}");

		surfaceTool.GenerateNormals();
		surfaceTool.GenerateTangents();

		ArrayMesh prunedCombinedMesh = surfaceTool.Commit();

		return prunedCombinedMesh;
	}

	public static bool FaceIsInterior(MeshDataTool mdt, int faceIndexToCheck, List<Vector3> faceCenters, List<Vector3> faceNormals)
	{
		int numberOfIntersectionsInPositiveNormalDirection = 0;
		int numberOfIntersectionsInNegativeNormalDirection = 0;

		for (int faceIndex = 0; faceIndex < mdt.GetFaceCount(); faceIndex++)
		{
			if (faceIndex == faceIndexToCheck)
			{
				continue;
			}

			int v0 = mdt.GetFaceVertex(faceIndex, 0);
			int v1 = mdt.GetFaceVertex(faceIndex, 1);
			int v2 = mdt.GetFaceVertex(faceIndex, 2);
			Vector3 p0 = mdt.GetVertex(v0);
			Vector3 p1 = mdt.GetVertex(v1);
			Vector3 p2 = mdt.GetVertex(v2);

			// check positive direction
			Vector3 positiveNormal = faceNormals[faceIndexToCheck];
			Variant positiveIntersectionPoint =
				Geometry3D.RayIntersectsTriangle(faceCenters[faceIndexToCheck], positiveNormal, p0, p1, p2);
			
			if (positiveIntersectionPoint.VariantType != Variant.Type.Nil)
			{
				numberOfIntersectionsInPositiveNormalDirection++;
			}

			// check negative direction
			Vector3 negativeNormal = -1.0f * faceNormals[faceIndexToCheck];
			Variant negativeIntersectionPoint =
				Geometry3D.RayIntersectsTriangle(faceCenters[faceIndexToCheck], negativeNormal, p0, p1, p2);
			
			if (negativeIntersectionPoint.VariantType != Variant.Type.Nil)
			{
				numberOfIntersectionsInNegativeNormalDirection++;
			}
		}

		if (numberOfIntersectionsInNegativeNormalDirection > 0 && numberOfIntersectionsInPositiveNormalDirection > 0)
		{
			return true;
		}

		return false;
	}
}