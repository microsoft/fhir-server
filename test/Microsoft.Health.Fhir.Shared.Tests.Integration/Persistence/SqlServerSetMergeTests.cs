﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [FhirStorageTestsFixtureArgumentSets(DataStore.SqlServer)]
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
            var mergeResults = await _store.MergeAsync(
                new List<ResourceWrapperOperation>
                {
                    new ResourceWrapperOperation(patientWrapper, true, true, null, false, false, bundleResourceContext: null),
                    new ResourceWrapperOperation(observationWrapper, true, true, null, false, false, bundleResourceContext: null),
                },
                default);
            Assert.NotNull(mergeResults);
            Assert.Equal(2, mergeResults.Count);
            Assert.Equal(2, mergeResults.Count(r => r.Value.IsOperationSuccessful));
            var patientOutcome = mergeResults.Values.FirstOrDefault(_ => _.UpsertOutcome.Wrapper.ResourceId == patientId).UpsertOutcome;
            Assert.NotNull(patientOutcome);
            Assert.Equal(SaveOutcomeType.Created, patientOutcome.OutcomeType);
            Assert.Equal("1", patientOutcome.Wrapper.Version);
            var observationOutcome = mergeResults.Values.FirstOrDefault(_ => _.UpsertOutcome.Wrapper.ResourceId == observationId).UpsertOutcome;
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
            mergeResults = await _store.MergeAsync(
                new List<ResourceWrapperOperation>
                {
                    new ResourceWrapperOperation(patientWrapper, true, true, null, false, false, bundleResourceContext: null),
                    new ResourceWrapperOperation(observationWrapper, true, true, null, false, false, bundleResourceContext: null),
                },
                default);
            Assert.NotNull(mergeResults);
            Assert.Equal(2, mergeResults.Count);
            Assert.Equal(2, mergeResults.Count(r => r.Value.IsOperationSuccessful));
            patientOutcome = mergeResults.Values.FirstOrDefault(_ => _.UpsertOutcome.Wrapper.ResourceId == patientId).UpsertOutcome;
            Assert.NotNull(patientOutcome);
            Assert.Equal(SaveOutcomeType.Updated, patientOutcome.OutcomeType);
            Assert.Equal("2", patientOutcome.Wrapper.Version);
            observationOutcome = mergeResults.Values.FirstOrDefault(_ => _.UpsertOutcome.Wrapper.ResourceId == observationId).UpsertOutcome;
            Assert.NotNull(observationOutcome);
            Assert.Equal(SaveOutcomeType.Updated, observationOutcome.OutcomeType);
            Assert.Equal("1", observationOutcome.Wrapper.Version);

            // update observation
            _logger.LogInformation($"update observation");
            UpdateResource(observationWrapper);
            mergeResults = await _store.MergeAsync(
                new List<ResourceWrapperOperation>
                {
                    new ResourceWrapperOperation(patientWrapper, true, true, null, false, false, bundleResourceContext: null),
                    new ResourceWrapperOperation(observationWrapper, true, true, null, false, false, bundleResourceContext: null),
                },
                default);
            Assert.NotNull(mergeResults);
            Assert.Equal(2, mergeResults.Count);
            Assert.Equal(2, mergeResults.Count(r => r.Value.IsOperationSuccessful));
            patientOutcome = mergeResults.Values.FirstOrDefault(_ => _.UpsertOutcome.Wrapper.ResourceId == patientId).UpsertOutcome;
            Assert.NotNull(patientOutcome);
            Assert.Equal(SaveOutcomeType.Updated, patientOutcome.OutcomeType);
            Assert.Equal("2", patientOutcome.Wrapper.Version);
            observationOutcome = mergeResults.Values.FirstOrDefault(_ => _.UpsertOutcome.Wrapper.ResourceId == observationId).UpsertOutcome;
            Assert.NotNull(observationOutcome);
            Assert.Equal(SaveOutcomeType.Updated, observationOutcome.OutcomeType);
            Assert.Equal("2", observationOutcome.Wrapper.Version);
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
    }
}
