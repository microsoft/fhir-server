// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
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
using Microsoft.Health.Fhir.Core.Models;
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
    public class SqlServerFhirResourceChangeCaptureTests
    {
        private const string LocalConnectionString = "server=(local);Integrated Security=true";
        private IOptions<CoreFeatureConfiguration> _coreFeatureConfigOptions;

        public SqlServerFhirResourceChangeCaptureTests()
        {
            _coreFeatureConfigOptions = Options.Create(new CoreFeatureConfiguration() { SupportsResourceChangeCapture = true });
        }

        /// <summary>
        /// A basic smoke test verifying that the code can
        /// insert and read resource changes after inserting a resource.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseSupportsResourceChangeCapture_WhenInsertingAResource_ThenResourceChangesShouldBeReturned()
        {
            FhirStorageTestsFixture fhirStorageTestsFixture = null;

            try
            {
                for (int i = SchemaVersionConstants.SupportsResourceChangeCaptureSchemaVersion; i <= SchemaVersionConstants.Max; i++)
                {
                    string databaseName = $"FHIRRESOURCECHANGECAPTUREINSERTIONTEST_V{i}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    try
                    {
                        // this will either create the database or upgrade the schema.
                        fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(i, databaseName, _coreFeatureConfigOptions));
                        await fhirStorageTestsFixture.InitializeAsync();

                        Mediator mediator = fhirStorageTestsFixture.Mediator;

                        // add a new resource
                        var saveResult = await mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                        var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

                        // get resource changes
                        var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(databaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
                        var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(0, 200, CancellationToken.None);

                        Assert.NotNull(resourceChanges);
                        Assert.Equal(1, resourceChanges.Count);
                        Assert.Equal(deserialized.Id, resourceChanges.First().ResourceId);
                        Assert.Equal(0, resourceChanges.First().ResourceChangeTypeId); // 0 is for resource creating.
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Failure using schema version {i}", e);
                    }
                    finally
                    {
                        await fhirStorageTestsFixture?.DisposeAsync();
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"An error occurred on a resource change capture integration test", e);
            }
        }

        /// <summary>
        /// A basic smoke test verifying that the code can
        /// insert and read resource changes after updating a resource.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseSupportsResourceChangeCapture_WhenUpdatingAResource_ThenResourceChangesShouldBeReturned()
        {
            FhirStorageTestsFixture fhirStorageTestsFixture = null;

            try
            {
                for (int i = SchemaVersionConstants.SupportsResourceChangeCaptureSchemaVersion; i <= SchemaVersionConstants.Max; i++)
                {
                    string databaseName = $"FHIRRESOURCECHANGECAPTUREUPDATETEST_V{i}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    try
                    {
                        fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(i, databaseName, _coreFeatureConfigOptions));
                        await fhirStorageTestsFixture.InitializeAsync();

                        Mediator mediator = fhirStorageTestsFixture.Mediator;

                        // add a new resource
                        var saveResult = await mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));

                        // update the resource
                        var newResourceValues = Samples.GetJsonSample("WeightInGrams").ToPoco();
                        newResourceValues.Id = saveResult.RawResourceElement.Id;

                        // save updated resource
                        var updateResult = await mediator.UpsertResourceAsync(newResourceValues.ToResourceElement(), WeakETag.FromVersionId(saveResult.RawResourceElement.VersionId));
                        var deserialized = updateResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

                        // get resource changes
                        var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(databaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
                        var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(0, 200, CancellationToken.None);

                        Assert.NotNull(resourceChanges);
                        Assert.Equal(2, resourceChanges.Count);

                        var resourceChangeData = resourceChanges.Where(x => x.ResourceVersion == 2).First();

                        Assert.Equal(deserialized.Id, resourceChangeData.ResourceId);
                        Assert.Equal(1, resourceChangeData.ResourceChangeTypeId); // 1 is for resource udpate.
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Failure using schema version {i}", e);
                    }
                    finally
                    {
                        await fhirStorageTestsFixture?.DisposeAsync();
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"An error occurred on a resource change capture integration test", e);
            }
        }

        /// <summary>
        /// A basic smoke test verifying that the code can
        /// insert and read resource changes after deleting a resource.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseSupportsResourceChangeCapture_WhenDeletingAResource_ThenResourceChangesShouldBeReturned()
        {
            FhirStorageTestsFixture fhirStorageTestsFixture = null;

            try
            {
                for (int i = SchemaVersionConstants.SupportsResourceChangeCaptureSchemaVersion; i <= SchemaVersionConstants.Max; i++)
                {
                    string databaseName = $"FHIRRESOURCECHANGECAPTUREDELETIONTEST_V{i}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    try
                    {
                        // this will either create the database or upgrade the schema.
                        fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(i, databaseName, _coreFeatureConfigOptions));
                        await fhirStorageTestsFixture.InitializeAsync();

                        Mediator mediator = fhirStorageTestsFixture.Mediator;

                        // add a new resource
                        var saveResult = await mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                        var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

                        // delete the resource
                        var deletedResourceKey = await mediator.DeleteResourceAsync(new ResourceKey("Observation", saveResult.RawResourceElement.Id), DeleteOperation.SoftDelete);

                        // get resource changes
                        var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(databaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
                        IReadOnlyCollection<IResourceChangeData> resourceChanges = await resourceChangeDataStore.GetRecordsAsync(0, 200, CancellationToken.None);

                        Assert.NotNull(resourceChanges);
                        Assert.Equal(2, resourceChanges.Count);

                        var resourceChangeData = resourceChanges.Where(x => x.ResourceVersion == 2).First();

                        Assert.Equal(deserialized.Id, resourceChangeData.ResourceId);
                        Assert.Equal(2, resourceChangeData.ResourceChangeTypeId); // 2 is for resource deletion.
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Failure using schema version {i}", e);
                    }
                    finally
                    {
                        await fhirStorageTestsFixture?.DisposeAsync();
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"An error occurred on a resource change capture integration test", e);
            }
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
                for (int i = SchemaVersionConstants.SupportsResourceChangeCaptureSchemaVersion; i <= SchemaVersionConstants.Max; i++)
                {
                    string databaseName = $"FHIRRESOURCECHANGECAPTUREDISABLEDTEST_V{i}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    try
                    {
                        // this will either create the database or upgrade the schema.
                        _coreFeatureConfigOptions.Value.SupportsResourceChangeCapture = false;
                        fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(i, databaseName, _coreFeatureConfigOptions));
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
                        var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(0, 200, CancellationToken.None);

                        Assert.NotNull(resourceChanges);
                        Assert.Equal(0, resourceChanges.Count);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Failure using schema version {i}", e);
                    }
                    finally
                    {
                        await fhirStorageTestsFixture?.DisposeAsync();
                    }
                }
            }
            catch (Exception e)
            {
                throw new Exception($"An error occurred on a resource change capture integration test", e);
            }
        }

        /// <summary>
        /// A basic smoke test verifying that resource changes should not be created
        ///  when the resource change capture config is disabled.
        /// </summary>
        [Fact]
        public async Task GivenADatabase_WhenGettingResourceTypes_ThenCountShouldBeEqualToNumberOfResourceTypeNames()
        {
            FhirStorageTestsFixture fhirStorageTestsFixture = null;

            try
            {
                string databaseName = $"FHIRRESOURCETYPESTEST_V{SchemaVersionConstants.Max}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

                // this will either create the database or upgrade the schema.
                _coreFeatureConfigOptions.Value.SupportsResourceChangeCapture = false;
                fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, databaseName, _coreFeatureConfigOptions));
                await fhirStorageTestsFixture.InitializeAsync();

                // get resource types
                var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(GetSqlConnectionFactory(databaseName), NullLogger<SqlServerFhirResourceChangeDataStore>.Instance);
                var resourceChanges = await resourceChangeDataStore.GetResourceTypeMapAsync(CancellationToken.None);

                Assert.NotNull(resourceChanges);
                Assert.Equal(ModelInfoProvider.GetResourceTypeNames().Count, resourceChanges.Count);
            }
            catch (Exception e)
            {
                throw new Exception($"An error occurred on a resource change capture integration test", e);
            }
        }

        private static DefaultSqlConnectionFactory GetSqlConnectionFactory(string databaseName)
        {
            var initialConnectionString = Environment.GetEnvironmentVariable("SqlServer:ConnectionString") ?? LocalConnectionString;
            var testConnectionString = new SqlConnectionStringBuilder(initialConnectionString) { InitialCatalog = databaseName }.ToString();
            var schemaOptions = new SqlServerSchemaOptions { AutomaticUpdatesEnabled = false };
            var config = new SqlServerDataStoreConfiguration { ConnectionString = testConnectionString, Initialize = false, SchemaOptions = schemaOptions };
            var connectionStringProvider = new DefaultSqlConnectionStringProvider(config);
            var connectionFactory = new DefaultSqlConnectionFactory(connectionStringProvider);
            return connectionFactory;
        }
    }
}
