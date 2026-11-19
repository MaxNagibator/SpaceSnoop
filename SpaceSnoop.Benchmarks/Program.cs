using BenchmarkDotNet.Running;
using SpaceSnoop.Benchmarks;
using System.Reflection;

if (args is ["--progress-channels"])
{
    ProgressChannelMeasurement.Run();
    return;
}

BenchmarkSwitcher.FromAssembly(Assembly.GetExecutingAssembly()).Run(args);
