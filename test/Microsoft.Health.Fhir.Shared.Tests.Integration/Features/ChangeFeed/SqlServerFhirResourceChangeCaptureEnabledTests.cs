﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.ChangeFeed
{
    /// <summary>
    /// Integration tests for a resource change capture feature.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DomainLogicValidation)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerFhirResourceChangeCaptureEnabledTests : IClassFixture<SqlServerFhirResourceChangeCaptureFixture>
    {
        private readonly SqlServerFhirResourceChangeCaptureFixture _fixture;
        private readonly ITestOutputHelper _testOutputHelper;
        private const byte ResourceChangeTypeCreated = 0; // 0 is for the resource creating.
        private const byte ResourceChangeTypeUpdated = 1; // 1 is for resource update.
        private const byte ResourceChangeTypeDeleted = 2; // 2 is for resource deletion.

        public SqlServerFhirResourceChangeCaptureEnabledTests(SqlServerFhirResourceChangeCaptureFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
        }

        /// <summary>
        /// A basic smoke test verifying that the code can
        /// insert and read resource changes after inserting a resource.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseSupportsResourceChangeCapture_WhenInsertingAResource_ThenResourceChangesShouldBeReturned()
        {
            // add a new resource
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

            // get resource changes
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, _fixture.SchemaInformation);
            var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            Assert.NotNull(resourceChanges);
            Assert.Single(resourceChanges.Where(x => x.ResourceId == deserialized.Id));
            Assert.Equal(ResourceChangeTypeCreated, resourceChanges.First(x => x.ResourceId == deserialized.Id).ResourceChangeTypeId);
        }

        /// <summary>
        /// A basic smoke test verifying that the code can
        /// insert and read resource changes after updating a resource.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseSupportsResourceChangeCapture_WhenUpdatingAResource_ThenResourceChangesShouldBeReturned()
        {
            // add a new resource
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

            // update the resource
            var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
            newResourceValues.Id = saveResult.RawResourceElement.Id;

            // save updated resource
            var updateResult = await _fixture.Mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));
            var deserialized = updateResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

            // get resource changes
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, _fixture.SchemaInformation);
            var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            Assert.NotNull(resourceChanges);
            Assert.Single(resourceChanges.Where(x => x.ResourceVersion.ToString() == deserialized.VersionId && x.ResourceId == deserialized.Id));

            var resourceChangeData = resourceChanges.FirstOrDefault(x => x.ResourceVersion.ToString() == deserialized.VersionId && x.ResourceId == deserialized.Id);

            Assert.NotNull(resourceChangeData);
            Assert.Equal(ResourceChangeTypeUpdated, resourceChangeData.ResourceChangeTypeId);
        }

        [Fact]
        public async Task GivenADatabaseSupportsResourceChangeCapture_WhenImportingNegativeVersions_ThenResourceChangesShouldBeReturned()
        {
            ExecuteSql("TRUNCATE TABLE dbo.Resource");

            var store = (SqlServerFhirDataStore)_fixture.DataStore;

            var id = Guid.NewGuid().ToString("N");
            var date = DateTimeOffset.UtcNow;
            var wrapper = CreateTestPatient(id, date.AddHours(-1)); // version 1
            await store.ImportResourcesAsync([new ImportResource(0, 0, 0, true, false, false, wrapper)], ImportMode.IncrementalLoad, true, CancellationToken.None);
            wrapper = CreateTestPatient(id, date); // version 2
            await store.ImportResourcesAsync([new ImportResource(0, 0, 0, true, false, false, wrapper)], ImportMode.IncrementalLoad, true, CancellationToken.None);
            wrapper = CreateTestPatient(id, date.AddHours(-2)); // version -1
            await store.ImportResourcesAsync([new ImportResource(0, 0, 0, true, false, false, wrapper)], ImportMode.IncrementalLoad, true, CancellationToken.None);

            Assert.Equal(3, await GetCount());

            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, _fixture.SchemaInformation);
            var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            Assert.NotNull(resourceChanges);
            Assert.Single(resourceChanges.Where(x => x.ResourceVersion.ToString() == "1" && x.ResourceId == id));
            Assert.Single(resourceChanges.Where(x => x.ResourceVersion.ToString() == "2" && x.ResourceId == id));

            // negative versions are filtered out according to the existing logic because they are historical
            Assert.Empty(resourceChanges.Where(x => x.ResourceVersion.ToString() == "-1" && x.ResourceId == id));
        }

        [Fact]
        public async Task GivenChangeCaptureEnabledAndNoVersionPolicy_AfterUpdating_InvisibleHistoryIsRemovedByWatchdog()
        {
            EnableInvisibleHistory();
            ExecuteSql("TRUNCATE TABLE dbo.Transactions");
            ExecuteSql("TRUNCATE TABLE dbo.Resource");

            var store = (SqlServerFhirDataStore)_fixture.DataStore;

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            var storeClient = new SqlStoreClient(_fixture.SqlRetryService, NullLogger<SqlStoreClient>.Instance);
            var wd = new InvisibleHistoryCleanupWatchdog(storeClient, _fixture.SqlRetryService, XUnitLogger<InvisibleHistoryCleanupWatchdog>.Create(_testOutputHelper))
            {
                PeriodSec = 1,
                LeasePeriodSec = 2,
                RetentionPeriodDays = 2.0 / 24 / 3600,
            };

            var wdTask = wd.ExecuteAsync(cts.Token); // retention 2 seconds
            var startTime = DateTime.UtcNow;

            while ((!wd.IsLeaseHolder || !wd.IsInitialized) && !cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2), cts.Token);
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            // create 2 records (1 invisible)
            var create = await _fixture.Mediator.CreateResourceAsync(Samples.GetDefaultOrganization(), cancellationToken: cts.Token);
            Assert.Equal("1", create.VersionId);
            var newValue = Samples.GetDefaultOrganization().UpdateId(create.Id);
            newValue.ToPoco<Hl7.Fhir.Model.Organization>().Text = new Hl7.Fhir.Model.Narrative { Status = Hl7.Fhir.Model.Narrative.NarrativeStatus.Generated, Div = "<div>Whatever</div>" };
            var update = await _fixture.Mediator.UpsertResourceAsync(newValue, cancellationToken: cts.Token);
            Assert.Equal("2", update.RawResourceElement.VersionId);

            // check 2 records exist
            Assert.Equal(2, await GetCount());

            await store.StoreClient.MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken.None); // this logic is invoked by WD normally

            // check only 1 record remains
            while (await GetCount() != 1 && !cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            Assert.Equal(1, await GetCount());
            DisableInvisibleHistory();
            await cts.CancelAsync();
            await wdTask;
        }

        [Fact]
        public async Task GivenChangeCaptureEnabledAndNoVersionPolicy_AfterHardDeleting_InvisibleHistoryIsRetainedAndIsRemovedByWatchdog()
        {
            EnableInvisibleHistory();
            ExecuteSql("TRUNCATE TABLE dbo.Transactions");
            ExecuteSql("TRUNCATE TABLE dbo.Resource");

            var store = (SqlServerFhirDataStore)_fixture.DataStore;

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            var storeClient = new SqlStoreClient(_fixture.SqlRetryService, NullLogger<SqlStoreClient>.Instance);
            var wd = new InvisibleHistoryCleanupWatchdog(storeClient, _fixture.SqlRetryService, XUnitLogger<InvisibleHistoryCleanupWatchdog>.Create(_testOutputHelper))
            {
                PeriodSec = 1,
                LeasePeriodSec = 2,
                RetentionPeriodDays = 2.0 / 24 / 3600,
            };

            var wdTask = wd.ExecuteAsync(cts.Token); // retention 2 seconds
            var startTime = DateTime.UtcNow;
            while ((!wd.IsLeaseHolder || !wd.IsInitialized) && !cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2), cts.Token);
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            // create 1 resource and hard delete it
            var create = await _fixture.Mediator.CreateResourceAsync(Samples.GetDefaultOrganization(), CancellationToken.None);
            Assert.Equal("1", create.VersionId);

            var resource = await store.GetAsync(new ResourceKey("Organization", create.Id, create.VersionId), CancellationToken.None);
            Assert.NotNull(resource);

            await store.HardDeleteAsync(new ResourceKey("Organization", create.Id), false, false, cts.Token);

            resource = await store.GetAsync(new ResourceKey("Organization", create.Id, create.VersionId), CancellationToken.None);
            Assert.Null(resource);

            // check 1 record exist
            Assert.Equal(1, await GetCount());

            await store.StoreClient.MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken.None); // this logic is invoked by WD normally

            // check no records
            while (await GetCount() > 0 && !cts.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cts.Token);
            }

            Assert.Equal(0, await GetCount());
            DisableInvisibleHistory();

            await cts.CancelAsync();
            await wdTask;
        }

        [Fact]
        public async Task GivenChangeCaptureEnabledAndNoVersionPolicy_AfterHardDeleting_CanRecreateResource()
        {
            EnableDatabaseLogging();
            EnableInvisibleHistory();

            var create = await _fixture.Mediator.CreateResourceAsync(Samples.GetDefaultOrganization());
            Assert.Equal("1", create.VersionId);
            var id = create.Id;

            var store = (SqlServerFhirDataStore)_fixture.DataStore;
            await store.HardDeleteAsync(new ResourceKey("Organization", id), false, false, CancellationToken.None);

            var reCreate = await _fixture.Mediator.UpsertResourceAsync(Samples.GetDefaultOrganization().UpdateId(id));
            Assert.Equal(id, reCreate.RawResourceElement.Id);
            Assert.Equal("2", reCreate.RawResourceElement.VersionId);

            DisableInvisibleHistory();
        }

        [Fact]
        public async Task GivenChangeCaptureEnabledAndNoVersionPolicy_AfterSoftDeleting_CanRecreateResource()
        {
            EnableDatabaseLogging();
            EnableInvisibleHistory();

            var create = await _fixture.Mediator.CreateResourceAsync(Samples.GetDefaultOrganization());
            Assert.Equal("1", create.VersionId);
            var id = create.Id;

            await _fixture.Mediator.DeleteResourceAsync(new ResourceKey("Organization", id), DeleteOperation.SoftDelete);

            var reCreate = await _fixture.Mediator.UpsertResourceAsync(Samples.GetDefaultOrganization().UpdateId(id));
            Assert.Equal(id, reCreate.RawResourceElement.Id);
            Assert.Equal("3", reCreate.RawResourceElement.VersionId);

            DisableInvisibleHistory();
        }

        [Fact]
        public async Task GivenChangeCaptureEnabledAndNoVersionPolicy_AfterUpdating_HistoryIsNotReturnedAndChangesAreReturned()
        {
            EnableDatabaseLogging();
            EnableInvisibleHistory();
            var store = (SqlServerFhirDataStore)_fixture.DataStore;

            await store.StoreClient.MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken.None); // this logic is invoked by WD normally
            var startTranId = await store.StoreClient.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None);

            var create = await _fixture.Mediator.CreateResourceAsync(Samples.GetDefaultOrganization());
            Assert.Equal("1", create.VersionId);

            var newValue = Samples.GetDefaultOrganization().UpdateId(create.Id);
            newValue.ToPoco<Hl7.Fhir.Model.Organization>().Text = new Hl7.Fhir.Model.Narrative { Status = Hl7.Fhir.Model.Narrative.NarrativeStatus.Generated, Div = $"<div>Whatever</div>" };
            var update = await _fixture.Mediator.UpsertResourceAsync(newValue);
            Assert.Equal("2", update.RawResourceElement.VersionId);

            var history = await _fixture.Mediator.SearchResourceHistoryAsync(Core.Models.KnownResourceTypes.Organization, create.Id);
            var bundle = history.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Single(bundle.Entry);

            await store.StoreClient.MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken.None); // this logic is invoked by WD normally
            var endTranId = await store.StoreClient.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None);

            // old style TODO: Remove once events switch to new style
            var changeStore = new SqlServerFhirResourceChangeDataStore(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, _fixture.SchemaInformation);
            var changes = await changeStore.GetRecordsAsync(1, 200, CancellationToken.None);
            Assert.NotNull(changes);
            var change = changes.Where(x => x.ResourceVersion.ToString() == create.VersionId && x.ResourceId == create.Id).FirstOrDefault();
            Assert.NotNull(change);
            Assert.Equal(ResourceChangeTypeCreated, change.ResourceChangeTypeId);
            change = changes.Where(x => x.ResourceVersion.ToString() == update.RawResourceElement.VersionId && x.ResourceId == create.Id).FirstOrDefault();
            Assert.NotNull(change);
            Assert.Equal(ResourceChangeTypeUpdated, change.ResourceChangeTypeId);

            // new style
            var trans = await store.StoreClient.GetTransactionsAsync(startTranId, endTranId, CancellationToken.None);
            Assert.Equal(2, trans.Count);
            var resourceKeys = await store.StoreClient.GetResourceDateKeysByTransactionIdAsync(trans[0].TransactionId, CancellationToken.None);
            Assert.Single(resourceKeys);
            Assert.Equal(create.Id, resourceKeys[0].Id);
            Assert.Equal("1", resourceKeys[0].VersionId);
            Assert.False(resourceKeys[0].IsDeleted);
            resourceKeys = await store.StoreClient.GetResourceDateKeysByTransactionIdAsync(trans[1].TransactionId, CancellationToken.None);
            Assert.Single(resourceKeys);
            Assert.Equal("2", resourceKeys[0].VersionId);
            Assert.False(resourceKeys[0].IsDeleted);

            DisableInvisibleHistory();
        }

        private void EnableInvisibleHistory()
        {
            using var conn = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "INSERT INTO dbo.Parameters (Id, Number) SELECT 'InvisibleHistory.IsEnabled', 1";
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        private void DisableInvisibleHistory()
        {
            using var conn = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "DELETE FROM dbo.Parameters WHERE Id = 'InvisibleHistory.IsEnabled'";
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        private void EnableDatabaseLogging()
        {
            using var conn = _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false).Result;
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandText = "INSERT INTO dbo.Parameters (Id, Char) SELECT name, 'LogEvent' FROM sys.objects WHERE type = 'p'";
            cmd.ExecuteNonQueryAsync(CancellationToken.None).Wait();
        }

        /// <summary>
        /// A basic smoke test verifying that the code can
        /// insert and read resource changes after deleting a resource.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseSupportsResourceChangeCapture_WhenDeletingAResource_ThenResourceChangesShouldBeReturned()
        {
            // add a new resource
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

            // delete the resource
            var deletedResourceKey = await _fixture.Mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), DeleteOperation.SoftDelete);

            // get resource changes
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, _fixture.SchemaInformation);
            var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            Assert.NotNull(resourceChanges);
            Assert.Single(resourceChanges.Where(x => x.ResourceVersion.ToString() == deletedResourceKey.ResourceKey.VersionId && x.ResourceId == deletedResourceKey.ResourceKey.Id));

            var resourceChangeData = resourceChanges.FirstOrDefault(x => x.ResourceVersion.ToString() == deletedResourceKey.ResourceKey.VersionId && x.ResourceId == deletedResourceKey.ResourceKey.Id);

            Assert.NotNull(resourceChangeData);
            Assert.Equal(ResourceChangeTypeDeleted, resourceChangeData.ResourceChangeTypeId);
        }

        /// <summary>
        /// A basic smoke test verifying that the resource type name
        /// should be equal to a resource type.
        /// </summary>
        [Fact]
        public async Task GivenADatabase_WhenGettingResourceTypes_WhenInsertingAResource_ThenResourceTypeNameShouldBeEqual()
        {
            // add a new resource
            var saveResult = await _fixture.Mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
            var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

            // get resource types
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(_fixture.SqlConnectionWrapperFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, _fixture.SchemaInformation);
            var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            Assert.NotNull(resourceChanges);
            Assert.Single(resourceChanges.Where(x => x.ResourceId == deserialized.Id));

            var resourceChangeData = resourceChanges.Where(x => x.ResourceId == deserialized.Id).FirstOrDefault();

            Assert.NotNull(resourceChangeData);
            Assert.Equal(saveResult.RawResourceElement.InstanceType, resourceChangeData.ResourceTypeName);
        }

        private async void ExecuteSql(string sql)
        {
            using var conn = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = sql;
            await cmd.ExecuteNonQueryAsync(CancellationToken.None);
        }

        private async Task<int> GetCount()
        {
            using var conn = await _fixture.SqlConnectionWrapperFactory.ObtainSqlConnectionWrapperAsync(CancellationToken.None, false);
            using var cmd = conn.CreateRetrySqlCommand();
            cmd.CommandTimeout = 120;
            cmd.CommandText = "SELECT count(*) FROM dbo.Resource";
            return (int)await cmd.ExecuteScalarAsync(CancellationToken.None);
        }

        private ResourceWrapper CreateTestPatient(string id = null, DateTimeOffset? lastUpdated = null)
        {
            var patient = new Hl7.Fhir.Model.Patient()
            {
                Id = id ?? Guid.NewGuid().ToString("N"),
                Meta = new(),
            };

            if (lastUpdated is not null)
            {
                patient.Meta = new Hl7.Fhir.Model.Meta { LastUpdated = lastUpdated };
            }

            var resource = patient.ToTypedElement().ToResourceElement();
            var rawResourceFactory = Substitute.For<RawResourceFactory>(new FhirJsonSerializer());
            var wrapper = new ResourceWrapper(resource, rawResourceFactory.Create(resource, keepMeta: true), new ResourceRequest("Import"), true, new List<SearchIndexEntry>(), null, null, "ABC");

            return wrapper;
        }
    }
}
