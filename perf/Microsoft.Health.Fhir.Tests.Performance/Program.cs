// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using BenchmarkDotNet.Running;
using Microsoft.Health.Fhir.Core.UnitTests.Persistence;

namespace Microsoft.Health.Fhir.Tests.Performance
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var assemblies = new[]
            {
                typeof(Program).Assembly,
                typeof(RawResourceTests).Assembly,
            };

            foreach (var assembly in assemblies)
            {
                BenchmarkRunner.Run(assembly);
            }
        }
    }
}
