using Godot;
using System.Collections.Generic;

namespace Destruct3D;

// there is no need to add this script to a node in the scene tree, its used for internal stuff (DestructibleBody3D creates instances of this class automatically, as necessary)

public struct UVwithSurface
{
	public Material material;
}

// defines a map from a face normal to a material & set of UV coordinates
public class MaterialRegistry
{
	public readonly Dictionary<Vector3, UVwithSurface> normalToUVMap = [];

	/// <summary>adds a given materials normals along with their corresponding materials to a registry</summary>
	public MaterialRegistry(ArrayMesh originalArrayMesh, Material originalMaterial)
	{
		MeshDataTool mdt = new();
		mdt.CreateFromSurface(originalArrayMesh, 0);

		for (int faceIndex = 0; faceIndex < mdt.GetFaceCount(); faceIndex++)
		{
			Vector3 v1 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 0));
			Vector3 v2 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 1));
			Vector3 v3 = mdt.GetVertex(mdt.GetFaceVertex(faceIndex, 2));

			Vector3 normal = (v2 - v1).Cross(v3 - v1).Normalized();

			// --- if normal is already a key in the map then continue --- //
			bool isNormalAlreadyStored = false;
			
			foreach (var (normalAlreadyInMap, _)  in normalToUVMap)
			{
				if (MeshPruning.IsApproxSameDirection(normal, normalAlreadyInMap))
				{
					isNormalAlreadyStored = true;
					break;
				}
			}
			if (isNormalAlreadyStored)
			{
				continue;
			}
			// --- //

			UVwithSurface uvWithSurface = new()
			{
				material = originalMaterial
			};

			normalToUVMap.Add(normal, uvWithSurface);
		}
	}

	public UVwithSurface? GetParentSurface(Vector3 normalToCheck)
	{
		foreach (var (normal, uvWithSurface) in normalToUVMap)
		{
			if (MeshPruning.IsApproxSameDirection(normal, normalToCheck))
			{
				return uvWithSurface;
			}
		}

		// no corresponding parent surface found, i.e. surface is a new surface and should be assigned the fragmentMaterial
		return null;
	}

}
