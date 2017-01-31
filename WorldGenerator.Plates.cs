using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using GlmSharp;

using BenTools.Mathematics;

namespace ProceduralWorld
{
    public partial class WorldGenerator
    {
        public class TectonicEdge
        {
            //      b
            // left | right
            //      a
            public ivec2 A;
            public ivec2 B;
            public TectonicPlate LeftPlate;
            public TectonicPlate RightPlate;
            public float MountainFactor;
        }

        public class TectonicPlate
        {
            public ivec2 Center;
            public List<TectonicEdge> Edges = new List<TectonicEdge>();

            public vec2 LinearVelocity;
            public float AngularVelocity;
            public float BaseHeight;
        }

        /// <summary>
        /// Saves a list of tectonic plates as an annotated SVG image.
        /// </summary>
        void SavePlateImage(List<TectonicPlate> plates, string filename)
        {
            string svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" width=\"100%\" height=\"100%\">";

            foreach (var plate in plates)
            {
                svg += "<circle cx=\"" + plate.Center.x.ToString() + "\" cy=\"" + plate.Center.y.ToString() + "\" r=\"2\" fill=\"blue\" />";

                string centerLabel = "(" + plate.Center.x.ToString() + "," + plate.Center.y.ToString() + ")";
                svg += "<text font-size=\"8\" x=\"" + plate.Center.x.ToString() + "\" y=\"" + plate.Center.y.ToString() + "\" fill=\"black\">" + centerLabel + "</text>";


                foreach (var edge in plate.Edges)
                {
                    if (edge.LeftPlate == plate)
                        continue;

                    ivec2 middle = (edge.A + edge.B) / 2;

                    svg += "<line x1=\"" + edge.A.x.ToString() + "\" y1=\"" + edge.A.y.ToString() + "\" x2=\"" + edge.B.x.ToString() + "\" y2=\"" + edge.B.y.ToString() + "\" style=\"stroke:rgb(255,0,0);stroke-width:2\" />";

                    string edgeLabel = "l(" + edge.LeftPlate.Center.x.ToString() + "," + edge.LeftPlate.Center.y.ToString() + "),r(" + edge.RightPlate.Center.x.ToString() + "," + edge.RightPlate.Center.y.ToString() + ")";
                    svg += "<text font-size=\"8\" x=\"" + middle.x.ToString() + "\" y=\"" + middle.y.ToString() + "\" fill=\"gray\">" +
                           edgeLabel + "</text>";
                }
            }

            svg += "</svg>";

            File.WriteAllText(filename, svg);
        }


        // Create a basic set of 
        public List<TectonicPlate> GeneratePlates()
        {
            // Spawn some random cell centers within a grid.
            // Add one row and column outside of the map so no cells inside the map are border cells.
            List<TectonicPlate> plates = new List<TectonicPlate>();
            for (int left = -PlateSize; left < MapSize.x + PlateSize; left += PlateSize)
                for (int bottom = -PlateSize; bottom < MapSize.y + PlateSize; bottom += PlateSize)
                {
                    int right = left + PlateSize;
                    int top = bottom + PlateSize;
                    plates.Add(new TectonicPlate
                    {
                        Center = new ivec2(rand.Next(left, right), rand.Next(bottom, top)),
                        AngularVelocity = (float)rand.NextDouble() * maxPlateAngluarVelocity,
                        LinearVelocity = new vec2((float)rand.NextDouble() * maxPlateLinearVelocity, (float)rand.NextDouble() * maxPlateLinearVelocity),
                        BaseHeight = (float)rand.NextDouble() + 1f
                    });
                }

            // Compute voronoi triangulation for plate edges
            var plateVectors = new Dictionary<Vector, TectonicPlate>();

            foreach (var tectonicPlate in plates)
            {
                var center = new Vector(tectonicPlate.Center.x, tectonicPlate.Center.y);
                plateVectors[center] = tectonicPlate;
            }

            VoronoiGraph graph = Fortune.ComputeVoronoiGraph(plateVectors.Keys);

            foreach (var edge in graph.Edges)
            {
                ivec2 a = new ivec2((int)edge.VVertexA[0], (int)edge.VVertexA[1]);
                ivec2 b = new ivec2((int)edge.VVertexB[0], (int)edge.VVertexB[1]);

                // Ignore edges into infinity. We generate cells outside of the map so we have only finite edges in the mep
                if (a.x == Int32.MaxValue || a.x == Int32.MinValue
                    || a.y == Int32.MaxValue || a.y == Int32.MinValue
                    || b.x == Int32.MaxValue || b.x == Int32.MinValue
                    || b.y == Int32.MaxValue || b.y == Int32.MinValue)
                    continue;

                a.x = Math.Min(Math.Max(-200, a.x), MapSize.x + 200);
                a.y = Math.Min(Math.Max(-200, a.y), MapSize.y + 200);
                b.x = Math.Min(Math.Max(-200, b.x), MapSize.x + 200);
                b.y = Math.Min(Math.Max(-200, b.y), MapSize.y + 200);

                // left and right cells of the edges given by the fortune voronoi implementation are incorrect, compute the correct cells again

                ivec2 middle = (a + b) / 2;

                // Find the two plate centers closest to the edge middle point
                List<TectonicPlate> neighborCells = new List<TectonicPlate>();
                neighborCells.AddRange(plates.OrderBy(p => (p.Center - middle).Length).Take(2));

                TectonicPlate left = neighborCells[0];
                TectonicPlate right = neighborCells[1];

                // left/right correct?
                if (EdgeAngle(neighborCells[0].Center, a, b) > 0)
                {
                    right = neighborCells[0];
                    left = neighborCells[1];
                }

                float mountainFactor = rand.NextFloat(-1f, 1f);

                var tectonicEdge = new TectonicEdge { A = a, B = b, LeftPlate = left, RightPlate = right, MountainFactor = mountainFactor };

                left.Edges.Add(tectonicEdge);
                right.Edges.Add(tectonicEdge);
            }

            SavePlateImage(plates, "plates.svg");

            return plates;
        }


        List<TectonicPlate> JitterPlates(List<TectonicPlate> plates)
        {
            var newEdges = new List<TectonicEdge>();

            foreach (var tectonicPlate in plates)
            {
                foreach (var edge in tectonicPlate.Edges)
                {
                    // Oly process edges where this plate is the left plate to prevent double processing
                    if (tectonicPlate != edge.LeftPlate)
                        continue;

                    int splits = (int)(edge.A - edge.B).Length / 10;
                    if (splits > 4)
                        splits = 4;

                    newEdges.AddRange(SplitTectonicEdge(edge, edge, splits));
                }

                tectonicPlate.Edges.Clear();
            }

            // Add the new edges back to the Plate.Edges lists
            foreach (var edge in newEdges)
            {
                edge.LeftPlate.Edges.Add(edge);
                edge.RightPlate.Edges.Add(edge);
            }

            return plates;
        }

        List<TectonicEdge> SplitTectonicEdge(TectonicEdge edge, TectonicEdge originalEdge, int depth)
        {
            var subEdges = new List<TectonicEdge>();

            vec2 a = new vec2(edge.A.x, edge.A.y);
            vec2 b = new vec2(edge.B.x, edge.B.y);

            // Randomly move on an axis perpendicular to the original voronoi edge

            // vector of original edge line
            vec2 edgeVector = new vec2(originalEdge.A.x - originalEdge.B.x, originalEdge.A.y - originalEdge.B.y);

            // vectors between left/right plate centers and original.A
            vec2 leftCenterToA = new vec2(edge.A.x - edge.LeftPlate.Center.x, edge.A.y - edge.LeftPlate.Center.y);
            vec2 rightCenterToA = new vec2(edge.A.x - edge.RightPlate.Center.x, edge.A.y - edge.RightPlate.Center.y);

            // project centers to origVector
            float projectedCenterLeft = (glm.Dot(edgeVector, leftCenterToA) / leftCenterToA.LengthSqr);
            float projectedCenterRight = (glm.Dot(edgeVector, rightCenterToA) / rightCenterToA.LengthSqr);

            vec2 movementAxis = new vec2(-edgeVector.y, edgeVector.x);
            movementAxis = movementAxis.Normalized;

            float shift = rand.NextFloat(0.1f, 0.9f);
            vec2 vMid = shift * a + (1f - shift) * b;

            vMid += movementAxis * rand.NextFloat(0f, depth * depth);

            ivec2 middle = new ivec2((int)vMid.x, (int)vMid.y);

            TectonicEdge subEdge1 = new TectonicEdge
            {
                A = edge.A,
                B = middle,
                LeftPlate = edge.LeftPlate,
                RightPlate = edge.RightPlate,
                MountainFactor = edge.MountainFactor
            };

            TectonicEdge subEdge2 = new TectonicEdge
            {
                A = middle,
                B = edge.B,
                LeftPlate = edge.LeftPlate,
                RightPlate = edge.RightPlate,
                MountainFactor = edge.MountainFactor
            };

            if (depth <= 0)
            {
                subEdges.Add(subEdge1);
                subEdges.Add(subEdge2);
            }
            else
            {
                subEdges.AddRange(SplitTectonicEdge(subEdge1, originalEdge, depth - 1));
                subEdges.AddRange(SplitTectonicEdge(subEdge2, originalEdge, depth - 1));
            }

            return subEdges;
        }


    }
}
