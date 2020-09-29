using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.NoEmit;

namespace Zlib.Benchmark
{
    class Benchmark
    {
        static void Main(string[] args)
        {
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
                .AddColumnProvider(new CompositeColumnProvider(DefaultColumnProviders.Instance))
                .AddLogger(new ConsoleLogger())
                .AddExporter(new HtmlExporter());

            pressSwitcher.RunAllJoined(config);

        }
    }
}
