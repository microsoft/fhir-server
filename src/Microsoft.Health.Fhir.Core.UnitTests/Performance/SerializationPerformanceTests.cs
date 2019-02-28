// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net.Http;
using BenchmarkDotNet.Attributes;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Tests.Common;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.UnitTests.Performance
{
    [MemoryDiagnoser]
    [InProcess]
    public class SerializationPerformanceTests
    {
        private static readonly ResourceWrapper Wrapper;
        private static readonly Observation Observation;
        private static readonly RawResourceFactory RawResourceFactory;
        private static readonly ResourceDeserializer ResourceDeserializer;

        static SerializationPerformanceTests()
        {
            RawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());

            Observation = Samples.GetDefaultObservation();
            Observation.Id = "id1";

            Wrapper = new ResourceWrapper(Observation, RawResourceFactory.Create(Observation), new ResourceRequest("http://fhir", HttpMethod.Post), false, null, null, null);

            ResourceDeserializer = Deserializers.ResourceDeserializer;
        }

        [Benchmark(Baseline = true)]
        public void SerializingWithNewtonSoft()
        {
            JsonConvert.SerializeObject(Observation);
        }

        [Benchmark]
        public void DeserializingWithNewtonSoft()
        {
            JsonConvert.DeserializeObject(Wrapper.RawResource.Data);
        }

        [Benchmark]
        public void SerializingWithFhirSdk()
        {
            RawResourceFactory.Create(Observation);
        }

        [Benchmark]
        public void DeserializingWithFhirSdk()
        {
            ResourceDeserializer.Deserialize(Wrapper);
        }
    }
}
