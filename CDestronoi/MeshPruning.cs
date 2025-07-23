using Godot;
using System.Collections.Generic;

namespace CDestronoi;

public static class MeshPruning
{
    public static MeshInstance3D CombineMeshesAndPrune(List<MeshInstance3D> meshInstances)
	{
		ArrayMesh combinedMesh = VSTSplittingComponent.CombineMeshes(meshInstances).Mesh as ArrayMesh;

		// remove interior surfaces from mesh (which doesn't actually seem necessary lmao)
		ArrayMesh prunedCombinedMesh = RemoveInteriorFaces(combinedMesh);

		return new MeshInstance3D
		{
			Mesh = prunedCombinedMesh
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

    // public static ArrayMesh CombineFaces(ArrayMesh arrayMesh)
    // {
        
    // }
}