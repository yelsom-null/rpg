using System.Collections.Generic;
using UnityEngine;

namespace Elderholt
{
    // ============================================================================
    //  TileMap — the movement doc's ground truth: the server thinks in 1 m square
    //  tiles; the client renders continuous motion on top. One walkability grid
    //  per band, mirroring the world builder's geometry (walls with gate gaps,
    //  buildings, pond water, the quarry rim). Pathing is A*, 8-directional with
    //  no corner-cutting, capped per click so long journeys stay re-clicks.
    // ============================================================================
    public static class TileMap
    {
        public const int Size = 112;          // tiles per side, centred on the band origin
        public const int PathCap = 25;        // tiles per click (design constant)
        const int SearchBudget = 4000;        // A* expansion guard

        static bool[][,] walk;

        static float OffX(int band) => Bands.All[band].originX - Size / 2f;
        static float OffZ(int band) => Bands.All[band].originZ - Size / 2f;

        public static Vector2Int ToTile(int band, float x, float z)
        {
            return new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(x - OffX(band)), 0, Size - 1),
                Mathf.Clamp(Mathf.FloorToInt(z - OffZ(band)), 0, Size - 1));
        }

        public static Vector2 ToWorld(int band, Vector2Int t)
        {
            return new Vector2(OffX(band) + t.x + 0.5f, OffZ(band) + t.y + 0.5f);
        }

        public static bool Walkable(int band, Vector2Int t)
        {
            Ensure();
            return t.x >= 0 && t.y >= 0 && t.x < Size && t.y < Size && walk[band][t.x, t.y];
        }

        static void Ensure()
        {
            if (walk != null) return;
            walk = new bool[Bands.Count][,];
            for (int b = 0; b < Bands.Count; b++)
            {
                walk[b] = new bool[Size, Size];
                for (int i = 0; i < Size; i++)
                    for (int j = 0; j < Size; j++)
                    {
                        float x = OffX(b) + i + 0.5f, z = OffZ(b) + j + 0.5f;
                        walk[b][i, j] = b == 0 ? Surface(x, z) : Chamber(b, x, z);
                    }
            }
        }

        static bool Chamber(int b, float x, float z)
        {
            BandDef d = Bands.All[b];
            float dx = x - d.originX, dz = z - d.originZ;
            if (dx * dx + dz * dz > d.radius * d.radius) return false;
            if (Mathf.Abs(dx) < 0.8f && Mathf.Abs(dz) < 0.8f) return false;   // the shaft ladder
            return true;
        }

        // Band 0 walkability mirrors ThornmereWorld's Bracken Cross geometry.
        static bool Surface(float x, float z)
        {
            if (Mathf.Abs(x) > 54f || Mathf.Abs(z) > 54f) return false;

            // City walls, with the N/S gate gaps at |x| < 2.
            bool nsWall = Mathf.Abs(Mathf.Abs(z) - 14f) < 0.9f && Mathf.Abs(x) <= 18.6f && Mathf.Abs(x) >= 2.2f;
            bool ewWall = Mathf.Abs(Mathf.Abs(x) - 18f) < 0.9f && Mathf.Abs(z) <= 14.6f;
            if (nsWall || ewWall) return false;

            if (Block(x, z, -10f, 9f, 7.5f, 4.5f)) return false;      // guildhall
            if (Block(x, z, 10f, 9f, 6f, 4f)) return false;           // the Vault
            if (Block(x, z, -14f, -9f, 2.2f, 2f)) return false;       // High Street shop
            if (Block(x, z, -14f, -6.3f, 2.2f, 2f)) return false;     // High Street shop
            if (Block(x, z, 1f, -9.5f, 1.6f, 1.6f)) return false;     // furnace
            if (Block(x, z, 4.5f, -9.5f, 1.3f, 0.7f)) return false;   // anvil

            // Mirror Pond is water (swimming is an open question; blocked for now).
            if ((x + 30f) * (x + 30f) + (z - 2f) * (z - 2f) < 7.5f * 7.5f) return false;

            // Grey Quarry rim boulders, with the western opening toward the road.
            float qx = x - 33f, qz = z + 1f;
            float qd = Mathf.Sqrt(qx * qx + qz * qz);
            if (qd > 8.7f && qd < 11.3f && !(qx < -6.5f && Mathf.Abs(qz) < 4.5f)) return false;

            return true;
        }

        static bool Block(float x, float z, float cx, float cz, float w, float d)
        {
            return Mathf.Abs(x - cx) <= w / 2f + 0.4f && Mathf.Abs(z - cz) <= d / 2f + 0.4f;
        }

        // Nearest walkable tile to t, ring-searching outward (for clicks on water,
        // walls, or the insides of buildings — "never refuse to move at all").
        public static Vector2Int NearestWalkable(int band, Vector2Int t, int maxR = 6)
        {
            if (Walkable(band, t)) return t;
            for (int r = 1; r <= maxR; r++)
                for (int dx = -r; dx <= r; dx++)
                    for (int dz = -r; dz <= r; dz++)
                    {
                        if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue;
                        Vector2Int c = new Vector2Int(t.x + dx, t.y + dz);
                        if (Walkable(band, c)) return c;
                    }
            return t;
        }

        // A*: 8-directional, diagonals disallowed when either adjacent cardinal is
        // blocked (no corner-cutting). Returns the tile sequence excluding the
        // start tile; truncated=true when capped or the goal proved unreachable
        // (the path then leads to the closest approach).
        public static List<Vector2Int> FindPath(int band, Vector2Int start, Vector2Int goal, out bool truncated)
        {
            Ensure();
            truncated = false;
            List<Vector2Int> result = new List<Vector2Int>();
            if (start == goal) return result;
            goal = NearestWalkable(band, goal);

            Dictionary<Vector2Int, Vector2Int> came = new Dictionary<Vector2Int, Vector2Int>();
            Dictionary<Vector2Int, float> gScore = new Dictionary<Vector2Int, float> { [start] = 0f };
            List<Vector2Int> open = new List<Vector2Int> { start };
            HashSet<Vector2Int> closed = new HashSet<Vector2Int>();
            Vector2Int best = start;
            float bestH = Heur(start, goal);
            int expansions = 0;

            while (open.Count > 0 && expansions++ < SearchBudget)
            {
                // Smallest f in the open list (grids this small don't need a heap).
                int bi = 0;
                float bf = float.MaxValue;
                for (int i = 0; i < open.Count; i++)
                {
                    float f = gScore[open[i]] + Heur(open[i], goal);
                    if (f < bf) { bf = f; bi = i; }
                }
                Vector2Int cur = open[bi];
                open.RemoveAt(bi);
                if (closed.Contains(cur)) continue;
                closed.Add(cur);

                float h = Heur(cur, goal);
                if (h < bestH) { bestH = h; best = cur; }
                if (cur == goal) { best = cur; break; }

                for (int dx = -1; dx <= 1; dx++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        Vector2Int nb = new Vector2Int(cur.x + dx, cur.y + dz);
                        if (!Walkable(band, nb) || closed.Contains(nb)) continue;
                        // No corner-cutting on diagonals.
                        if (dx != 0 && dz != 0 &&
                            (!Walkable(band, new Vector2Int(cur.x + dx, cur.y)) ||
                             !Walkable(band, new Vector2Int(cur.x, cur.y + dz)))) continue;

                        float g = gScore[cur] + ((dx != 0 && dz != 0) ? 1.41421f : 1f);
                        if (!gScore.TryGetValue(nb, out float old) || g < old)
                        {
                            gScore[nb] = g;
                            came[nb] = cur;
                            open.Add(nb);
                        }
                    }
            }

            if (best != goal) truncated = true;

            Vector2Int walkBack = best;
            while (walkBack != start)
            {
                result.Add(walkBack);
                walkBack = came[walkBack];
            }
            result.Reverse();

            if (result.Count > PathCap)
            {
                result.RemoveRange(PathCap, result.Count - PathCap);
                truncated = true;
            }
            return result;
        }

        // Octile distance.
        static float Heur(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x), dz = Mathf.Abs(a.y - b.y);
            return Mathf.Max(dx, dz) + 0.41421f * Mathf.Min(dx, dz);
        }
    }
}
