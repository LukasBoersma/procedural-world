using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GlmSharp;

namespace ProceduralWorld
{
    public partial class WorldGenerator
    {

        public class MapPixel
        {
            public TectonicPlate Plate;
            public List<TectonicEdge> Edges = new List<TectonicEdge>();
            public float Height;
            public bool IsCorner = false;
        }

        MapPixel[,] GenerateHeightMap(List<TectonicPlate> plates, float progressMin, float progressMax)
        {
            var map = new MapPixel[MapSize.x, MapSize.y];

            UpdateGenerationInfo("Initialize with random heightmap");

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    map[x, y] = new MapPixel();
                    map[x, y].Height = 0.5f + 0.5f * SimplexNoise.RecursiveNoise.Noise2D(x / 200f, y / 200f, 8);
                }

            var drawnEdges = new HashSet<TectonicEdge>();

            UpdateGenerationInfo("Initializing tectonic edges");

            // Initialize the edges and plate centers
            foreach (var tectonicPlate in plates)
            {
                foreach (var edge in tectonicPlate.Edges)
                {
                    if (drawnEdges.Contains(edge))
                        continue;
                    drawLine(edge, map);
                    drawnEdges.Add(edge);
                }

                int x = tectonicPlate.Center.x;
                int y = tectonicPlate.Center.y;

                if (x >= 0 && x < MapSize.x &&
                    y >= 0 && y < MapSize.y)
                    map[x, y].Plate = tectonicPlate;
            }

            UpdateGenerationInfo("Computing tectonic surface");

            // Flood fill from the plate centers
            bool done = false;
            for (int i = 1; !done; i++)
            {
                done = true;
                for (int x = 0; x < MapSize.x; x++)
                    for (int y = 0; y < MapSize.y; y++)
                    {
                        // Found a pixel without a Plate assigned?
                        MapPixel p = map[x, y];
                        if (p.Plate == null)
                        {
                            TryFillPixel(map, new ivec2(x, y));
                            if (p.Plate != null)
                                done = false;
                        }
                    }
                for (int x = MapSize.x - 1; x > 0; x--)
                    for (int y = MapSize.y - 1; y > 0; y--)
                    {
                        // Found a pixel without a Plate assigned?
                        MapPixel p = map[x, y];
                        if (p.Plate == null)
                        {
                            TryFillPixel(map, new ivec2(x, y));
                            if (p.Plate != null)
                                done = false;
                        }
                    }
            }
            UpdateProgress(progressMin + (progressMax-progressMin) * 0.5f);

            // Find missing plates
            UpdateGenerationInfo("Find unused tectonic plates");
            List<TectonicPlate> missingPlates = FindUnusedPlates(map, plates);

            // Set height values depending on the base heights of the plates and the 
            UpdateGenerationInfo("Set base height of terrain");
            Parallel.For(0, MapSize.x, x =>
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    var p = map[x, y];

                    TectonicEdge bestEdge = null;
                    float bestDistance = Math.Max(MapSize.x, MapSize.y);

                    float baseHeight = -1f;

                    if (p.Plate != null)
                    {
                        baseHeight = p.Plate.BaseHeight;

                        foreach (var edge in p.Plate.Edges)
                        {
                            if (!missingPlates.Contains(edge.LeftPlate) &&
                                !missingPlates.Contains(edge.RightPlate))
                            {
                                float dist = DistanceToEdgeSquared(edge, new ivec2(x, y));
                                if (dist < bestDistance)
                                {
                                    bestDistance = dist;
                                    bestEdge = edge;
                                }
                            }
                        }
                    }

                    float tectonicHeight = 0f;

                    if (bestEdge != null)
                    {
                        tectonicHeight = bestEdge.MountainFactor * 3f * (1f / ((float)Math.Pow(1.2f + 0.02f * bestDistance, 2))) *
                                               (1f + SimplexNoise.RecursiveNoise.Noise2D(250f - x / 200f, 250f - y / 200f, 10));
                    }

                    float noise = SimplexNoise.RecursiveNoise.Noise2D(x / 100f, y / 100f, 8);

                    p.Height = baseHeight + tectonicHeight + noise;
                }
            });

            UpdateProgress(progressMax);

            return map;
        }

        static readonly ivec2[] neighborMask = new ivec2[] { new ivec2(-1, 0), new ivec2(1, 0), new ivec2(0, -1), new ivec2(0, 1) };

        List<TectonicPlate> FindUnusedPlates(MapPixel[,] map, List<TectonicPlate> plates)
        {
            var missingPlates = new List<TectonicPlate>();

            missingPlates.AddRange(plates);

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    MapPixel p = map[x, y];
                    if (p.Plate != null && missingPlates.Contains(p.Plate))
                        missingPlates.Remove(p.Plate);
                }

            return missingPlates;
        }

        void TryFillPixel(MapPixel[,] map, ivec2 pos)
        {
            MapPixel p = map[pos.x, pos.y];

            var plateVotes = new Dictionary<TectonicPlate, int>();

            // Look for a neighbor pixel with a plate assigned
            foreach (ivec2 d in neighborMask)
            {
                int dx = pos.x + d.x;
                int dy = pos.y + d.y;

                if (dx >= 0 && dx < MapSize.x &&
                    dy >= 0 && dy < MapSize.y)
                {
                    TectonicPlate votedPlate = null;
                    if (map[dx, dy].Plate != null && map[dx, dy].Edges.Count == 0)
                    {
                        votedPlate = map[dx, dy].Plate;
                    }

                    if (votedPlate == null)
                        continue;

                    if (!plateVotes.ContainsKey(votedPlate))
                        plateVotes[votedPlate] = 1;
                    else
                        plateVotes[votedPlate] += 1;
                }
            }

            if (plateVotes.Count == 0)
                return;

            p.Plate = (from plateVote in plateVotes orderby plateVote.Value descending select plateVote.Key).First();
        }

        TectonicPlate SelectSide(ivec2 position, List<TectonicEdge> edges)
        {
            var plateVotes = new Dictionary<TectonicPlate, int>();

            foreach (var edge in edges)
            {
                float angle = EdgeAngle(position, edge.A, edge.B);

                TectonicPlate votedPlate = angle > 0 ? edge.RightPlate : edge.LeftPlate;

                if (!plateVotes.ContainsKey(votedPlate))
                    plateVotes[votedPlate] = 1;
                else
                    plateVotes[votedPlate] += 1;
            }

            return (from plateVote in plateVotes orderby plateVote.Value descending select plateVote.Key).First();
        }

        float EdgeAngle(ivec2 position, ivec2 a, ivec2 b)
        {
            if (position == a)
                return 0;

            ivec2 edgeVec = b - a;
            ivec2 edgeNormal = new ivec2(-edgeVec.y, edgeVec.x);

            ivec2 pDelta = position - a;

            vec2 edgeNormalF = new vec2(edgeNormal.x, edgeNormal.y);
            vec2 pDeltaF = new vec2(pDelta.x, pDelta.y);

            return glm.Dot(edgeNormalF.Normalized, pDeltaF.Normalized);
        }

        float DistanceToEdgeSquared(TectonicEdge edge, ivec2 position)
        {
            vec2 v = new vec2(edge.A.x, edge.A.y);
            vec2 w = new vec2(edge.B.x, edge.B.y);

            vec2 p = new vec2(position.x, position.y);

            // Return minimum distance between line segment vw and point p
            float l2 = (v - w).LengthSqr;  // i.e. |w-v|^2 -  avoid a sqrt
            if (l2 == 0f) return (p - v).LengthSqr;   // v == w case
            // Consider the line extending the segment, parameterized as v + t (w - v).
            // We find projection of point p onto the line. 
            // It falls where t = [(p-v) . (w-v)] / |w-v|^2
            float t = glm.Dot(p - v, w - v) / l2;
            if (t < 0) return (p - v).LengthSqr;       // Beyond the 'v' end of the segment
            else if (t > 1) return (p - w).LengthSqr;  // Beyond the 'w' end of the segment
            vec2 projection = v + t * (w - v);  // Projection falls on the segment
            return (p - projection).LengthSqr;
        }

        void drawLine(TectonicEdge edge, MapPixel[,] map)
        {
            int x0 = edge.A.x;
            int y0 = edge.A.y;
            int x1 = edge.B.x;
            int y1 = edge.B.y;
            int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy, e2; // error value e_xy

            if(dx > map.GetLength(0) * 4 || dy > map.GetLength(1) * 4)
            {
                //TODO: Investigate broken edge coordinates
                return;
            }

            for (; ; )
            {
                if (x0 >= 0 && x0 < MapSize.x &&
                    y0 >= 0 && y0 < MapSize.y)
                {
                    map[x0, y0].Edges.Add(edge);
                    map[x0, y0].Plate = edge.LeftPlate;
                }

                if (x0 == x1 && y0 == y1) break;

                e2 = 2 * err;

                // horizontal step?
                if (e2 > dy)
                {
                    err += dy;
                    x0 += sx;
                }
                else if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
