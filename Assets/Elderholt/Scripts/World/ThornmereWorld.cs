using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  ThornmereWorld — builds the Phase 1 zone "Thornmere Reach": a hillside mine
    //  with a cluster of ore rocks, a birch stand, and a river bend. Terrain and
    //  props are recreated from the prototype's three.js scene (style, not
    //  vertices). Ore-rock visuals are driven by server node state.
    // ============================================================================
    public class ThornmereWorld
    {
        public readonly Dictionary<string, GameObject> RockGroups = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, Avatar> Avatars = new Dictionary<string, Avatar>();
        public GameObject Ground { get; private set; }
        public Transform Marker { get; private set; }

        readonly Transform root;

        public ThornmereWorld(ZoneServer server)
        {
            root = new GameObject("Thornmere Reach").transform;

            BuildTerrain();
            BuildRiver();
            BuildBirchStand();
            BuildMineHill();
            BuildRocks(server);
            BuildMarker();

            Avatars["you"] = new Avatar("You", Geo.ShirtYou, root);
            Avatars["fenn"] = new Avatar("Fenn", Geo.ShirtFenn, root);
            Avatars["you"].Place(2, 4, 0);
            Avatars["fenn"].Place(-5, 8, 0);
        }

        void BuildTerrain()
        {
            const int seg = 40;
            const float size = 130f;
            int side = seg + 1;
            Vector3[] verts = new Vector3[side * side];
            for (int gz = 0; gz < side; gz++)
            {
                for (int gx = 0; gx < side; gx++)
                {
                    float x = -size / 2f + gx * (size / seg);
                    float z = -size / 2f + gz * (size / seg);
                    verts[gz * side + gx] = new Vector3(x, Height(x, z), z);
                }
            }
            List<int> tris = new List<int>(seg * seg * 6);
            for (int gz = 0; gz < seg; gz++)
            {
                for (int gx = 0; gx < seg; gx++)
                {
                    int a = gz * side + gx, b = a + 1, c = a + side, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
            }
            Mesh mesh = new Mesh { name = "Thornmere Terrain" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.triangles = Geo.DoubleSided(tris);
            Geo.FlatShade(mesh);

            Ground = Geo.MeshObject("Ground", mesh, Geo.Grass, root);
            Ground.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        // Hillside mine to the east/south, a river channel, gentle rolling noise
        // elsewhere, flat near spawn — matches the prototype's heightfield.
        static float Height(float x, float z)
        {
            float d = Mathf.Sqrt(x * x + z * z);
            float h = d > 12f ? (Mathf.Sin(x * 0.35f) + Mathf.Cos(z * 0.3f)) * 0.45f + Random.value * 0.18f : 0f;
            if (x > 14f && z < -4f) h += (x - 14f) * 0.14f;
            if (Mathf.Abs(z - 24f + Mathf.Sin(x * 0.08f) * 6f) < 5f) h = -0.7f;
            return h;
        }

        void BuildRiver()
        {
            List<Vector3> pts = new List<Vector3>();
            for (float x = -65; x <= 65; x += 5) pts.Add(new Vector3(x, 0.03f, 24f - Mathf.Sin(x * 0.08f) * 6f));

            const float half = 1.7f;
            Vector3[] verts = new Vector3[pts.Count * 2];
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 fwd = (pts[Mathf.Min(i + 1, pts.Count - 1)] - pts[Mathf.Max(i - 1, 0)]);
                fwd.y = 0;
                fwd = fwd.sqrMagnitude > 1e-5f ? fwd.normalized : Vector3.forward;
                Vector3 perp = new Vector3(-fwd.z, 0, fwd.x);
                verts[i * 2] = pts[i] - perp * half;
                verts[i * 2 + 1] = pts[i] + perp * half;
            }
            List<int> tris = new List<int>();
            for (int i = 0; i < pts.Count - 1; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, dd = a + 3;
                tris.Add(a); tris.Add(c); tris.Add(b);
                tris.Add(b); tris.Add(c); tris.Add(dd);
            }
            Mesh mesh = new Mesh { name = "River" };
            mesh.vertices = verts;
            mesh.SetTriangles(Geo.DoubleSided(tris), 0);
            Geo.FlatShade(mesh);
            Geo.MeshObject("River", mesh, Geo.Water, root);
        }

        void BuildBirchStand()
        {
            int[,] spots = { { -14, -8 }, { -18, -12 }, { -11, -14 }, { -20, -6 }, { -16, -18 }, { -8, -10 }, { -24, -14 }, { -12, 2 } };
            for (int i = 0; i < spots.GetLength(0); i++)
            {
                Transform grp = new GameObject("Birch").transform;
                grp.SetParent(root, false);
                grp.localPosition = new Vector3(spots[i, 0], 0, spots[i, 1]);

                GameObject trunk = Geo.Primitive(PrimitiveType.Cylinder, Geo.BirchTrunk, grp);
                trunk.transform.localScale = new Vector3(0.44f, 1.2f, 0.44f); // ~r0.22, h2.4
                trunk.transform.localPosition = new Vector3(0, 1.2f, 0);

                float s = 0.7f + Random.value * 0.5f;
                GameObject crown = Geo.MeshObject("Crown", Geo.Icosahedron(1.6f * s), Geo.BirchCrown, grp);
                crown.transform.localPosition = new Vector3(0, 2.6f + s, 0);
            }
        }

        void BuildMineHill()
        {
            GameObject hill = Geo.MeshObject("MineHill", Geo.Cone(7f, 6f, 7), Geo.Hill, root);
            hill.transform.localPosition = new Vector3(30, 1.5f, -16);
        }

        void BuildRocks(ZoneServer server)
        {
            foreach (OreNode n in server.Nodes)
            {
                Transform grp = new GameObject("Ore_" + n.id).transform;
                grp.SetParent(root, false);
                grp.localPosition = new Vector3(n.x, 0, n.z);

                GameObject rock = Geo.MeshObject("Rock", Geo.Icosahedron(1.0f), Geo.Rock, grp);
                rock.transform.localScale = new Vector3(1f, 0.7f, 1f);
                rock.transform.localPosition = new Vector3(0, 0.5f, 0);
                rock.transform.localRotation = Quaternion.Euler(0, Random.value * 180f, 0);

                GameObject glint = Geo.MeshObject("Glint", Geo.Icosahedron(0.22f), Geo.Glint, grp);
                glint.transform.localPosition = new Vector3(0.4f, 0.85f, 0.3f);

                grp.gameObject.AddComponent<OreRockRef>().id = n.id;
                SphereCollider col = grp.gameObject.AddComponent<SphereCollider>();
                col.center = new Vector3(0, 0.6f, 0);
                col.radius = 1.15f;

                RockGroups[n.id] = grp.gameObject;
            }
        }

        void BuildMarker()
        {
            GameObject disc = Geo.Primitive(PrimitiveType.Cylinder, Geo.Marker, root);
            disc.name = "Marker";
            disc.transform.localScale = new Vector3(0.9f, 0.02f, 0.9f);
            disc.SetActive(false);
            Marker = disc.transform;
        }

        public void SetRockVisible(string id, bool visible)
        {
            if (RockGroups.TryGetValue(id, out GameObject go) && go.activeSelf != visible)
                go.SetActive(visible);
        }
    }
}
