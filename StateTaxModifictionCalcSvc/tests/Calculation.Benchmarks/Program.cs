using BenchmarkDotNet.Running;
using Calculation.Tests.Performance;

// Run with:
//   dotnet run -c Release --project tests/Calculation.Benchmarks -- --filter "*Benchmarks*"
BenchmarkRunner.Run<CalculationEngineBenchmarks>();
