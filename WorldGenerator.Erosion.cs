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
        private const bool DebugErosion = false;

        // Time delta between simulation steps (in seconds)
        private float StepSize = 0.04f;

        // How many rain rounds should there be?
        private const int RainRounds = 10;

        // How long should each round last? (in seconds)
        private const float RainRoundDuration = 3f;

        // How many rain drops should be dropped per pixel in the heightmap in each rain round?
        private const float RainDropsPerPixel = 0.02f;

        // Radius of a single rain drop
        private const int RainDropRadius = 7;

        // Water height added by a rain drop
        private const float RainDropHeight = 0.015f;

        // Gravity constant for water simulation.
        private const float Gravity = 20f;

        private const float ErosionFactor = 1f;
        private const float DepositionFactor = 2f;
        private const float SoilCapacity = 0.2f;

        private const float EvaporationPerSecond = 0.002f;

        private const float MaxFlux = 1f;
        private const float MaxWaterHeight = 1f;

        // Height of the terrain
        private float[,] soilHeight;

        // Height of the water, relative to soilHeight
        private float[,] waterHeight;

        // Volume of soil currently dissolved in the water
        private float[,] soilVolume;

        // Buffer for soilVolume advection double buffering
        private float[,] soilVolumeNew;

        // Velocity of water
        private vec2[,] velocity;

        // Outgoing flux to the right cell
        private float[,] fluxRight;

        // Outgoing flux to the top cell
        private float[,] fluxTop;

        // Total amount of soil taken away per pixel
        private float[,] totalErosion;

        private float[,] totalDeposition;

        // Sum of water heights over the whole simulation time per pixel
        private float[,] moisture;

        // Average value of moisture
        private float averageMoisture = 1f;

        public float[,] Erode(float[,] map, float progressMin, float progressMax)
        {
            float maxDuration = RainRounds * RainRoundDuration;
            int maxSteps = (int)(maxDuration/StepSize);

            int dropsPerRound = (int)(MapSize.x * MapSize.y * RainDropsPerPixel);

            soilHeight = map;

            waterHeight = new float[MapSize.x, MapSize.y];

            // Volume of soil currently dissolved in the water
            soilVolume = new float[MapSize.x, MapSize.y];
            soilVolumeNew = new float[MapSize.x, MapSize.y];

            // Velocity of water
            velocity = new vec2[MapSize.x, MapSize.y];

            fluxRight = new float[MapSize.x, MapSize.y];
            fluxTop = new float[MapSize.x, MapSize.y];

            totalErosion = new float[MapSize.x, MapSize.y];
            totalDeposition = new float[MapSize.x, MapSize.y];
            moisture = new float[MapSize.x, MapSize.y];

            // Initialize the arrays
            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    waterHeight[x, y] = 0f;
                    soilVolume[x, y] = 0f;
                    soilVolumeNew[x, y] = 0f;
                    velocity[x, y] = vec2.Zero;
                    fluxRight[x, y] = 0f;
                    fluxTop[x, y] = 0f;
                    totalErosion[x, y] = 0f;
                    moisture[x, y] = 0f;
                }

            float timeUntilRain = 0f;
            float t = 0f;
            for (int step = 0; step < maxSteps; step++)
            {
                if (maxSteps - step < 50)
                    StepSize *= 0.95f;

                t += StepSize;
                timeUntilRain -= StepSize;

                UpdateProgress(progressMin + (progressMax - progressMin) * ((float)step / maxSteps));

                // Spawn new rain drops?
                if (timeUntilRain <= 0f)
                {
                    SpawnRainDrops(dropsPerRound);
                    timeUntilRain = RainRoundDuration;

                    SmoothMoistAreas();
                }

                UpdateFlux();
                UpdateVelocity();
                UpdateWaterHeight();
                UpdateErosion();

                if (step % 100 == 0 && DebugErosion)
                {
                    SaveHeightmap(waterHeight, "water" + step.ToString() + ".png");
                    SaveMapImage(soilHeight, "height" + step.ToString() + ".png");
                }

                step++;
            }

            FinishErosion();

            if (DebugErosion)
            {
                SaveHeightmap(totalErosion, "totalErosion.png");
                SaveHeightmap(totalDeposition, "totalDeposition.png");
                SaveHeightmap(moisture, "moisture.png");
                SaveMapImage(soilHeight, "heightFinish.png");
            }

            return map;
        }

        void SpawnRainDrops(int count)
        {
            for (int i = 0; i < count; i++)
            {
                ivec2 p = new ivec2(rand.Next(0, MapSize.x), rand.Next(0, MapSize.y));
                for (int x = p.x - RainDropRadius; x < p.x + RainDropRadius; x++)
                    for (int y = p.y - RainDropRadius; y < p.y + RainDropRadius; y++)
                    {
                        if (IsSamplePosLegal(x, y, 2))
                            waterHeight[x, y] += RainDropHeight;
                    }
            }
        }

        void UpdateFlux()
        {
            int total = 0;
            Parallel.For(0, MapSize.x, x =>
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    total++;
                    if (IsSamplePosLegal(x, y, 2))
                    {
                        {
                            // Right
                            float a = -Gravity*(AbsWaterHeight(x + 1, y) - AbsWaterHeight(x, y));
                            float newFlux = fluxRight[x, y] + a*StepSize;

                            newFlux = Math.Max(-MaxFlux, Math.Min(newFlux, MaxFlux));

                            if ((newFlux > 0 && waterHeight[x, y] <= 0f)
                                || (newFlux < 0 && waterHeight[x + 1, y] <= 0f))
                                newFlux = 0f;

                            fluxRight[x, y] = newFlux;
                        }
                        {
                            // Top
                            float a = -Gravity*(AbsWaterHeight(x, y + 1) - AbsWaterHeight(x, y));
                            float newFlux = fluxTop[x, y] + a*StepSize;

                            newFlux = Math.Max(-MaxFlux, Math.Min(newFlux, MaxFlux));

                            if ((newFlux > 0 && waterHeight[x, y] <= 0f)
                                || (newFlux < 0 && waterHeight[x, y + 1] <= 0f))
                                newFlux = 0f;

                            fluxTop[x, y] = newFlux;
                        }
                    }
                    else
                    {
                        fluxTop[x, y] = 0f;
                        fluxRight[x, y] = 0f;
                    }
                }
            });
        }

        private void UpdateVelocity()
        {
            Parallel.For(0, MapSize.x, x =>
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    if (IsSamplePosLegal(x, y, 2))
                    {
                        velocity[x, y] = new vec2(
                            fluxRight[x, y] - FluxLeft(x, y),
                            fluxTop[x, y] - FluxBottom(x, y)
                            );
                    }
                    else
                    {
                        velocity[x, y] = vec2.Zero;
                    }
                }
            });
        }

        private void UpdateWaterHeight()
        {
            Parallel.For(0, MapSize.x, x =>
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    if (IsSamplePosLegal(x, y, 2))
                    {
                        float dh = -1f*StepSize*(
                            fluxRight[x, y] +
                            fluxTop[x, y] +
                            FluxLeft(x, y) +
                            FluxBottom(x, y)
                            );

                        float hOld = waterHeight[x, y];
                        float hNew = hOld + dh;

                        if (hNew < 0f)
                            hNew = 0f;

                        hNew = Math.Max(0f, hNew - EvaporationPerSecond*StepSize);
                        hNew = Math.Min(hNew, MaxWaterHeight);

                        waterHeight[x, y] = hNew;
                        moisture[x, y] += hNew;
                    }
                    else
                    {
                        waterHeight[x, y] = 0f;
                    }
                }
            });
        }

        private void UpdateErosion()
        {
            Parallel.For(0, MapSize.x, x =>
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    if (IsSamplePosLegal(x, y, 2))
                    {
                        float oldVolume = soilVolume[x, y];

                        ivec2 p = new ivec2(x, y);

                        vec2 g = Gradient(soilHeight, p);
                        float gl = g.Length;

                        float capacity = SoilCapacity*(gl + 0.02f)*velocity[x, y].Length;

                        float erosion = capacity - oldVolume;
                        erosion *= erosion > 0 ? ErosionFactor : DepositionFactor;

                        float newVolume = oldVolume + erosion;

                        if (newVolume < 0f)
                        {
                            erosion -= newVolume;
                            newVolume = 0f;
                        }

                        soilVolume[x, y] = newVolume;
                        soilHeight[x, y] -= erosion;
                        totalErosion[x, y] += Math.Max(0, erosion);
                        totalDeposition[x, y] += Math.Max(0, -erosion);
                    }
                }
            });

        // Advection
            Parallel.For(0, MapSize.x, x =>
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    vec2 v = velocity[x, y];
                    vec2 advectPos = new vec2(x - (v.x*StepSize), y - (v.y*StepSize));

                    if (IsSamplePosLegal(advectPos, 2))
                        soilVolumeNew[x, y] = Sample(soilVolume, advectPos);
                    else
                        soilVolumeNew[x, y] = 0f;
                }
            });
            // Swap buffers
            float[,] buf = soilVolume;
            soilVolume = soilVolumeNew;
            soilVolumeNew = buf;
        }

        void FinishErosion()
        {
            float maxMoisture = 1f;
            float maxErodedness = 1f;
            float maxDeposition = 1f;

            for (int x = 0; x < MapSize.x; x++)
            {
                for (int y = 0; y < MapSize.y; y++)
                {
                    soilHeight[x, y] += soilVolume[x, y];

                    if (totalErosion[x, y] > maxErodedness)
                        maxErodedness = totalErosion[x, y];

                    if (totalDeposition[x, y] > maxDeposition)
                        maxDeposition = totalDeposition[x, y];

                    if (moisture[x, y] > maxMoisture)
                        maxMoisture = moisture[x, y];
                }
            }

            averageMoisture = 0f;

            // Scale totalErosion and moisture to [0,1]
            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    totalErosion[x, y] = totalErosion[x, y] / maxErodedness;
                    totalDeposition[x, y] = totalDeposition[x, y] / maxDeposition;
                    moisture[x, y] = moisture[x, y] / maxMoisture;
                    averageMoisture += moisture[x, y];
                }

            averageMoisture = averageMoisture/(MapSize.x*MapSize.y);

        }

        void SmoothMoistAreas()
        {
            float averageMoisture = 0f;

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    averageMoisture += moisture[x, y];
                }

            averageMoisture = averageMoisture / (MapSize.x * MapSize.y);

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    if (IsSamplePosLegal(x, y, 2))
                    {
                        if (Convolute(moisture, x, y) > 0.75f * averageMoisture)
                        {
                            soilHeight[x, y] = Convolute(soilHeight, x, y);
                            waterHeight[x, y] = Convolute(waterHeight, x, y);
                            soilVolume[x, y] = Convolute(soilVolume, x, y);
                        }
                    }
                }
        }

        float FluxLeft(int x, int y)
        {
            return -fluxRight[x - 1, y];
        }
        float FluxBottom(int x, int y)
        {
            return -fluxTop[x, y - 1];
        }

        void AddFluxLeft(int x, int y, float v)
        {
            fluxRight[x - 1, y] += -v;
        }
        void AddFluxBottom(int x, int y, float v)
        {
            fluxTop[x, y - 1] += -v;
        }
        void SetFluxLeft(int x, int y, float v)
        {
            fluxRight[x - 1, y] = -v;
        }
        void SetFluxBottom(int x, int y, float v)
        {
            fluxTop[x, y - 1] = -v;
        }

        float AbsWaterHeight(int x, int y)
        {
            return soilHeight[x, y] + waterHeight[x, y];
        }

        bool IsSamplePosLegal(int x, int y, int radius = 0)
        {
            return (x >= 0 + radius && x < MapSize.x - radius &&
                y >= 0 + radius && y < MapSize.y - radius);
        }

        bool IsSamplePosLegal(vec2 p, int radius = 0)
        {
            return (p.x >= 0 + radius && p.x < MapSize.x - radius &&
                p.y >= 0 + radius && p.y < MapSize.y - radius);
        }

        //Samples from a contiuous position with linear interpolation
        float Sample(float[,] map, vec2 p)
        {
            int x0 = (int)p.x;
            int y0 = (int)p.y;
            int x1 = (int)p.x + 1;
            int y1 = (int)p.y + 1;

            float p00 = map[x0, y0];
            float p01 = map[x0, y1];
            float p10 = map[x1, y0];
            float p11 = map[x1, y1];

            float wx = 1f - (p.x - x0);
            float wy = 1f - (p.y - y0);

            float v = wy * (p00 * wx + p10 * (1 - wx)) + (1 - wy) * (p01 * wx + p11 * (1 - wx));
            
            return v;
        }

        float Sample(float[,] map, ivec2 p)
        {
            return map[p.x, p.y];
        }

        void AddDelta(float[,] map, vec2 p, float delta)
        {
            int x0 = (int)p.x;
            int y0 = (int)p.y;
            int x1 = (int)p.x + 1;
            int y1 = (int)p.y + 1;

            float wx = p.x - x0;
            float wy = p.y - y0;

            map[x0, y0] += delta * wx * wy;
            map[x0, y1] += delta * wx * (1 - wy);
            map[x1, y0] += delta * (1 - wx) * wy;
            map[x1, y1] += delta * (1 - wx) * (1 - wy);
        }

        vec2 Gradient(float[,] map, ivec2 p)
        {
            float px0 = Sample(map, p + new ivec2(-1, 0));
            float px1 = Sample(map, p + new ivec2(+1, 0));
            float py0 = Sample(map, p + new ivec2(0, -1));
            float py1 = Sample(map, p + new ivec2(0, +1));

            float dx = px0 - px1;
            float dy = py0 - py1;

            return (new vec2(dx,dy)) * 0.5f;
        }
    }
}
