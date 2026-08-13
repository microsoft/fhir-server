// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using BenchmarkDotNet.Running;

namespace Microsoft.Health.Fhir.Benchmarks
{
    /// <summary>
    /// Entry point for the FHIR SDK provider benchmarks.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Runs the benchmarks. Pass BenchmarkDotNet switches such as <c>--filter *</c> to select a subset, or
        /// <c>--smoke</c> to execute each benchmark once with exceptions surfaced, which is far easier to
        /// diagnose than BenchmarkDotNet's isolated child process.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        public static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--smoke")
            {
                var benchmarks = new ImportPipelineBenchmarks();
                benchmarks.Setup();

                System.Console.WriteLine($"FirelyParse      : {benchmarks.FirelyParse().InstanceType}");
                System.Console.WriteLine($"IgnixaParse      : {benchmarks.IgnixaParse().InstanceType}");
                System.Console.WriteLine($"FirelySerialize  : {benchmarks.FirelySerialize().Data.Length} chars");
                System.Console.WriteLine($"IgnixaSerialize  : {benchmarks.IgnixaSerialize().Data.Length} chars");
                System.Console.WriteLine($"FirelyEvaluate   : {benchmarks.FirelyEvaluate()}");
                System.Console.WriteLine($"IgnixaEvaluate   : {benchmarks.IgnixaEvaluate()}");
                return;
            }

            BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
