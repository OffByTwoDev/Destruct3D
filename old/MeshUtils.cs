using Godot;

public static class MeshUtils
{
	public static bool AreMeshVerticesEqual(ArrayMesh mesh, int surfaceIndex1, int surfaceIndex2)
	{
		// Get the arrays describing the surfaces
		var arrays1 = mesh.SurfaceGetArrays(surfaceIndex1);
		var arrays2 = mesh.SurfaceGetArrays(surfaceIndex2);

		// Get vertex arrays
		var verts1 = (Godot.Collections.Array)arrays1[(int)Mesh.ArrayType.Vertex];
		var verts2 = (Godot.Collections.Array)arrays2[(int)Mesh.ArrayType.Vertex];

		if (verts1.Count != verts2.Count)
			return false;

		for (int i = 0; i < verts1.Count; i++)
		{
			Vector3 v1 = (Vector3)verts1[i];
			Vector3 v2 = (Vector3)verts2[i];

			if (!v1.IsEqualApprox(v2))
				return false;
		}

		return true;
	}
}
