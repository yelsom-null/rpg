using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  ThornmereWorld — builds the zone to the Bracken Cross city map:
    //  a walled first city (no hazards inside — social + economic space) with
    //  Market Square day stalls, the Wayfarers' Guildhall (work orders), the
    //  Vault (bank), High Street shopfront lots, Smithy Row, the Clan Quarter
    //  and Warehouse Row — and outside, every gate pointing at a skill:
    //  Bracken Grove (N, logging/foraging), Mirror Pond (W, fishery),
    //  Grey Quarry (E, surface veins + the deep shafts below), the Caravan
    //  Field (S) and the dashed edge of Redbriar March (SE, bounty zone).
    //  Underground depth-band chambers live at the same world offsets the
    //  server uses. Ore-rock visuals are driven by server node state.
    // ============================================================================
    public class ThornmereWorld
    {
        public readonly Dictionary<string, GameObject> RockGroups = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, GameObject> SeamGlints = new Dictionary<string, GameObject>();
        public readonly Dictionary<string, Avatar> Avatars = new Dictionary<string, Avatar>();
        public GameObject Ground { get; private set; }
        public readonly List<GameObject> Walkable = new List<GameObject>();   // ground + chamber floors
        public readonly List<KeyValuePair<string, Vector3>> Signs = new List<KeyValuePair<string, Vector3>>();
        public Transform Marker { get; private set; }

        readonly Transform root;

        public ThornmereWorld(ZoneServer server)
        {
            root = new GameObject("Bracken Cross").transform;

            BuildTerrain();
            BuildCity();
            BuildGrove();
            BuildPond();
            BuildQuarry();
            BuildCaravanField();
            BuildRedbriar();
            BuildChambers();
            BuildRocks(server);
            BuildMarker();

            Avatars["you"] = new Avatar("You", Geo.ShirtYou, root);
            Avatars["you"].Place(0, 2, 0);
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
            Mesh mesh = new Mesh { name = "Bracken Terrain" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.triangles = Geo.DoubleSided(tris);
            Geo.FlatShade(mesh);

            Ground = Geo.MeshObject("Ground", mesh, Geo.Grass, root);
            Ground.AddComponent<MeshCollider>().sharedMesh = mesh;
            Walkable.Add(Ground);
        }

        // Flat inside the city walls, a shallow quarry bowl to the east, a pond
        // dip to the west, gentle rolling noise elsewhere.
        static float Height(float x, float z)
        {
            if (Mathf.Abs(x) < 20f && Mathf.Abs(z) < 16f) return 0f;   // the city

            float qd = Mathf.Sqrt((x - 33f) * (x - 33f) + (z + 1f) * (z + 1f));
            if (qd < 10f) return -0.5f + qd * 0.04f;                    // quarry bowl

            float pd = Mathf.Sqrt((x + 30f) * (x + 30f) + (z - 2f) * (z - 2f));
            if (pd < 9f) return -0.55f;                                  // pond dip

            float h = (Mathf.Sin(x * 0.35f) + Mathf.Cos(z * 0.3f)) * 0.35f + Random.value * 0.15f;
            if (Mathf.Abs(x) < 1.8f || Mathf.Abs(z) < 1.8f) h *= 0.2f;   // road lines
            return h;
        }

        // -------------------------------------------------------------- the city
        void BuildCity()
        {
            Transform city = new GameObject("City").transform;
            city.SetParent(root, false);

            BuildWallsAndRoads(city);
            BuildMarketSquare(city);
            BuildGuildhall(city);
            BuildVault(city);
            BuildHighStreet(city);
            BuildSmithyRow(city);
            BuildPlots(city);
        }

        void BuildWallsAndRoads(Transform city)
        {
            // Wall runs with gate gaps at north and south (x in [-2, 2]).
            WallRun(city, new Vector3(-10f, 0, 14f), 16f, true);    // N-west run
            WallRun(city, new Vector3(10f, 0, 14f), 16f, true);     // N-east run
            WallRun(city, new Vector3(-10f, 0, -14f), 16f, true);   // S-west run
            WallRun(city, new Vector3(10f, 0, -14f), 16f, true);    // S-east run
            WallRun(city, new Vector3(18f, 0, 0f), 28f, false);     // east wall
            WallRun(city, new Vector3(-18f, 0, 0f), 28f, false);    // west wall

            Gate(city, new Vector3(0, 0, 14f), "N GATE");
            Gate(city, new Vector3(0, 0, -14f), "S GATE");

            // Roads: dirt strips, no colliders so clicks fall through to terrain.
            Road(city, new Vector3(0, 0.02f, 2f), 2.4f, 56f);     // N-S high road
            Road(city, new Vector3(2f, 0.02f, 0), 64f, 2.4f);     // E-W cross road
        }

        void WallRun(Transform parent, Vector3 centre, float length, bool alongX)
        {
            GameObject wall = Geo.Primitive(PrimitiveType.Cube, Geo.Stone, parent);
            wall.name = "Wall";
            wall.transform.localScale = alongX ? new Vector3(length, 2.6f, 0.7f) : new Vector3(0.7f, 2.6f, length);
            wall.transform.localPosition = centre + Vector3.up * 1.3f;
        }

        void Gate(Transform parent, Vector3 at, string label)
        {
            for (int i = 0; i < 2; i++)
            {
                GameObject post = Geo.Primitive(PrimitiveType.Cube, Geo.Stone, parent);
                post.transform.localScale = new Vector3(0.9f, 3.4f, 0.9f);
                post.transform.localPosition = at + new Vector3(i == 0 ? -2.4f : 2.4f, 1.7f, 0);
            }
            GameObject lintel = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, parent);
            lintel.transform.localScale = new Vector3(5.8f, 0.4f, 1.0f);
            lintel.transform.localPosition = at + Vector3.up * 3.3f;
            TryModel("KayKit/Props/torch_lit", parent, at + new Vector3(-2.9f, 0, 0.8f), 1.3f, -1f);
            TryModel("Village/Lantern", parent, at + new Vector3(2.9f, 0, 0.8f), 1f, -1f);
            Signs.Add(new KeyValuePair<string, Vector3>(label, at + Vector3.up * 4.1f));
        }

        void Road(Transform parent, Vector3 centre, float sx, float sz)
        {
            GameObject strip = Geo.Primitive(PrimitiveType.Cube, Geo.Hex(0xc9b98e), parent);
            strip.name = "Road";
            strip.transform.localScale = new Vector3(sx, 0.04f, sz);
            strip.transform.localPosition = centre;
        }

        // Market Square: eight day stalls around the trade post (the stall-keeper
        // station the economy talks to).
        void BuildMarketSquare(Transform city)
        {
            Transform sq = new GameObject("MarketSquare").transform;
            sq.SetParent(city, false);
            sq.localPosition = new Vector3(ZoneServer.StallPos.x, 0, ZoneServer.StallPos.y);

            GameObject plaza = Geo.Primitive(PrimitiveType.Cube, Geo.Hex(0xd9c98e), sq);
            plaza.transform.localScale = new Vector3(11f, 0.06f, 8f);
            plaza.transform.localPosition = new Vector3(0, 0.03f, 0);

            // Day stalls: two rows of four.
            Color[] canopies = { Geo.Canopy, Geo.Hex(0x4a6f96), Geo.Hex(0x5f7a44), Geo.Hex(0xb09040) };
            for (int i = 0; i < 8; i++)
            {
                float sx = -3.9f + (i % 4) * 2.6f;
                float sz = i < 4 ? -2.6f : 2.6f;
                Transform st = new GameObject("DayStall").transform;
                st.SetParent(sq, false);
                st.localPosition = new Vector3(sx, 0, sz);
                // A finished village stall if assigned, else procedural posts+canopy.
                if (TryModel("Village/Stall", st, Vector3.zero, 1f, sz > 0 ? 180f : 0f) == null)
                {
                    for (int p = 0; p < 4; p++)
                    {
                        GameObject post = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, st);
                        post.transform.localScale = new Vector3(0.1f, 1.6f, 0.1f);
                        post.transform.localPosition = new Vector3(p % 2 == 0 ? -0.8f : 0.8f, 0.8f, p < 2 ? -0.6f : 0.6f);
                    }
                    GameObject canopy = Geo.Primitive(PrimitiveType.Cube, canopies[i % canopies.Length], st);
                    canopy.transform.localScale = new Vector3(2.0f, 0.08f, 1.6f);
                    canopy.transform.localPosition = new Vector3(0, 1.7f, 0);
                    canopy.transform.localRotation = Quaternion.Euler(6f, 0, 0);
                    if (i % 3 == 0) TryModel("KayKit/Props/box_small", st, new Vector3(0.3f, 0, 0), 1.2f, -1f);
                }
            }

            // The trade post at centre: the clickable stall station.
            Transform post2 = Station("TradePost", "stall", ZoneServer.StallPos, 2.0f);
            if (TryModel("KayKit/Props/table_long", post2, Vector3.zero, 1.6f, 0f) == null)
            {
                GameObject counter = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, post2);
                counter.transform.localScale = new Vector3(2.4f, 0.5f, 0.6f);
                counter.transform.localPosition = new Vector3(0, 0.55f, 0);
            }
            TryModel("KayKit/Props/coin_stack_medium", post2, new Vector3(0.5f, 0.85f, 0), 1.2f, -1f);
            TryModel("KayKit/Resource/Gold_Bars_Stack_Small", post2, new Vector3(-0.6f, 0.85f, 0), 1.1f, -1f);
            TryModel("KayKit/Props/barrel_large", post2, new Vector3(-1.8f, 0, 0.7f), 1.3f, -1f);
            TryModel("KayKit/Resource/Wood_Log_Stack", post2, new Vector3(1.9f, 0, 0.8f), 1.2f, -1f);
            TryModel("KayKit/Resource/Textiles_A", post2, new Vector3(1.2f, 0, -1.1f), 1.2f, -1f);

            // Optional village dressing (inert until you assign the slots).
            TryModel("Village/Well", sq, new Vector3(4.3f, 0, 0f), 1f, -1f);
            TryModel("Village/Lantern", sq, new Vector3(-4.3f, 0, 2.6f), 1f, -1f);
            TryModel("Village/Lantern", sq, new Vector3(-4.3f, 0, -2.6f), 1f, -1f);
            Signs.Add(new KeyValuePair<string, Vector3>("MARKET SQUARE", new Vector3(ZoneServer.StallPos.x, 3.1f, ZoneServer.StallPos.y)));
        }

        void BuildGuildhall(Transform city)
        {
            Transform hall = new GameObject("Guildhall").transform;
            hall.SetParent(city, false);
            hall.localPosition = new Vector3(-10f, 0, 9f);

            Building(hall, new Vector3(7.5f, 3.2f, 4.5f), Geo.Hex(0xd0b878));
            TryModel("KayKit/Props/torch_lit", hall, new Vector3(2.2f, 0, -2.6f), 1.3f, -1f);
            Signs.Add(new KeyValuePair<string, Vector3>("WAYFARERS' GUILDHALL", new Vector3(-10f, 4.3f, 9f)));

            // The work-order board out front (the contract station).
            Transform board = Station("WorkOrders", "board", ZoneServer.BoardPos, 1.6f);
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

        void BuildVault(Transform city)
        {
            Transform vault = new GameObject("Vault").transform;
            vault.SetParent(city, false);
            vault.localPosition = new Vector3(10f, 0, 9f);

            Building(vault, new Vector3(6f, 3.6f, 4f), Geo.Hex(0xcfc39a));
            Signs.Add(new KeyValuePair<string, Vector3>("THE VAULT", new Vector3(10f, 4.7f, 9f)));

            // The teller's chest out front (the bank station).
            Transform teller = Station("VaultTeller", "vault", ZoneServer.VaultPos, 1.8f);
            if (TryModel("KayKit/Props/chest", teller, Vector3.zero, 1.5f, 180f) == null)
            {
                GameObject chest = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, teller);
                chest.transform.localScale = new Vector3(1.1f, 0.8f, 0.7f);
                chest.transform.localPosition = new Vector3(0, 0.4f, 0);
            }
            TryModel("KayKit/Props/torch_lit", teller, new Vector3(1.4f, 0, 0.4f), 1.3f, -1f);
        }

        // High Street: seven shopfront lots down the west side. Two read as
        // deeded (built), the rest as claimable frames — the deed economy is a
        // later phase; the street sets the stage.
        void BuildHighStreet(Transform city)
        {
            for (int i = 0; i < 7; i++)
            {
                Vector3 at = new Vector3(-14f, 0, -9f + i * 2.7f);
                if (i < 2)
                {
                    Transform shop = new GameObject("Shop_" + i).transform;
                    shop.SetParent(city, false);
                    shop.localPosition = at;
                    // A finished village house if one's assigned, else the block.
                    if (TryModel("Village/House", shop, Vector3.zero, 1f, 90f) == null)
                        Building(shop, new Vector3(2.2f, 2.0f, 2.0f), Geo.Hex(0xe2d4ae));
                    TryModel("KayKit/Props/crates_stacked", shop, new Vector3(1.5f, 0, 0), 1.1f, -1f);
                }
                else
                {
                    // Claimable lot: corner posts and a rope-line frame.
                    Transform lot = new GameObject("Lot_" + i).transform;
                    lot.SetParent(city, false);
                    lot.localPosition = at;
                    for (int c = 0; c < 4; c++)
                    {
                        GameObject post = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, lot);
                        post.transform.localScale = new Vector3(0.12f, 0.9f, 0.12f);
                        post.transform.localPosition = new Vector3(c % 2 == 0 ? -1.1f : 1.1f, 0.45f, c < 2 ? -1f : 1f);
                    }
                }
            }
            Signs.Add(new KeyValuePair<string, Vector3>("HIGH STREET — lots for deed", new Vector3(-14f, 2.9f, 0f)));
        }

        void BuildSmithyRow(Transform city)
        {
            Transform row = new GameObject("SmithyRow").transform;
            row.SetParent(city, false);
            row.localPosition = new Vector3(2.5f, 0, -9.5f);

            // Open work shed over the public forges.
            for (int i = 0; i < 4; i++)
            {
                GameObject post = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, row);
                post.transform.localScale = new Vector3(0.18f, 2.6f, 0.18f);
                post.transform.localPosition = new Vector3(i % 2 == 0 ? -3.2f : 3.2f, 1.3f, i < 2 ? -1.6f : 1.6f);
            }
            GameObject roof = Geo.Primitive(PrimitiveType.Cube, Geo.Hex(0xc9b0a0), row);
            roof.transform.localScale = new Vector3(7.4f, 0.12f, 4.0f);
            roof.transform.localPosition = new Vector3(0, 2.7f, 0);
            roof.transform.localRotation = Quaternion.Euler(4f, 0, 0);
            Signs.Add(new KeyValuePair<string, Vector3>("SMITHY ROW", new Vector3(2.5f, 3.6f, -9.5f)));

            // Furnace station.
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

            // Anvil station.
            Transform anvil = Station("Anvil", "anvil", ZoneServer.AnvilPos, 1.4f);
            GameObject stump = Geo.Primitive(PrimitiveType.Cylinder, Geo.Timber, anvil);
            stump.transform.localScale = new Vector3(0.8f, 0.3f, 0.8f);
            stump.transform.localPosition = new Vector3(0, 0.3f, 0);
            GameObject top = Geo.Primitive(PrimitiveType.Cube, Geo.Anvil, anvil);
            top.transform.localScale = new Vector3(1.1f, 0.35f, 0.45f);
            top.transform.localPosition = new Vector3(0, 0.78f, 0);

            TryModel("KayKit/Props/keg", row, new Vector3(-2.6f, 0, 1.0f), 1.3f, -1f);
            TryModel("KayKit/Props/box_small", row, new Vector3(2.6f, 0, 1.1f), 1.3f, -1f);
            // Work-in-progress stock: bars by the anvil, coal by the furnace mouth.
            TryModel("KayKit/Resource/Iron_Bars_Stack_Small", row, new Vector3(2.4f, 0, -1.2f), 1.2f, -1f);
            TryModel("KayKit/Resource/Copper_Bars_Stack_Small", row, new Vector3(-0.6f, 0, 1.2f), 1.2f, -1f);
            TryModel("KayKit/Resource/Stone_Chunks_Small", row, new Vector3(-1.9f, 0, -1.2f), 1.3f, -1f);
        }

        // Clan Quarter hall plots and Warehouse Row bulk lots (east side) —
        // staked ground for later phases.
        void BuildPlots(Transform city)
        {
            for (int i = 0; i < 3; i++)
                PlotFrame(city, new Vector3(13f, 0, 7f - i * 4f), 3.2f, 2.8f, Geo.Hex(0x7a8256));
            Signs.Add(new KeyValuePair<string, Vector3>("CLAN QUARTER — hall plots", new Vector3(13f, 2.7f, 9.5f)));

            for (int i = 0; i < 2; i++)
                PlotFrame(city, new Vector3(13f, 0, -7f - i * 4f), 3.2f, 2.8f, Geo.Hex(0x8a7a56));
            Signs.Add(new KeyValuePair<string, Vector3>("WAREHOUSE ROW", new Vector3(13f, 2.7f, -6f)));
        }

        void PlotFrame(Transform parent, Vector3 at, float w, float d, Color c)
        {
            Transform plot = new GameObject("Plot").transform;
            plot.SetParent(parent, false);
            plot.localPosition = at;
            for (int i = 0; i < 4; i++)
            {
                GameObject post = Geo.Primitive(PrimitiveType.Cube, c, plot);
                post.transform.localScale = new Vector3(0.15f, 1.0f, 0.15f);
                post.transform.localPosition = new Vector3(i % 2 == 0 ? -w / 2f : w / 2f, 0.5f, i < 2 ? -d / 2f : d / 2f);
            }
        }

        // A simple building: plinth walls + slab roof; door gap faces -z.
        void Building(Transform parent, Vector3 size, Color c)
        {
            GameObject body = Geo.Primitive(PrimitiveType.Cube, c, parent);
            body.transform.localScale = size;
            body.transform.localPosition = new Vector3(0, size.y / 2f, 0);
            GameObject roof = Geo.Primitive(PrimitiveType.Cube, Geo.Hex(0x8a6a56), parent);
            roof.transform.localScale = new Vector3(size.x + 0.6f, 0.25f, size.z + 0.6f);
            roof.transform.localPosition = new Vector3(0, size.y + 0.12f, 0);
            GameObject door = Geo.Primitive(PrimitiveType.Cube, Geo.Hex(0x3a2c1c), parent);
            door.transform.localScale = new Vector3(1.0f, 1.7f, 0.1f);
            door.transform.localPosition = new Vector3(0, 0.85f, -size.z / 2f - 0.03f);
        }

        // ---------------------------------------------------------- the outskirts
        void BuildGrove()
        {
            int[,] spots = { { -8, 21 }, { -4, 25 }, { 0, 22 }, { 4, 27 }, { -10, 27 }, { 7, 23 }, { -2, 29 }, { 3, 20 } };
            for (int i = 0; i < spots.GetLength(0); i++)
            {
                Transform grp = new GameObject("GroveTree").transform;
                grp.SetParent(root, false);
                grp.localPosition = new Vector3(spots[i, 0], 0, spots[i, 1]);

                float s = 0.7f + Random.value * 0.5f;
                string treePath = i % 2 == 0 ? "KayKit/Nature/tree_single_A" : "KayKit/Nature/tree_single_B";
                if (TryModel(treePath, grp, Vector3.zero, 2.6f * s, -1f) != null) continue;

                GameObject trunk = Geo.Primitive(PrimitiveType.Cylinder, Geo.BirchTrunk, grp);
                trunk.transform.localScale = new Vector3(0.44f, 1.2f, 0.44f);
                trunk.transform.localPosition = new Vector3(0, 1.2f, 0);
                GameObject crown = Geo.MeshObject("Crown", Geo.Icosahedron(1.6f * s), Geo.BirchCrown, grp);
                crown.transform.localPosition = new Vector3(0, 2.6f + s, 0);
            }
            TryModel("KayKit/Resource/Wood_Log_B", root, new Vector3(-6f, 0, 23f), 1.4f, -1f);
            TryModel("KayKit/Resource/Wood_Log_Stack", root, new Vector3(2f, 0, 25f), 1.3f, -1f);
            Signs.Add(new KeyValuePair<string, Vector3>("BRACKEN GROVE — logging · foraging (soon)", new Vector3(-2f, 3.4f, 24f)));
        }

        void BuildPond()
        {
            GameObject water = Geo.Primitive(PrimitiveType.Cylinder, Geo.Water, root);
            water.name = "MirrorPond";
            water.transform.localScale = new Vector3(15f, 0.03f, 12f);
            water.transform.localPosition = new Vector3(-30f, -0.25f, 2f);
            TryModel("KayKit/Nature/rock_single_B", root, new Vector3(-24f, 0, 8f), 2.0f, -1f);
            Signs.Add(new KeyValuePair<string, Vector3>("MIRROR POND — fishery (soon)", new Vector3(-30f, 2.6f, 2f)));
        }

        void BuildQuarry()
        {
            Transform q = new GameObject("GreyQuarry").transform;
            q.SetParent(root, false);
            q.localPosition = new Vector3(33f, 0, -1f);

            // Rim boulders with a gap toward the city road (west).
            int rim = 12;
            for (int i = 0; i < rim; i++)
            {
                float a = (float)i / rim * Mathf.PI * 2f;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 180f)) < 32f) continue;   // west opening
                Vector3 at = new Vector3(Mathf.Cos(a) * 10f, 0, Mathf.Sin(a) * 10f);
                string rk = i % 2 == 0 ? "KayKit/Nature/rock_single_A" : "KayKit/Nature/rock_single_B";
                if (TryModel(rk, q, at, 3.2f + Random.value * 1.5f, -1f) == null)
                {
                    GameObject wall = Geo.MeshObject("RimRock", Geo.Icosahedron(2.2f + Random.value), Geo.ChamberWall, q);
                    wall.transform.localPosition = at + Vector3.up * 0.9f;
                }
            }
            Signs.Add(new KeyValuePair<string, Vector3>("GREY QUARRY — deep shafts below", new Vector3(33f, 3.8f, -1f)));

            // The shaft mouth down to Greyroot Gallery.
            Transform ent = Station("MineEntrance", "entrance", ZoneServer.EntrancePos, 2.4f);
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
            ent.rotation = Quaternion.Euler(0, 90f, 0);   // mouth faces the quarry floor (west)

            // Quarry-floor spoil: hewn stone waiting on the caravan.
            TryModel("KayKit/Resource/Stone_Chunks_Large", q, new Vector3(-3.5f, 0, 4f), 1.6f, -1f);
            TryModel("KayKit/Resource/Stone_Bricks_Stack_Medium", q, new Vector3(2f, 0, 5f), 1.4f, -1f);
            TryModel("KayKit/Resource/Wood_Log_A", q, new Vector3(-4f, 0, -3f), 1.4f, -1f);
        }

        void BuildCaravanField()
        {
            Transform f = new GameObject("CaravanField").transform;
            f.SetParent(root, false);
            f.localPosition = new Vector3(0, 0, -24f);

            for (int i = 0; i < 6; i++)
            {
                GameObject post = Geo.Primitive(PrimitiveType.Cube, Geo.Timber, f);
                post.transform.localScale = new Vector3(0.15f, 1.1f, 0.15f);
                post.transform.localPosition = new Vector3(-6f + i * 2.4f, 0.55f, 2.2f);
            }
            TryModel("KayKit/Props/crates_stacked", f, new Vector3(-3f, 0, -0.5f), 1.4f, -1f);
            TryModel("KayKit/Props/barrel_large", f, new Vector3(2.5f, 0, -1f), 1.4f, -1f);
            TryModel("KayKit/Props/box_small", f, new Vector3(0.4f, 0, 0.6f), 1.3f, -1f);
            TryModel("KayKit/Resource/Wood_Planks_Stack_Medium", f, new Vector3(-1.2f, 0, -1.4f), 1.4f, -1f);
            TryModel("KayKit/Resource/Textiles_Stack_Large", f, new Vector3(4.4f, 0, 0.4f), 1.3f, -1f);

            // Optional village dressing: a wagon and a fence line along the paddock.
            TryModel("Village/Cart", f, new Vector3(1.5f, 0, -2.6f), 1f, 20f);
            for (int i = 0; i < 5; i++)
                TryModel("Village/Fence", f, new Vector3(-6f + i * 3f, 0, 3.4f), 1f, 90f);
            Signs.Add(new KeyValuePair<string, Vector3>("CARAVAN FIELD — runs depart south (soon)", new Vector3(0, 2.8f, -24f)));
        }

        void BuildRedbriar()
        {
            GameObject march = Geo.Primitive(PrimitiveType.Cube, Geo.Hex(0xc9a08e), root);
            march.name = "RedbriarMarch";
            march.transform.localScale = new Vector3(26f, 0.05f, 14f);
            march.transform.localPosition = new Vector3(30f, 0.02f, -27f);
            for (int i = 0; i < 3; i++)
                TryModel("KayKit/Nature/rock_single_E", root, new Vector3(24f + i * 6f, 0, -25f - (i % 2) * 5f), 2.4f, -1f);
            Signs.Add(new KeyValuePair<string, Vector3>("REDBRIAR MARCH — bounty zone · PvP (later)", new Vector3(30f, 3.0f, -27f)));
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

        // Instantiate a model for `path`: an Inspector-assigned override on the
        // ElderholtAssets set if present, else the bundled KayKit model. Returns
        // null if neither exists, so every call site keeps its procedural fallback.
        static GameObject TryModel(string path, Transform parent, Vector3 localPos, float scale, float yawDeg)
        {
            GameObject prefab = AssetLibrary.Resolve(path);
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

                bool heart = n.metal == Items.HeartMetal;

                // KayKit boulder (per metal, for variety) or the procedural rock.
                // The Heart is a bigger boulder with a golden glow of its own.
                string rockPath = heart ? "KayKit/Nature/rock_single_A"
                    : n.metal == "tin" ? "KayKit/Nature/rock_single_D"
                    : n.metal == "iron" ? "KayKit/Nature/rock_single_E"
                    : "KayKit/Nature/rock_single_C";
                if (TryModel(rockPath, grp, Vector3.zero, heart ? 3.4f : 2.2f, -1f) == null)
                {
                    GameObject rock = Geo.MeshObject("Rock", Geo.Icosahedron(heart ? 1.6f : 1.0f), Geo.Rock, grp);
                    rock.transform.localScale = new Vector3(1f, 0.7f, 1f);
                    rock.transform.localPosition = new Vector3(0, 0.5f, 0);
                    rock.transform.localRotation = Quaternion.Euler(0, Random.value * 180f, 0);
                }

                // The exposed vein: a metal nugget from Resource Bits (tin reads
                // as silver), or the old glint icosahedron as fallback.
                string nugget = heart ? "KayKit/Resource/Gold_Nugget_Large"
                    : n.metal == "tin" ? "KayKit/Resource/Silver_Nugget_Medium"
                    : n.metal == "iron" ? "KayKit/Resource/Iron_Nugget_Medium"
                    : "KayKit/Resource/Copper_Nugget_Medium";
                if (TryModel(nugget, grp, new Vector3(0.4f, heart ? 1.4f : 0.8f, 0.3f), heart ? 1.8f : 1.2f, -1f) == null)
                {
                    Color glintCol = n.metal == "tin" ? Geo.GlintTin : n.metal == "iron" ? Geo.GlintIron : Geo.Glint;
                    GameObject glint = Geo.MeshObject("Glint", Geo.Icosahedron(heart ? 0.5f : 0.22f), glintCol, grp);
                    glint.transform.localPosition = new Vector3(0.4f, heart ? 1.5f : 0.85f, 0.3f);
                }

                if (heart)
                {
                    Light pulse = new GameObject("HeartGlow").AddComponent<Light>();
                    pulse.transform.SetParent(grp, false);
                    pulse.transform.localPosition = new Vector3(0, 2.2f, 0);
                    pulse.type = LightType.Point;
                    pulse.color = Geo.Glint;
                    pulse.range = 14f;
                    pulse.intensity = 2.2f;
                }

                // Seam hint: gold glitter shown when the seam runs to this rock.
                GameObject seam = TryModel("KayKit/Resource/Gold_Nugget_Small", grp, new Vector3(-0.3f, 1.1f, -0.2f), 1.5f, -1f);
                if (seam == null)
                {
                    seam = Geo.MeshObject("Seam", Geo.Icosahedron(0.3f), Geo.Marker, grp);
                    seam.transform.localPosition = new Vector3(-0.3f, 1.15f, -0.2f);
                }
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
