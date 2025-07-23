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

public static class MeshPruning
{
	public static MeshInstance3D CombineMeshesAndPrune(List<MeshInstance3D> meshInstances)
	{
		ArrayMesh combinedMesh = VSTSplittingComponent.CombineMeshes(meshInstances).Mesh as ArrayMesh;

		// remove interior surfaces from mesh (which doesn't actually seem necessary lmao)
		// ArrayMesh prunedCombinedMesh = RemoveInteriorFaces(combinedMesh);

		ArrayMesh UVGroupedMesh = CombineFaceGroupUVs(combinedMesh);

		return new MeshInstance3D
		{
			Mesh = UVGroupedMesh
		};
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

	public static ArrayMesh RemoveInteriorFaces(ArrayMesh arrayMesh)
	{
		MeshDataTool mdt = new();
		mdt.CreateFromSurface(arrayMesh, 0);

		List<Vector3> faceCenters = [];
		List<Vector3> faceNormals = [];

		GD.Print($"original face count: {mdt.GetFaceCount()}");

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

		for (int faceIndex = 0; faceIndex < mdt.GetFaceCount(); faceIndex++)
		{
			if (!FaceIsInterior(mdt, faceIndex, faceCenters, faceNormals))
			{
				Vector3 v1 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 0));
				Vector3 v2 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 1));
				Vector3 v3 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 2));

				DestronoiNode.AddFaceWithProjectedUVs(surfaceTool, v1, v2, v3);

				numberOfFacesToKeep++;
			}
		}

		GD.Print($"pruned face count: {numberOfFacesToKeep}");

		ArrayMesh prunedCombinedMesh = surfaceTool.Commit();

		return prunedCombinedMesh;
	}

	public static ArrayMesh CombineFaceGroupUVs(ArrayMesh arrayMesh)
	{
		MeshDataTool mdt = new();
		mdt.CreateFromSurface(arrayMesh, 0);

		List<List<int>> faceGroups = GroupParallelFaces(arrayMesh);

		GD.Print($"faceGroups[0].Count = {faceGroups[0].Count}");

		SurfaceTool surfaceTool = new();
		surfaceTool.Begin(Mesh.PrimitiveType.Triangles);

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

		ArrayMesh meshWithGroupedUVs = surfaceTool.Commit();

		return meshWithGroupedUVs;
	}

	public static void AddFaceWithProjectedUVsToSpecifiedUV(SurfaceTool surfaceTool,
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
			return new Vector2(local.Dot(tangent), local.Dot(bitangent)) * new Vector2(1/40.0f, 1/40.0f);
		}

		// Set data for each vertex
		surfaceTool.SetNormal(normal);
		surfaceTool.SetUV(GetUV(v1));
		surfaceTool.AddVertex(v1);

		surfaceTool.SetNormal(normal);
		surfaceTool.SetUV(GetUV(v2));
		surfaceTool.AddVertex(v2);

		surfaceTool.SetNormal(normal);
		surfaceTool.SetUV(GetUV(v3));
		surfaceTool.AddVertex(v3);
	}

	// function to group faces into parallel-surfaces / face-groups
	public static List<List<int>> GroupParallelFaces(ArrayMesh arrayMesh)
	{
		MeshDataTool mdt = new();
		mdt.CreateFromSurface(arrayMesh, 0);

		List<Vector3> faceCenters = [];
		List<Vector3> faceNormals = [];

		GD.Print($"original face count: {mdt.GetFaceCount()}");

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

	public static bool IsApproxSameDirectionSimplified(Vector3 firstVector, Vector3 secondVector)
	{
		var ThresholdAngleRadians = 0.01f;

		if (firstVector.AngleTo(secondVector) < ThresholdAngleRadians)
		{
			return true;
		}

		return false;
	}
}