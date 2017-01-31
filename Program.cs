using System;

namespace ProceduralWorld
{
    class MainClass
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Generating...");

            WorldGenerator generator = new WorldGenerator(123);
            var task = generator.GenerateMapAsync("out");
            generator.OnGenerationProgress += (s, progress) => {
                Console.CursorLeft = 0;
                Console.Write($"{progress}%  ");
            };
            generator.OnGenerationInfo += (s, info) => {
                Console.CursorLeft = 20;
                Console.Write(info);
            };
            task.Wait();
            Console.WriteLine();
            Console.WriteLine("Finished");
        }
    }
}
