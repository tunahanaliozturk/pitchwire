using BenchmarkDotNet.Running;

// Run everything with `dotnet run -c Release --project bench/Pitchwire.Benchmarks`, or one class with
// `--filter *MatchBenchmarks*`, or quickly with `--job short`. The numbers quoted in the README came
// from this, and the machine they came from is quoted with them: a benchmark without its machine is a
// number without a unit.
//
// The job is left to the command line rather than pinned here, because a configured job and a `--job`
// argument both run, and the report then carries every number twice.
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>
/// Named so the switcher has an assembly to look in.
/// </summary>
public partial class Program;
