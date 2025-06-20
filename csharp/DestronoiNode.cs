using Godot;
using System.Collections.Generic;
using System;

/// <summary>
/// Subdivides a convex ArrayMesh belonging to a RigidBody3D by generating a Voronoi Subdivision Tree (VST).
/// </summary>
public partial class DestronoiNode : Node
{
    // The root node of the VST.
    private VSTNode vstRoot;

    // Generates 2^n fragments, where n is treeHeight.
    [Export(PropertyHint.Range, "1,8")] public int treeHeight { get; set; } = 1;

    [Export] public MeshInstance3D meshInstance { get; set; }
    [Export] public Node fragmentContainer { get; set; }
    [Export] public RigidBody3D baseObject { get; set; }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Input.IsActionJustPressed("debug_explode"))
        {
            Destroy(2, 2, 1f);
        }
    }

    public override void _Ready()
    {
        if (meshInstance == null)
        {
            GD.Print("[Destronoi] No MeshInstance3D set");
            return;
        }

        vstRoot = new VSTNode(meshInstance);

        // Plot 2 sites for the subdivision
        PlotSitesRandom(vstRoot);
        // Generate 2 children from the root
        Bisect(vstRoot);
        // https://msdn.microsoft.com/query/roslyn.query?appId=roslyn&k=k(CS0246)Perform additional subdivisions depending on tree height
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
        node._sites = new List<Vector3> { node._meshInstance.Position + site1, node._meshInstance.Position + site2 };
    }

    public void PlotSitesRandom(VSTNode node)
    {
        node._sites = [];
        var mdt = new MeshDataTool();
        mdt.CreateFromSurface(node._meshInstance.Mesh as ArrayMesh, 0);

        var aabb = node._meshInstance.GetAabb();
        var min = aabb.Position;
        var max = aabb.End;
        float avgX = (min.X + max.X) * 0.5f;
        float avgY = (min.Y + max.Y) * 0.5f;
        float avgZ = (min.Z + max.Z) * 0.5f;

        float dev = 0.1f;
        while (node._sites.Count < 2)
        {
            var site = new Vector3(
                (float)GD.Randfn(avgX, dev),
                (float)GD.Randfn(avgY, dev),
                (float)GD.Randfn(avgZ, dev)
            );

            int intersections = 0;
            for (int t = 0; t < mdt.GetFaceCount(); t++)
            {
                int v0 = mdt.GetFaceVertex(t, 0);
                int v1 = mdt.GetFaceVertex(t, 1);
                int v2 = mdt.GetFaceVertex(t, 2);
                var p0 = mdt.GetVertex(v0);
                var p1 = mdt.GetVertex(v1);
                var p2 = mdt.GetVertex(v2);
                Variant? ip = Geometry3D.RayIntersectsTriangle(site, Vector3.Up, p0, p1, p2);
                if (ip != null)
                    intersections++;
            }

            if (intersections == 1)
                node._sites.Add(site);
        }
    }

    public bool Bisect(VSTNode node)
    {
        if (node.GetSiteCount() != 2)
            return false;

        var a = node._sites[0];
        var b = node._sites[1];
        var normal = (b - a).Normalized();
        var pos = a + (b - a) * 0.5f;
        var plane = new Plane(normal, pos);

        var data = new MeshDataTool();
        data.CreateFromSurface(node._meshInstance.Mesh as ArrayMesh, 0);

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
            st.SetMaterial(node._meshInstance.GetActiveMaterial(0));
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
        node._left = new VSTNode(meshUp, node._level + 1, Laterality.LEFT);

        var meshDown = new MeshInstance3D { Mesh = (ArrayMesh)surfB.Commit() };
        node._right = new VSTNode(meshDown, node._level + 1, Laterality.RIGHT);

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
            body.Transform = baseObject.Transform;

            var mi = leaf._meshInstance;
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
