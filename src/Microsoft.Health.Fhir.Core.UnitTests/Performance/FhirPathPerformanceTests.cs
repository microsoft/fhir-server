// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using BenchmarkDotNet.Attributes;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Core.UnitTests.Performance
{
    [MemoryDiagnoser]
    [InProcess]
    public class FhirPathPerformanceTests
    {
        private static readonly string Path;
        private static readonly CapabilityStatement Statement;

        static FhirPathPerformanceTests()
        {
            var c = new DefaultConformanceProvider(new FhirJsonParser());

            Statement = c.GetCapabilityStatementAsync().Result;

            Path = "CapabilityStatement.rest.resource.where(type = 'Patient').where(versioning = 'versioned-update').exists() or CapabilityStatement.rest.resource.where(type = 'Patient').where(versioning = 'versioned').exists()";
        }

        [Benchmark]
        public void ParseFhirPath()
        {
            Statement.Scalar(Path);
        }
    }
}
