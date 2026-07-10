using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  ThornmereWorld — builds the zone: the Phase 1 surface (hillside mine, ore
    //  rocks, birch stand, river bend) plus the Phase 2 camp (market stall,
    //  furnace, anvil, notice board), the mine entrance, and the two underground
    //  depth-band chambers. Chambers live at the same world offsets the server
    //  uses for their coordinates, so one space serves render and simulation.
    //  Ore-rock visuals are driven by server node state.
    // ============================================================================
    public class ThornmereWorld
    {
        public readonly Dictionary<string, GameObject> RockGroups = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, GameObject> SeamGlints = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, Avatar> Avatars = new Dictionary<string, Avatar>();
        public GameObject Ground { get; private set; }
        public readonly List<GameObject> Walkable = new List<GameObject>();   // ground + chamber floors
        public Transform Marker { get; private set; }

        readonly Transform root;

        public ThornmereWorld(ZoneServer server)
        {
            root = new GameObject("Thornmere Reach").transform;

            BuildTerrain();
            BuildRiver();
            BuildBirchStand();
            BuildMineHill();
            BuildCamp();
            BuildMineEntrance();
            BuildChambers();
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
            Walkable.Add(Ground);
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

                // KayKit tree if bundled, procedural birch otherwise.
                float s = 0.7f + Random.value * 0.5f;
                string treePath = i % 2 == 0 ? "KayKit/Nature/tree_single_A" : "KayKit/Nature/tree_single_B";
                if (TryModel(treePath, grp, Vector3.zero, 2.6f * s, -1f) != null) continue;

                GameObject trunk = Geo.Primitive(PrimitiveType.Cylinder, Geo.BirchTrunk, grp);
                trunk.transform.localScale = new Vector3(0.44f, 1.2f, 0.44f); // ~r0.22, h2.4
                trunk.transform.localPosition = new Vector3(0, 1.2f, 0);

                GameObject crown = Geo.MeshObject("Crown", Geo.Icosahedron(1.6f * s), Geo.BirchCrown, grp);
                crown.transform.localPosition = new Vector3(0, 2.6f + s, 0);
            }
        }

        void BuildMineHill()
        {
            GameObject hill = Geo.MeshObject("MineHill", Geo.Cone(7f, 6f, 7), Geo.Hill, root);
            hill.transform.localPosition = new Vector3(30, 1.5f, -16);
        }

        // ------------------------------------------------------------- Phase 2 camp
        void BuildCamp()
        {
            // Market stall: four posts, a slanted canopy, a counter.
            Transform stall = Station("Stall", "stall", ZoneServer.StallPos, 2.2f);
            for (int i = 0; i < 4; i++)
            {
                GameObject post = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, stall);
                post.transform.localScale = new Vector3(0.14f, 2.0f, 0.14f);
                post.transform.localPosition = new Vector3(i % 2 == 0 ? -1.1f : 1.1f, 1.0f, i < 2 ? -0.8f : 0.8f);
            }
            GameObject canopy = Geo.Primitive(PrimitiveType.Cube, Geo.Canopy, stall);
            canopy.transform.localScale = new Vector3(2.8f, 0.1f, 2.2f);
            canopy.transform.localPosition = new Vector3(0, 2.1f, 0);
            canopy.transform.localRotation = Quaternion.Euler(8f, 0, 0);
            if (TryModel("KayKit/Props/table_long", stall, new Vector3(0, 0, -0.9f), 1.6f, 0f) == null)
            {
                GameObject counter = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, stall);
                counter.transform.localScale = new Vector3(2.4f, 0.5f, 0.6f);
                counter.transform.localPosition = new Vector3(0, 0.55f, -0.9f);
            }
            TryModel("KayKit/Props/coin_stack_medium", stall, new Vector3(0.5f, 0.85f, -0.9f), 1.2f, -1f);

            // Camp clutter around the stall (visual only; silently skipped if the
            // model pack isn't present).
            TryModel("KayKit/Props/barrel_large", stall, new Vector3(-2.2f, 0, 0.6f), 1.4f, -1f);
            TryModel("KayKit/Props/crates_stacked", stall, new Vector3(2.4f, 0, 0.8f), 1.4f, -1f);
            TryModel("KayKit/Props/box_small", stall, new Vector3(1.8f, 0, -1.6f), 1.3f, -1f);
            TryModel("KayKit/Props/keg", stall, new Vector3(-1.8f, 0, -1.5f), 1.3f, -1f);
            TryModel("KayKit/Props/chest", stall, new Vector3(-2.6f, 0, -0.4f), 1.3f, 40f);

            // Furnace: stone block, chimney, ember mouth.
            Transform furnace = Station("Furnace", "furnace", ZoneServer.FurnacePos, 1.6f);
            GameObject body = Geo.Primitive(PrimitiveType.Cube, Geo.Stone, furnace);
            body.transform.localScale = new Vector3(1.4f, 1.2f, 1.4f);
            body.transform.localPosition = new Vector3(0, 0.6f, 0);
            GameObject chimney = Geo.Primitive(PrimitiveType.Cube, Geo.Stone, furnace);
            chimney.transform.localScale = new Vector3(0.5f, 1.4f, 0.5f);
            chimney.transform.localPosition = new Vector3(0.3f, 1.9f, 0.3f);
            GameObject mouth = Geo.Primitive(PrimitiveType.Cube, Geo.Ember, furnace);
            mouth.transform.localScale = new Vector3(0.6f, 0.5f, 0.1f);
            mouth.transform.localPosition = new Vector3(0, 0.45f, -0.71f);
            Light glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(furnace, false);
            glow.transform.localPosition = new Vector3(0, 0.8f, -1.0f);
            glow.type = LightType.Point;
            glow.color = Geo.Ember;
            glow.range = 6f;
            glow.intensity = 1.4f;

            // Anvil: base + horn block on a stump.
            Transform anvil = Station("Anvil", "anvil", ZoneServer.AnvilPos, 1.4f);
            GameObject stump = Geo.Primitive(PrimitiveType.Cylinder, Geo.Timber, anvil);
            stump.transform.localScale = new Vector3(0.8f, 0.3f, 0.8f);
            stump.transform.localPosition = new Vector3(0, 0.3f, 0);
            GameObject top = Geo.Primitive(PrimitiveType.Cube, Geo.Anvil, anvil);
            top.transform.localScale = new Vector3(1.1f, 0.35f, 0.45f);
            top.transform.localPosition = new Vector3(0, 0.78f, 0);

            // Notice board: two posts and a panel.
            Transform board = Station("NoticeBoard", "board", ZoneServer.BoardPos, 1.6f);
            for (int i = 0; i < 2; i++)
            {
                GameObject post = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, board);
                post.transform.localScale = new Vector3(0.14f, 1.9f, 0.14f);
                post.transform.localPosition = new Vector3(i == 0 ? -0.8f : 0.8f, 0.95f, 0);
            }
            GameObject panel = Geo.Primitive(PrimitiveType.Cube, Geo.Board, board);
            panel.transform.localScale = new Vector3(1.9f, 1.0f, 0.08f);
            panel.transform.localPosition = new Vector3(0, 1.35f, 0);
        }

        void BuildMineEntrance()
        {
            Transform ent = Station("MineEntrance", "entrance", ZoneServer.EntrancePos, 2.4f);
            // Dark doorway cut into the hillside, framed in timber.
            GameObject dark = Geo.Primitive(PrimitiveType.Cube, Color.black, ent);
            dark.transform.localScale = new Vector3(1.8f, 2.2f, 0.4f);
            dark.transform.localPosition = new Vector3(0, 1.1f, 0.3f);
            GameObject lintel = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, ent);
            lintel.transform.localScale = new Vector3(2.4f, 0.3f, 0.6f);
            lintel.transform.localPosition = new Vector3(0, 2.35f, 0.2f);
            for (int i = 0; i < 2; i++)
            {
                GameObject jamb = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, ent);
                jamb.transform.localScale = new Vector3(0.3f, 2.3f, 0.6f);
                jamb.transform.localPosition = new Vector3(i == 0 ? -1.05f : 1.05f, 1.15f, 0.2f);
            }
            TryModel("KayKit/Props/torch_lit", ent, new Vector3(1.6f, 0, -0.4f), 1.4f, -1f);
            TryModel("KayKit/Props/barrel_large", ent, new Vector3(-1.9f, 0, -0.6f), 1.3f, -1f);
            ent.rotation = Quaternion.Euler(0, -35f, 0);
        }

        // Underground depth-band chambers, built where the server places them.
        void BuildChambers()
        {
            for (int b = 1; b < Bands.Count; b++)
            {
                BandDef def = Bands.All[b];
                Transform grp = new GameObject("Band_" + def.name).transform;
                grp.SetParent(root, false);
                grp.localPosition = new Vector3(def.originX, 0, def.originZ);

                // Floor disc (walkable).
                GameObject floor = Geo.Primitive(PrimitiveType.Cylinder, Geo.ChamberFloor, grp);
                floor.transform.localScale = new Vector3(def.radius * 2f + 4f, 0.05f, def.radius * 2f + 4f);
                floor.transform.localPosition = new Vector3(0, -0.05f, 0);
                MeshCollider mc = floor.AddComponent<MeshCollider>();
                mc.sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;
                Walkable.Add(floor);

                // Wall ring: jagged boulders around the perimeter.
                int walls = 14;
                for (int i = 0; i < walls; i++)
                {
                    float a = (float)i / walls * Mathf.PI * 2f;
                    float r = def.radius + 2.4f;
                    Vector3 at = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                    string wallPath = i % 2 == 0 ? "KayKit/Nature/rock_single_A" : "KayKit/Nature/rock_single_B";
                    if (TryModel(wallPath, grp, at, 4.5f + Random.value * 2f, -1f) != null) continue;
                    GameObject wall = Geo.MeshObject("Wall", Geo.Icosahedron(2.6f + Random.value * 1.4f), Geo.ChamberWall, grp);
                    wall.transform.localPosition = at + Vector3.up * 1.2f;
                    wall.transform.localRotation = Quaternion.Euler(Random.value * 40f, Random.value * 180f, Random.value * 40f);
                }

                // Torches by the shaft so the gallery reads in the gloom.
                for (int i = 0; i < 3; i++)
                {
                    float a = i * 2.1f + 0.5f;
                    TryModel("KayKit/Props/torch_lit", grp, new Vector3(Mathf.Cos(a) * (def.radius * 0.55f), 0, Mathf.Sin(a) * (def.radius * 0.55f)), 1.4f, -1f);
                }

                // The shaft ladder at the chamber's heart: pole + rungs.
                Transform shaft = Station("Shaft_" + b, "shaft", new Vector2(def.originX, def.originZ), 1.4f);
                for (int i = 0; i < 2; i++)
                {
                    GameObject rail = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, shaft);
                    rail.transform.localScale = new Vector3(0.1f, 3.4f, 0.1f);
                    rail.transform.localPosition = new Vector3(i == 0 ? -0.3f : 0.3f, 1.7f, 0);
                }
                for (int i = 0; i < 5; i++)
                {
                    GameObject rung = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, shaft);
                    rung.transform.localScale = new Vector3(0.7f, 0.08f, 0.08f);
                    rung.transform.localPosition = new Vector3(0, 0.5f + i * 0.65f, 0);
                }

                // Shoring props against the walls (flavour for the shoring verb).
                for (int i = 0; i < 3; i++)
                {
                    float a = (0.7f + i * 2.1f);
                    Vector3 at = new Vector3(Mathf.Cos(a) * (def.radius + 0.8f), 0f, Mathf.Sin(a) * (def.radius + 0.8f));
                    if (TryModel("KayKit/Props/pillar", grp, at, 1.5f, -1f) != null) continue;
                    GameObject prop = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, grp);
                    prop.transform.localScale = new Vector3(0.18f, 3.0f, 0.18f);
                    prop.transform.localPosition = at + Vector3.up * 1.4f;
                    prop.transform.localRotation = Quaternion.Euler(0, 0, 8f);
                }

                // A dim lamp so the chamber reads in the gloom.
                Light lamp = new GameObject("Lamp").AddComponent<Light>();
                lamp.transform.SetParent(grp, false);
                lamp.transform.localPosition = new Vector3(0, 5f, 0);
                lamp.type = LightType.Point;
                lamp.color = Geo.Sun;
                lamp.range = def.radius * 2.6f;
                lamp.intensity = b == 1 ? 0.9f : 0.6f;
            }
        }

        // Instantiate a bundled KayKit model (CC0) from Resources; returns null if
        // missing so every call site keeps its procedural fallback.
        static GameObject TryModel(string path, Transform parent, Vector3 localPos, float scale, float yawDeg)
        {
            GameObject prefab = Resources.Load<GameObject>(path);
            if (prefab == null) return null;
            GameObject go = Object.Instantiate(prefab, parent);
            go.name = path;
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
            go.transform.localRotation = Quaternion.Euler(0, yawDeg < 0f ? Random.value * 360f : yawDeg, 0);
            return go;
        }

        Transform Station(string name, string kind, Vector2 pos, float clickRadius)
        {
            Transform grp = new GameObject(name).transform;
            grp.SetParent(root, false);
            grp.localPosition = new Vector3(pos.x, 0, pos.y);
            grp.gameObject.AddComponent<StationRef>().kind = kind;
            SphereCollider col = grp.gameObject.AddComponent<SphereCollider>();
            col.center = new Vector3(0, 1.0f, 0);
            col.radius = clickRadius;
            return grp;
        }

        void BuildRocks(ZoneServer server)
        {
            foreach (OreNode n in server.Nodes)
            {
                Transform grp = new GameObject("Ore_" + n.id).transform;
                grp.SetParent(root, false);
                grp.localPosition = new Vector3(n.x, 0, n.z);

                // KayKit boulder (per metal, for variety) or the procedural rock.
                string rockPath = n.metal == "tin" ? "KayKit/Nature/rock_single_D"
                    : n.metal == "iron" ? "KayKit/Nature/rock_single_E"
                    : "KayKit/Nature/rock_single_C";
                if (TryModel(rockPath, grp, Vector3.zero, 2.2f, -1f) == null)
                {
                    GameObject rock = Geo.MeshObject("Rock", Geo.Icosahedron(1.0f), Geo.Rock, grp);
                    rock.transform.localScale = new Vector3(1f, 0.7f, 1f);
                    rock.transform.localPosition = new Vector3(0, 0.5f, 0);
                    rock.transform.localRotation = Quaternion.Euler(0, Random.value * 180f, 0);
                }

                Color glintCol = n.metal == "tin" ? Geo.GlintTin : n.metal == "iron" ? Geo.GlintIron : Geo.Glint;
                GameObject glint = Geo.MeshObject("Glint", Geo.Icosahedron(0.22f), glintCol, grp);
                glint.transform.localPosition = new Vector3(0.4f, 0.85f, 0.3f);

                // Seam hint: a bright marker shown when the seam runs to this rock.
                GameObject seam = Geo.MeshObject("Seam", Geo.Icosahedron(0.3f), Geo.Marker, grp);
                seam.transform.localPosition = new Vector3(-0.3f, 1.15f, -0.2f);
                seam.SetActive(false);
                SeamGlints[n.id] = seam;

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

        public void SetSeamHint(string id, bool on)
        {
            if (SeamGlints.TryGetValue(id, out GameObject go) && go.activeSelf != on)
                go.SetActive(on);
        }
    }
}
