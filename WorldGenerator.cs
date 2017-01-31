using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Debug = System.Diagnostics.Debug;
using GlmSharp;

namespace ProceduralWorld
{
    /// <summary>
    /// Generates the large-scale game map.
    /// </summary>
    public partial class WorldGenerator
    {

        public static bool CurrentlyGenerating = false;

        ivec2 MapSize = new ivec2(2048, 2048);

        public WorldGenerator(int seed)
        {
            rand = new Random(seed);
        }

        /// <summary>
        /// Asynchronously Generates the world map.and returns the filename of an image of the map.
        /// </summary>
        /// <returns></returns>
        public Task GenerateMapAsync(string fileName)
        {
            string fileSuffix = fileName + ".png";

            string miniMapDir = "minimap/";
            string miniMapPath = miniMapDir+"map"+fileSuffix;
            string miniMapUrl = ""+fileSuffix;
            string heightMapDir = "./";
            string heightMapPath = heightMapDir + fileSuffix;

            if (!Directory.Exists(miniMapDir))
                Directory.CreateDirectory(miniMapDir);
            if (!Directory.Exists(heightMapDir))
                Directory.CreateDirectory(heightMapDir);

            var task = Task.Run(() =>
            {
                try
                {
                    DateTime startTime = DateTime.Now;
                    UpdateGenerationInfo("Simulating Tectonics");
                    var plates = GeneratePlates();
                    var jitteredPlates = JitterPlates(plates);
                    UpdateProgress(0.1f);

                    var map = GenerateHeightMap(jitteredPlates, 0.1f, 0.4f);

                    // Convert to heightmap array for better performance
                    float[,] heightMap = new float[MapSize.x, MapSize.y];
                    for (int x = 0; x < MapSize.x; x++)
                    {
                        if(x % 100 == 0)
                        {
                            UpdateProgress(0.1f + (0.4f * (float)x/MapSize.x));
                        }
                        
                        for (int y = 0; y < MapSize.y; y++)
                        {
                            float h = map[x, y].Height;

                            heightMap[x, y] = h;
                        }
                    }
                    UpdateGenerationInfo("Post-Processing");

                    heightMap = FilterHeightmap(heightMap);

                    MakeBorderFlat(heightMap, 0);

                    if (DebugErosion)
                    {
                        SaveHeightmap(heightMap, "heightmap.png", 5f, 5f);
                        SaveBiomeImage(heightMap, "preErosion.png");
                    }
                    UpdateProgress(0.5f);

                    UpdateGenerationInfo("Simulating Erosion");

                    heightMap = Erode(heightMap, 0.5f, 0.95f);

                    MakeBorderFlat(heightMap, 0);
                    MakeBorderFlat(moisture, 1);
                    MakeBorderFlat(totalErosion, 0);

                    if (DebugErosion)
                    {
                        SaveBiomeImage(heightMap, "postErosion.png");
                        SaveHeightmap(heightMap, "heightmap.png", -5f, 5f);
                    }

                    UpdateGenerationInfo("Saving Images");

                    SaveBiomeImage(heightMap, miniMapPath);
                    UpdateProgress(0.97f);
                    SaveHeightmapChannels(heightMap, moisture, totalErosion, heightMapPath, -8f, 8f);
                    UpdateProgress(1f);

                    double duration = (DateTime.Now - startTime).TotalSeconds;

                    UpdateGenerationInfo(string.Format("Generation Finished in {0:0.00} seconds", duration));

                    var finishHandler = OnFinish;
                    if (finishHandler != null)
                        OnFinish(this, miniMapUrl);

                    CurrentlyGenerating = false;

                }
                catch (Exception e)
                {
                    UpdateProgress(1f);
                    UpdateGenerationInfo("Error while generating world.");
                    Console.WriteLine("World Generation failed: " + e.Message);
                    throw;
                }
            });

            return task;
        }

        private int progressCounter = 0;

        private void UpdateProgress(float newProgress)
        {
            if (newProgress > 1)
                newProgress = 1;
            if (newProgress < 0)
                newProgress = 0;

            progressCounter = (int)Math.Floor(newProgress * 100);

            var progressHandler = OnGenerationProgress;
            if (progressHandler != null)
                progressHandler(this, progressCounter);
        }

        private void UpdateGenerationInfo(string info)
        {
            var handler = OnGenerationInfo;
            if (handler != null)
                handler(this, info);
        }

        public event EventHandler<int> OnGenerationProgress;
        public event EventHandler<string> OnGenerationInfo;
        public event EventHandler<string> OnFinish;

        public void SaveMapImage(float[,] heightMap, string filename)
        {
            Bitmap bmp = new Bitmap(MapSize.x, MapSize.y, PixelFormat.Format32bppArgb);

            for (int x = 2; x < MapSize.x - 2; x++)
                for (int y = 2; y < MapSize.y - 2; y++)
                {
                    {
                        float h = heightMap[x, y];
                        if (float.IsNaN(h))
                            bmp.SetPixel(x, y, Color.Fuchsia);
                        else
                        {

                            Color c = Color.Red;

                            /*if (h < 0f)
                                c = Color.DeepSkyBlue;
                            else */
                            if (h < 1.5f)
                                c = Color.ForestGreen;
                            else if (h < 2.5f)
                                c = Color.LimeGreen;
                            else if (h < 3.5f)
                                c = Color.Gray;
                            else
                                c = Color.White;

                            vec2 grad = Gradient(heightMap, new ivec2(x, y));

                            if (grad.x > 100 || grad.x < -100 || grad.y > 100 || grad.y < -100)
                                grad = vec2.Zero;

                            if (grad.Length > 0.05f)
                                grad = grad.Normalized;

                            float light = Math.Min(1, Math.Max(0, 0.2f + glm.Dot(grad, new vec2(0, -1))));

                            //if (h < 0f)
                            //    light = 1f;

                            if (float.IsNaN(light))
                            {
                                bmp.SetPixel(x, y, Color.Fuchsia);
                            }
                            else
                            {
                                c = Color.FromArgb(
                                    (int)(c.R * (light * 0.5 + 0.5)),
                                    (int)(c.G * (light * 0.5 + 0.5)),
                                    (int)(c.B * (light * 0.5 + 0.5))
                                    );

                                bmp.SetPixel(x, y, c);
                            }
                        }
                    }
                }

            bmp.Save(filename);
        }

        public void SaveBiomeImage(float[,] heightMap, string filename)
        {
            Bitmap bmp = new Bitmap(MapSize.x, MapSize.y, PixelFormat.Format32bppArgb);

            for (int x = 2; x < MapSize.x - 2; x++)
                for (int y = 2; y < MapSize.y - 2; y++)
                {
                    {
                        float h = heightMap[x, y];
                        if (float.IsNaN(h))
                            bmp.SetPixel(x, y, Color.Fuchsia);
                        else
                        {

                            Color c = Color.Red;

                            if (h < 0f)
                                c = Color.DeepSkyBlue;
                            else if ((moisture != null && moisture[x, y] > 0.1f * averageMoisture) || (h < 1f && Gradient(heightMap, new ivec2(x, y)).Length < 1.0f))
                            {
                                if (h < 0.3f)
                                    c = Color.Khaki;
                                else
                                    c = Color.ForestGreen;
                            }
                            else
                            {
                                if (h < 1.5f)
                                    c = Color.DarkGray;
                                else if (h < 3.5f)
                                    c = Color.LightGray;
                                else c = Color.Snow;
                            }

                            vec2 grad = Gradient(heightMap, new ivec2(x, y));

                            if (grad.x > 100 || grad.x < -100 || grad.y > 100 || grad.y < -100)
                                grad = vec2.Zero;

                            if (grad.Length > 0.05f)
                                grad = grad.Normalized;

                            float light = Math.Min(1, Math.Max(0, 0.2f + glm.Dot(grad, new vec2(0, -1))));

                            if (h < 0f)
                                light = 1f;

                            if (float.IsNaN(light))
                            {
                                bmp.SetPixel(x, y, Color.Fuchsia);
                            }
                            else
                            {
                                c = Color.FromArgb(
                                    (int)(c.R * (light * 0.5 + 0.5)),
                                    (int)(c.G * (light * 0.5 + 0.5)),
                                    (int)(c.B * (light * 0.5 + 0.5))
                                    );

                                bmp.SetPixel(x, y, c);
                            }
                        }
                    }
                }

            bmp.Save(filename);
        }

        public void SaveHeightmap(float[,] heightMap, string filename, float minR = 0f, float maxR = 1f)
        {
            short[] data = new short[MapSize.x * MapSize.y * 3];

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    {
                        float h = (heightMap[x, y] - minR) / (maxR - minR);
                        short c = (short)Math.Max(0.1f, Math.Min(h * Int16.MaxValue, Int16.MaxValue));
                        data[(y * MapSize.x + x) * 3 + 0] = c;
                        data[(y * MapSize.x + x) * 3 + 1] = c;
                        data[(y * MapSize.x + x) * 3 + 2] = c;
                    }
                }

            Bitmap bmp = new Bitmap(MapSize.x, MapSize.y, PixelFormat.Format48bppRgb);
            var bmpData = bmp.LockBits(new Rectangle(0, 0, MapSize.x, MapSize.y), ImageLockMode.WriteOnly,
                PixelFormat.Format48bppRgb);

            Marshal.Copy(data, 0, bmpData.Scan0, data.Length);

            bmp.UnlockBits(bmpData);

            //ImageCodecInfo codec = GetEncoderInfo("image/png");
            //var parameters = new EncoderParameters();
            //parameters.Param[0] = new EncoderParameter(Encoder.ColorDepth, 48);

            bmp.Save(filename);
        }


        public void SaveHeightmapChannels(float[,] red, float[,] green, float[,] blue, string filename, float minR = 0f, float maxR = 1f, float minG = 0f, float maxG = 1f, float minB = 0f, float maxB = 1f)
        {
            return;
            short[] data = new short[MapSize.x * MapSize.y * 3];

            for (int x = 0; x < MapSize.x; x++)
                for (int y = 0; y < MapSize.y; y++)
                {
                    {
                        float r = (red[x, y] - minR) / (maxR - minR);
                        float g = (green[x, y] - minG) / (maxG - minG);
                        float b = (blue[x, y] - minB) / (maxB - minB);
                        short cr = (short)Math.Max(0.1f, Math.Min(r * Int16.MaxValue, Int16.MaxValue));
                        short cg = (short)Math.Max(0.1f, Math.Min(g * Int16.MaxValue, Int16.MaxValue));
                        short cb = (short)Math.Max(0.1f, Math.Min(b * Int16.MaxValue, Int16.MaxValue));
                        data[(y * MapSize.x + x) * 3 + 0] = cb;
                        data[(y * MapSize.x + x) * 3 + 1] = cg;
                        data[(y * MapSize.x + x) * 3 + 2] = cr;
                    }
                }

            Bitmap bmp = new Bitmap(MapSize.x, MapSize.y, PixelFormat.Format48bppRgb);
            var bmpData = bmp.LockBits(new Rectangle(0, 0, MapSize.x, MapSize.y), ImageLockMode.WriteOnly,
                PixelFormat.Format48bppRgb);

            Marshal.Copy(data, 0, bmpData.Scan0, data.Length);

            bmp.UnlockBits(bmpData);

            //ImageCodecInfo codec = GetEncoderInfo("image/png");
            //var parameters = new EncoderParameters();
            //parameters.Param[0] = new EncoderParameter(Encoder.ColorDepth, 48);

            bmp.Save(filename);
        }

        private static ImageCodecInfo GetEncoderInfo(String mimeType)
        {
            int j;
            ImageCodecInfo[] encoders;
            encoders = ImageCodecInfo.GetImageEncoders();
            for (j = 0; j < encoders.Length; ++j)
            {
                if (encoders[j].MimeType == mimeType)
                    return encoders[j];
            }
            return null;
        }

        Random rand;

        /// <summary>
        ///  Size of the grid in which the tectonic plates are generated (in pixels)
        /// </summary>
        private int PlateSize = 120;

        /// <summary>
        /// Maximum drift vector length of tectonic plates (in pixels)
        /// </summary>
        private float maxPlateLinearVelocity = 1f;

        /// <summary>
        /// Maximum rotational drift angle of tectonic plates (in radians)
        /// </summary>
        private float maxPlateAngluarVelocity = 0.3f;


    }
}
