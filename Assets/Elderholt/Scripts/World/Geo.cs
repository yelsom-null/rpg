using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  Low-poly geometry + look helpers. The design's visual direction is
    //  flat-shaded low-poly (MeshLambertMaterial, flatShading) — so every mesh is
    //  "unwelded" (each triangle gets its own verts) and normals recalculated to
    //  get hard facets. Colours are the palette from the handoff.
    // ============================================================================
    public static class Geo
    {
        public static Color Hex(int rgb)
        {
            return new Color32(
                (byte)((rgb >> 16) & 0xff),
                (byte)((rgb >> 8) & 0xff),
                (byte)(rgb & 0xff),
                255);
        }

        // Palette (from the handoff's Visual Direction section).
        public static readonly Color Sky = Hex(0x9fc0d8);
        public static readonly Color Grass = Hex(0x6f9e4f);
        public static readonly Color Water = Hex(0x3d6f9e);
        public static readonly Color Sun = Hex(0xfff2d8);
        public static readonly Color HemiSky = Hex(0xd8e8f0);
        public static readonly Color HemiGround = Hex(0x4a5a38);
        public static readonly Color BirchTrunk = Hex(0xd8d2c0);
        public static readonly Color BirchCrown = Hex(0x7aa348);
        public static readonly Color Hill = Hex(0x7d7a70);
        public static readonly Color Rock = Hex(0x8a8a86);
        public static readonly Color Glint = Hex(0xc8a04a);
        public static readonly Color ShirtYou = Hex(0x7a4a2a);
        public static readonly Color ShirtFenn = Hex(0x3f6078);
        public static readonly Color Skin = Hex(0xd8a878);
        public static readonly Color Legs = Hex(0x4a4238);
        public static readonly Color Marker = Hex(0xf0d060);

        // Phase 2 — camp & underground palette.
        public static readonly Color Stone = Hex(0x6a675f);
        public static readonly Color ChamberFloor = Hex(0x55524a);
        public static readonly Color ChamberWall = Hex(0x484540);
        public static readonly Color Timber = Hex(0x8a6a3a);
        public static readonly Color Canopy = Hex(0xa04a3a);
        public static readonly Color Ember = Hex(0xd86a30);
        public static readonly Color Anvil = Hex(0x3a3a40);
        public static readonly Color Board = Hex(0x7a5a34);
        public static readonly Color GlintTin = Hex(0xb8c0c8);
        public static readonly Color GlintIron = Hex(0x9a5a48);

        static Shader lit;

        public static Material Mat(Color c)
        {
            if (lit == null) lit = Shader.Find("Standard");
            Material m = new Material(lit);
            m.color = c;
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
            return m;
        }

        // Append reversed windings so a generated surface is visible from both
        // sides (guards blind-authored meshes against a back-facing winding).
        public static int[] DoubleSided(IList<int> tris)
        {
            int n = tris.Count;
            int[] outT = new int[n * 2];
            for (int i = 0; i < n; i += 3)
            {
                outT[i] = tris[i]; outT[i + 1] = tris[i + 1]; outT[i + 2] = tris[i + 2];
                outT[n + i] = tris[i]; outT[n + i + 1] = tris[i + 2]; outT[n + i + 2] = tris[i + 1];
            }
            return outT;
        }

        // Duplicate shared vertices so every triangle is independent, then hard
        // normals -> flat shading.
        public static void FlatShade(Mesh mesh)
        {
            int[] tris = mesh.triangles;
            Vector3[] verts = mesh.vertices;
            Vector3[] outV = new Vector3[tris.Length];
            int[] outT = new int[tris.Length];
            for (int i = 0; i < tris.Length; i++)
            {
                outV[i] = verts[tris[i]];
                outT[i] = i;
            }
            mesh.Clear();
            mesh.vertices = outV;
            mesh.triangles = outT;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        // A 20-face icosahedron — the low-poly workhorse (rocks, tree crowns).
        public static Mesh Icosahedron(float radius)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            Vector3[] v =
            {
                new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
                new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
                new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
            };
            for (int i = 0; i < v.Length; i++) v[i] = v[i].normalized * radius;

            int[] f =
            {
                0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
                1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
                3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
                4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
            };

            Mesh m = new Mesh { name = "Icosahedron" };
            m.vertices = v;
            m.triangles = f;
            FlatShade(m);
            return m;
        }

        // A cone centred on the origin (apex up), like three.js ConeGeometry.
        public static Mesh Cone(float radius, float height, int segments)
        {
            List<Vector3> verts = new List<Vector3>();
            List<int> tris = new List<int>();
            float half = height / 2f;
            Vector3 apex = new Vector3(0, half, 0);
            Vector3 baseCenter = new Vector3(0, -half, 0);

            for (int i = 0; i < segments; i++)
            {
                float a0 = (float)i / segments * Mathf.PI * 2f;
                float a1 = (float)(i + 1) / segments * Mathf.PI * 2f;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, -half, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, -half, Mathf.Sin(a1) * radius);

                int b = verts.Count;
                verts.Add(apex); verts.Add(p1); verts.Add(p0);       // side
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);

                b = verts.Count;
                verts.Add(baseCenter); verts.Add(p0); verts.Add(p1); // base cap
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            }

            Mesh m = new Mesh { name = "Cone" };
            m.SetVertices(verts);
            m.SetTriangles(DoubleSided(tris), 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // A GameObject carrying a mesh + flat-shaded material, parented to `parent`.
        public static GameObject MeshObject(string name, Mesh mesh, Color color, Transform parent)
        {
            GameObject go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mat(color);
            return go;
        }

        // A primitive (cube/sphere/cylinder) without the auto-added collider.
        public static GameObject Primitive(PrimitiveType type, Color color, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            Collider col = go.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            if (parent != null) go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = Mat(color);
            return go;
        }
    }

    // Marks an ore-rock GameObject so the picker can resolve a raycast hit to a
    // node id and file an interact intent.
    public class OreRockRef : MonoBehaviour
    {
        public string id;
    }

    // Marks a camp/mine station (stall, furnace, anvil, board, entrance, shaft)
    // so the picker can walk the player over and the UI can open the right panel.
    public class StationRef : MonoBehaviour
    {
        public string kind;
    }
}
