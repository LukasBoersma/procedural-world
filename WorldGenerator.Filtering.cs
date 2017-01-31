using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GlmSharp;

namespace ProceduralWorld
{
    public partial class WorldGenerator
    {
        public float[,] FilterHeightmap(float[,] map)
        {
            map = MeanFilter(map);
            map = MeanFilter(map);
            map = ConvolutionFilter(map);
            map = ConvolutionFilter(map);

            return map;
        }

        public float[,] MeanFilter(float[,] map)
        {
            float[,] filteredMap = new float[MapSize.x, MapSize.y];

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    List<float> values = new List<float>();
                    for (int i = 0; i < filterMask.Length; i++)
                    {
                        int dx = x + filterMask[i].x;
                        int dy = y + filterMask[i].y;

                        if (dx >= 0 && dx < MapSize.x &&
                            dy >= 0 && dy < MapSize.y)
                            values.Add(map[dx, dy]);
                    }

                    values.Sort();
                    filteredMap[x, y] = values[values.Count / 2];
                }

            return filteredMap;
        }

        public float[,] ConvolutionFilter(float[,] map)
        {
            float[,] filteredMap = new float[MapSize.x, MapSize.y];

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    float acc = 0f;
                    for (int i = 0; i < filterMask.Length; i++)
                    {
                        int dx = x + filterMask[i].x;
                        int dy = y + filterMask[i].y;

                        if (dx >= 0 && dx < MapSize.x &&
                            dy >= 0 && dy < MapSize.y)
                            acc += map[dx, dy];
                    }

                    filteredMap[x, y] = acc / filterMask.Length;
                }

            return filteredMap;
        }

        public float Convolute(float[,] map, int x, int y)
        {
            float acc = 0f;
            for (int i = 0; i < filterMask.Length; i++)
            {
                int dx = x + filterMask[i].x;
                int dy = y + filterMask[i].y;

                if (dx >= 0 && dx < MapSize.x &&
                    dy >= 0 && dy < MapSize.y)
                    acc += map[dx, dy];
            }

            return acc / filterMask.Length;
        }



        private static readonly ivec2[] filterMask = {
                                                             new ivec2(-1, -1),
                                                             new ivec2(-1, 0),
                                                             new ivec2(-1, 1),
                                                             new ivec2(0, -1),
                                                             new ivec2(0, 0),
                                                             new ivec2(0, 1),
                                                             new ivec2(-1, -1),
                                                             new ivec2(0, 0),
                                                             new ivec2(1, 1),
                                                             new ivec2(0, -2),
                                                             new ivec2(-2, 0), 
                                                             new ivec2(0, 2), 
                                                             new ivec2(2, 0)
                                                         };

        private void MakeBorderFlat(float[,] heightMap, float constValue)
        {
            float BorderSize = Math.Min(MapSize.x, MapSize.y) / 10f;

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    float distLeft = x;
                    float distBottom = y;
                    float distTop = MapSize.y - y;
                    float distRight = MapSize.x - x;

                    float minDist = Math.Min(Math.Min(Math.Min(distLeft, distBottom), distTop), distRight);

                    minDist = minDist/BorderSize;

                    float flatteningFactor = (float)Math.Sin(Math.Min(1f, minDist*minDist)*(float)Math.PI*0.5f);

                    heightMap[x, y] = flatteningFactor * heightMap[x, y] + (1 - flatteningFactor) * constValue;
                }
        }

    }
}
