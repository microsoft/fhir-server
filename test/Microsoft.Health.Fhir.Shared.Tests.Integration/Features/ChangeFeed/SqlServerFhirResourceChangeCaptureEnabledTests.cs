// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
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
            Assert.Equal(ResourceChangeTypeCreated, resourceChanges.First().ResourceChangeTypeId);
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

            var resourceChangeData = resourceChanges.Where(x => x.ResourceVersion.ToString() == deserialized.VersionId && x.ResourceId == deserialized.Id).FirstOrDefault();

            Assert.NotNull(resourceChangeData);
            Assert.Equal(ResourceChangeTypeUpdated, resourceChangeData.ResourceChangeTypeId);
        }

        [Fact]
        public async Task GivenChangeCaptureEnabledAndNoVersionPolicy_AfterUpdating_InvisibleHistoryIsRemovedByWatchdog()
        {
            ExecuteSql("TRUNCATE TABLE dbo.Resource");

            var store = (SqlServerFhirDataStore)_fixture.DataStore;

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            var wd = new InvisibleHistoryCleanupWatchdog(store, () => _fixture.SqlConnectionWrapperFactory.CreateMockScope(), XUnitLogger<InvisibleHistoryCleanupWatchdog>.Create(_testOutputHelper));
            await wd.StartAsync(cts.Token, 1, 2, 2.0 / 24 / 3600); // retention 2 seconds
            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            // create 2 records (1 invisible)
            var create = await _fixture.Mediator.CreateResourceAsync(Samples.GetDefaultOrganization());
            Assert.Equal("1", create.VersionId);
            var newValue = Samples.GetDefaultOrganization().UpdateId(create.Id);
            newValue.ToPoco<Hl7.Fhir.Model.Organization>().Text = new Hl7.Fhir.Model.Narrative { Status = Hl7.Fhir.Model.Narrative.NarrativeStatus.Generated, Div = $"<div>Whatever</div>" };
            var update = await _fixture.Mediator.UpsertResourceAsync(newValue);
            Assert.Equal("2", update.RawResourceElement.VersionId);

            // check 2 records exist
            Assert.Equal(2, await GetCount());

            await store.MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken.None); // this logic is invoked by WD normally
            await Task.Delay(5000);

            // check only 1 record remains
            Assert.Equal(1, await GetCount());
        }

        [Fact]
        public async Task GivenChangeCaptureEnabledAndNoVersionPolicy_AfterHardDeleting_InvisibleHistoryIsRetainedAndIsRemovedByWatchdog()
        {
            ExecuteSql("TRUNCATE TABLE dbo.Resource");

            var store = (SqlServerFhirDataStore)_fixture.DataStore;

            using var cts = new CancellationTokenSource();
            cts.CancelAfter(TimeSpan.FromSeconds(60));

            var wd = new InvisibleHistoryCleanupWatchdog(store, () => _fixture.SqlConnectionWrapperFactory.CreateMockScope(), XUnitLogger<InvisibleHistoryCleanupWatchdog>.Create(_testOutputHelper));
            await wd.StartAsync(cts.Token, 1, 2, 2.0 / 24 / 3600); // retention 2 seconds
            var startTime = DateTime.UtcNow;
            while (!wd.IsLeaseHolder && (DateTime.UtcNow - startTime).TotalSeconds < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(0.2));
            }

            Assert.True(wd.IsLeaseHolder, "Is lease holder");
            _testOutputHelper.WriteLine($"Acquired lease in {(DateTime.UtcNow - startTime).TotalSeconds} seconds.");

            // create 1 resource and hard delete it
            var create = await _fixture.Mediator.CreateResourceAsync(Samples.GetDefaultOrganization());
            Assert.Equal("1", create.VersionId);
            await store.HardDeleteAsync(new ResourceKey("Organization", create.Id), false, cts.Token);

            // check 1 record exist
            Assert.Equal(1, await GetCount());

            await store.MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken.None); // this logic is invoked by WD normally
            await Task.Delay(5000);

            // check no records
            Assert.Equal(0, await GetCount());
        }

        [Fact]
        public async Task GivenChangeCaptureEnabledAndNoVersionPolicy_AfterUpdating_HistoryIsNotReturnedAndChangesAreReturned()
        {
            EnableDatabaseLogging();
            var store = (SqlServerFhirDataStore)_fixture.DataStore;

            await store.MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken.None); // this logic is invoked by WD normally
            var startTranId = await store.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None);

            var create = await _fixture.Mediator.CreateResourceAsync(Samples.GetDefaultOrganization());
            Assert.Equal("1", create.VersionId);

            var newValue = Samples.GetDefaultOrganization().UpdateId(create.Id);
            newValue.ToPoco<Hl7.Fhir.Model.Organization>().Text = new Hl7.Fhir.Model.Narrative { Status = Hl7.Fhir.Model.Narrative.NarrativeStatus.Generated, Div = $"<div>Whatever</div>" };
            var update = await _fixture.Mediator.UpsertResourceAsync(newValue);
            Assert.Equal("2", update.RawResourceElement.VersionId);

            var history = await _fixture.Mediator.SearchResourceHistoryAsync(Core.Models.KnownResourceTypes.Organization, create.Id);
            var bundle = history.ToPoco<Hl7.Fhir.Model.Bundle>();
            Assert.Single(bundle.Entry);

            await store.MergeResourcesAdvanceTransactionVisibilityAsync(CancellationToken.None); // this logic is invoked by WD normally
            var endTranId = await store.MergeResourcesGetTransactionVisibilityAsync(CancellationToken.None);

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
            var trans = await store.GetTransactionsAsync(startTranId, endTranId, CancellationToken.None);
            Assert.Equal(2, trans.Count);
            var resourceKeys = await store.GetResourceDateKeysByTransactionIdAsync(trans[0].TransactionId, CancellationToken.None);
            Assert.Equal(1, resourceKeys.Count);
            Assert.Equal(create.Id, resourceKeys[0].Id);
            Assert.Equal("1", resourceKeys[0].VersionId);
            Assert.False(resourceKeys[0].IsDeleted);
            resourceKeys = await store.GetResourceDateKeysByTransactionIdAsync(trans[1].TransactionId, CancellationToken.None);
            Assert.Equal(1, resourceKeys.Count);
            Assert.Equal("2", resourceKeys[0].VersionId);
            Assert.False(resourceKeys[0].IsDeleted);
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

            var resourceChangeData = resourceChanges.Where(x => x.ResourceVersion.ToString() == deletedResourceKey.ResourceKey.VersionId && x.ResourceId == deletedResourceKey.ResourceKey.Id).FirstOrDefault();

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
    }
}
