using System;
using System.IO.Compression;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace Zlib.Benchmark
{
    class Benchmark
    {
        static void PrintPressBaseCompressedSize<TPressBase>(CompressionLevel level)
            where TPressBase : PressBase, new()
        {
            int byteCount = PressBase.ByteCount1;
            var systemDeflate = new TPressBase
            {
                ByteCount = byteCount,
                Level = level
            };
            systemDeflate.Setup();
            
            long written = systemDeflate.Compress();
            Console.WriteLine(
                $"Compressed size of {typeof(TPressBase).Name}, " +
                $"{level} level: {byteCount} -> {written}");
        }

        static void Main(string[] args)
        {
            PrintPressBaseCompressedSize<SystemDeflate>(CompressionLevel.Fastest);
            PrintPressBaseCompressedSize<IonicDeflate>(CompressionLevel.Fastest);

            PrintPressBaseCompressedSize<SystemDeflate>(CompressionLevel.Optimal);
            PrintPressBaseCompressedSize<IonicDeflate>(CompressionLevel.Optimal);
            Console.WriteLine();
            
            var pressSwitcher = BenchmarkSwitcher.FromTypes(
                new[] {
                    typeof(SystemDeflate),
                    typeof(IonicDeflate)
                });

            var job = Job.Default
                .WithToolchain(new InProcessNoEmitToolchain(true))
                //.WithEvaluateOverhead(true)
                //.WithGcForce(true)
                ;
            
            var config = new ManualConfig()
                .AddJob(job)
                .AddColumnProvider(
                    new CompositeColumnProvider(DefaultColumnProviders.Instance))
                .AddLogger(new ConsoleLogger())
                .AddExporter(new HtmlExporter())
                .AddDiagnoser(MemoryDiagnoser.Default);
            
            pressSwitcher.RunAllJoined(config);

        }
    }
}
