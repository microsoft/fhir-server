// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.Shared.Tests.Integration.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.ChangeFeed
{
    /// <summary>
    /// Integration tests for a resource change capture feature.
    /// </summary>
    public class SqlServerFhirResourceChangeCaptureTests : IClassFixture<SqlServerFhirResourceChangeCaptureFixture>
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true";
        private readonly SqlServerFhirResourceChangeCaptureFixture _fixture;
        private const byte ResourceChangeTypeCreated = 0; // 0 is for the resource creating.
        private const byte ResourceChangeTypeUpdated = 1; // 1 is for resource update.
        private const byte ResourceChangeTypeDeleted = 2; // 2 is for resource deletion.

        public SqlServerFhirResourceChangeCaptureTests(SqlServerFhirResourceChangeCaptureFixture fixture)
        {
            _fixture = fixture;
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
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(_fixture.DatabaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
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
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(_fixture.DatabaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
            var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            Assert.NotNull(resourceChanges);
            Assert.Single(resourceChanges.Where(x => x.ResourceVersion.ToString() == deserialized.VersionId && x.ResourceId == deserialized.Id));

            var resourceChangeData = resourceChanges.Where(x => x.ResourceVersion.ToString() == deserialized.VersionId && x.ResourceId == deserialized.Id).FirstOrDefault();

            Assert.NotNull(resourceChangeData);
            Assert.Equal(ResourceChangeTypeUpdated, resourceChangeData.ResourceChangeTypeId);
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
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(_fixture.DatabaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
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
            var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(_fixture.DatabaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
            var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

            Assert.NotNull(resourceChanges);
            Assert.Single(resourceChanges.Where(x => x.ResourceId == deserialized.Id));

            var resourceChangeData = resourceChanges.Where(x => x.ResourceId == deserialized.Id).FirstOrDefault();

            Assert.NotNull(resourceChangeData);
            Assert.Equal(saveResult.RawResourceElement.InstanceType, resourceChangeData.ResourceTypeName);
        }

        /// <summary>
        /// A basic smoke test verifying that resource changes should not be created
        ///  when the resource change capture config is disabled.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseSupportsResourceChangeCapture_WhenResourceChangeCaptureIsDisabled_ThenResourceChangesShouldNotBeCreated()
        {
            FhirStorageTestsFixture fhirStorageTestsFixture = null;
            try
            {
                string databaseName = $"FHIRRESOURCECHANGEDISABLEDTEST_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}_{BigInteger.Abs(new BigInteger(Guid.NewGuid().ToByteArray()))}";

                // this will either create the database or upgrade the schema.
                var coreFeatureConfigOptions = Options.Create(new CoreFeatureConfiguration() { SupportsResourceChangeCapture = false });
                fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, databaseName, coreFeatureConfigOptions));
                await fhirStorageTestsFixture.InitializeAsync();

                Mediator mediator = fhirStorageTestsFixture.Mediator;

                // add a new resource
                var saveResult = await mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

                // update the resource
                var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
                newResourceValues.Id = saveResult.RawResourceElement.Id;

                // save updated resource
                var updateResult = await mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));

                // delete the resource
                var deletedResourceKey = await mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), DeleteOperation.SoftDelete);

                // get resource changes
                var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(databaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
                var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

                Assert.NotNull(resourceChanges);
                Assert.Empty(resourceChanges);
            }
            finally
            {
                await fhirStorageTestsFixture?.DisposeAsync();
            }
        }

        private static DefaultSqlConnectionFactory GetSqlConnectionFactory(string databaseName)
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;
            var testConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName }.ToString();
            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = false };
            var config = Options.Create(new SqlServerDataStoreConfiguration { ConnectionString = testConnectionString, Initialize = false, SchemaOptions = schemaOptions });
            var connectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            var connectionFactory = new DefaultSqlConnectionFactory(connectionStringProvider);
            return connectionFactory;
        }
    }
}
