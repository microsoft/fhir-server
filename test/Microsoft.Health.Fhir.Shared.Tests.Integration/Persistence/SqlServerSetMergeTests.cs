// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Client;
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
        private readonly XUnitLogger<SqlServerSetMergeTests> _logger;
        private readonly FhirJsonSerializer _jsonSerializer;
        private readonly SqlConnectionWrapperFactory _sqlConnectionWrapperFactory;

        public SqlServerSetMergeTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _store = (SqlServerFhirDataStore)fixture.GetService<IFhirDataStore>();
            _jsonSerializer = new FhirJsonSerializer(null);
            _logger = XUnitLogger<SqlServerSetMergeTests>.Create(testOutputHelper);
            _sqlConnectionWrapperFactory = fixture.SqlConnectionWrapperFactory;
        }

        [Fact]
        public async Task GivenSetOfResources_MergeAndGet()
        {
            var patientId = Guid.NewGuid().ToString();
            var patientWrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));
            var observationId = Guid.NewGuid().ToString();
            var observationWrapper = GetResourceWrapper(Samples.GetDefaultObservation().UpdateId(observationId));

            // create both
            var mergeResults = await _store.MergeAsync(new List<ResourceWrapperOperation> { new ResourceWrapperOperation(patientWrapper, true, true, null, false), new ResourceWrapperOperation(observationWrapper, true, true, null, false) }, default);
            Assert.NotNull(mergeResults);
            Assert.Equal(2, mergeResults.Count);
            var patientOutcome = mergeResults.Values.FirstOrDefault(_ => _.Wrapper.ResourceId == patientId);
            Assert.NotNull(patientOutcome);
            Assert.Equal(SaveOutcomeType.Created, patientOutcome.OutcomeType);
            Assert.Equal("1", patientOutcome.Wrapper.Version);
            var observationOutcome = mergeResults.Values.FirstOrDefault(_ => _.Wrapper.ResourceId == observationId);
            Assert.NotNull(observationOutcome);
            Assert.Equal(SaveOutcomeType.Created, observationOutcome.OutcomeType);
            Assert.Equal("1", observationOutcome.Wrapper.Version);

            var wrappers = await _store.GetAsync(new List<ResourceKey> { new ResourceKey("Patient", patientId), new ResourceKey("Observation", observationId) }, default);
            _logger.LogInformation($"wrappers.Count={wrappers.Count}");
            Assert.Equal(2, wrappers.Count);
            Assert.NotNull(wrappers.FirstOrDefault(_ => _.ResourceId == patientId));
            Assert.NotNull(wrappers.FirstOrDefault(_ => _.ResourceId == observationId));

            // update patient
            _logger.LogInformation($"update patient");
            UpdateResource(patientWrapper);
            mergeResults = await _store.MergeAsync(new List<ResourceWrapperOperation> { new ResourceWrapperOperation(patientWrapper, true, true, null, false), new ResourceWrapperOperation(observationWrapper, true, true, null, false) }, default);
            Assert.NotNull(mergeResults);
            Assert.Equal(2, mergeResults.Count);
            patientOutcome = mergeResults.Values.FirstOrDefault(_ => _.Wrapper.ResourceId == patientId);
            Assert.NotNull(patientOutcome);
            Assert.Equal(SaveOutcomeType.Updated, patientOutcome.OutcomeType);
            Assert.Equal("2", patientOutcome.Wrapper.Version);
            observationOutcome = mergeResults.Values.FirstOrDefault(_ => _.Wrapper.ResourceId == observationId);
            Assert.NotNull(observationOutcome);
            Assert.Equal(SaveOutcomeType.Updated, observationOutcome.OutcomeType);
            Assert.Equal("1", observationOutcome.Wrapper.Version);

            // update observation
            _logger.LogInformation($"update observation");
            UpdateResource(observationWrapper);
            mergeResults = await _store.MergeAsync(new List<ResourceWrapperOperation> { new ResourceWrapperOperation(patientWrapper, true, true, null, false), new ResourceWrapperOperation(observationWrapper, true, true, null, false) }, default);
            Assert.NotNull(mergeResults);
            Assert.Equal(2, mergeResults.Count);
            patientOutcome = mergeResults.Values.FirstOrDefault(_ => _.Wrapper.ResourceId == patientId);
            Assert.NotNull(patientOutcome);
            Assert.Equal(SaveOutcomeType.Updated, patientOutcome.OutcomeType);
            Assert.Equal("2", patientOutcome.Wrapper.Version);
            observationOutcome = mergeResults.Values.FirstOrDefault(_ => _.Wrapper.ResourceId == observationId);
            Assert.NotNull(observationOutcome);
            Assert.Equal(SaveOutcomeType.Updated, observationOutcome.OutcomeType);
            Assert.Equal("2", observationOutcome.Wrapper.Version);
        }

        [Fact]
        public async Task GivenResourceAndMergeDisabled_UpsertAndGet()
        {
            DisableMergeResources();

            try
            {
                var patientId = Guid.NewGuid().ToString();
                var patientWrapper = GetResourceWrapper(Samples.GetDefaultPatient().UpdateId(patientId));

                // create
                var upsertResult = await _store.UpsertAsync(new ResourceWrapperOperation(patientWrapper, true, true, null, false), default);
                Assert.NotNull(upsertResult);
                Assert.Equal(SaveOutcomeType.Created, upsertResult.OutcomeType);
                Assert.Equal("1", upsertResult.Wrapper.Version);

                var wrapper = await _store.GetAsync(new ResourceKey("Patient", patientId), default);
                Assert.NotNull(wrapper);

                // update
                UpdateResource(patientWrapper);
                upsertResult = await _store.UpsertAsync(new ResourceWrapperOperation(patientWrapper, true, true, null, false), default);
                Assert.NotNull(upsertResult);
                Assert.Equal(SaveOutcomeType.Updated, upsertResult.OutcomeType);
                Assert.Equal("2", upsertResult.Wrapper.Version);
            }
            finally
            {
                EnableMergeResources();
            }
        }

        private ResourceWrapper GetResourceWrapper(ResourceElement resource)
        {
            var poco = resource.ToPoco();
            poco.VersionId = "1";
            var str = _jsonSerializer.SerializeToString(poco);
            var raw = new RawResource(str, FhirResourceFormat.Json, true);
            var wrapper = new ResourceWrapper(resource, raw, new ResourceRequest("Merge"), false, null, null, null, "hash");
            wrapper.LastModified = DateTime.UtcNow;
            return wrapper;
        }

        private static void UpdateResource(ResourceWrapper resource)
        {
            var rawResourceData = resource.RawResource.Data;
            if (resource.ResourceTypeName == "Observation")
            {
                rawResourceData = rawResourceData.Replace("\"value\":67,", "\"value\":167,");
            }
            else if (resource.ResourceTypeName == "Patient")
            {
                rawResourceData = rawResourceData.Replace("\"birthDate\":\"1974-12-25\"", "\"birthDate\":\"2000-01-01\"");
            }

            resource.RawResource = new RawResource(rawResourceData, FhirResourceFormat.Json, true);
        }

        private void DisableMergeResources()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "UPDATE dbo.Parameters SET Number = 1 WHERE Id = @Id IF @@rowcount = 0 INSERT INTO dbo.Parameters (Id, Number) SELECT @Id, 1";
            cmd.Parameters.AddWithValue("@Id", SqlServerFhirDataStore.MergeResourcesDisabledFlagId);
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        private void EnableMergeResources()
        {
            using var conn = _sqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "DELETE FROM dbo.Parameters WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", SqlServerFhirDataStore.MergeResourcesDisabledFlagId);
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }
    }
}
