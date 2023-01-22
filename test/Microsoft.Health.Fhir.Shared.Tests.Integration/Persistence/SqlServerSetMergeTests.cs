// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerSetMergeTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirDataStore _store;
        private readonly ITestOutputHelper _testOutputHelper;
        private readonly FhirJsonSerializer _jsonSerializer;

        public SqlServerSetMergeTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _store = (SqlServerFhirDataStore)fixture.GetService<IFhirDataStore>();
            _testOutputHelper = testOutputHelper;
            _jsonSerializer = new FhirJsonSerializer(null);
        }

        [Fact]
        public async Task SaveAndGetSetOfResources()
        {
            var patientId = Guid.NewGuid().ToString();
            var patientWrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));
            var observationId = Guid.NewGuid().ToString();
            var observationWrapper = GetResourceWrapper(Samples.GetDefaultObservation().UpdateId(observationId));

            await _store.MergeAsync(new List<ResourceWrapper> { patientWrapper, observationWrapper }, default);
            var wr = await _store.GetAsync(new ResourceKey("Patient", patientId), default);
            Assert.NotNull(wr);
            wr = await _store.GetAsync(new ResourceKey("Observation", observationId), default);
            Assert.NotNull(wr);
            ////var wrappers = await _store.GetAsync(new List<ResourceKey> { new ResourceKey("Patient", patientId), new ResourceKey("Observation", observationId) }, default);
            ////Assert.Equal(2, wrappers.Count);
            ////Assert.NotNull(wrappers.FirstOrDefault(_ => _.ResourceId == patientId));
            ////Assert.NotNull(wrappers.FirstOrDefault(_ => _.ResourceId == observationId));
        }

        private ResourceWrapper GetResourceWrapper(ResourceElement resource)
        {
            var poco = resource.ToPoco();
            poco.VersionId = "1";
            var str = _jsonSerializer.SerializeToString(poco);
            var raw = new RawResource(str, FhirResourceFormat.Json, true);
            var wrapper = new ResourceWrapper(resource, raw, new ResourceRequest("Merge"), false, null, null, null);
            wrapper.LastModified = DateTime.UtcNow;
            return wrapper;
        }
    }
}
