// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerSqlCompatibilityTests
    {
        /// <summary>
        /// A basic smoke test verifying that the code is compatible with schema versions
        /// all the way back to <see cref="SchemaVersionConstants.Min"/>. Ensures that the code can
        /// insert a resource using every schema version, and that we can read resources
        /// that were inserted into with earlier schemas.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseWithAnEarlierSupportedSchemaAndUpgraded_WhenUpsertingAfter_OperationSucceeds()
        {
            string databaseName = $"FHIRCOMPATIBILITYTEST_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var insertedElements = new List<string>();

            FhirStorageTestsFixture fhirStorageTestsFixture = null;
            try
            {
                for (int i = Math.Max(SchemaVersionConstants.Min, SchemaVersionConstants.Max - 5); i <= SchemaVersionConstants.Max; i++)
                {
                    try
                    {
                        fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(i, databaseName));
                        await fhirStorageTestsFixture.InitializeAsync(); // this will either create the database or upgrade the schema.

                        Mediator mediator = fhirStorageTestsFixture.Mediator;

                        foreach (string id in insertedElements)
                        {
                            // verify that we can read entries from previous versions
                            var readResult = (await mediator.GetResourceAsync(new ResourceKey("Observation", id))).ToResourceElement(fhirStorageTestsFixture.Deserializer);
                            Assert.Equal(id, readResult.Id);
                        }

                        // add a new entry

                        var saveResult = await mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                        var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);
                        var result = (await mediator.GetResourceAsync(new ResourceKey(deserialized.InstanceType, deserialized.Id, deserialized.VersionId))).ToResourceElement(fhirStorageTestsFixture.Deserializer);

                        Assert.NotNull(result);
                        Assert.Equal(deserialized.Id, result.Id);
                        insertedElements.Add(result.Id);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Failure using schema version {i}", e);
                    }
                }
            }
            finally
            {
                if (fhirStorageTestsFixture != null)
                {
                    await fhirStorageTestsFixture.DisposeAsync();
                }
            }
        }

        /// <summary>
        /// A basic smoke test verifying that the code is compatible with schema versions
        /// all the way back to <see cref="SchemaVersionConstants.Min"/>.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseWithAnEarlierSupportedSchema_WhenUpserting_OperationSucceeds()
        {
            // List<FhirStorageTestsFixture> fhirStorageTestsFixtures = new();
            var versions = Enum.GetValues(typeof(SchemaVersion)).OfType<object>().ToList().Select(x => Convert.ToInt32(x)).ToList();
            await Parallel.ForEachAsync(versions, new ParallelOptions { MaxDegreeOfParallelism = 2}, async (version, cancel) =>
            {
                if (version >= Math.Max(SchemaVersionConstants.Min, SchemaVersionConstants.Max - 5) && version <= SchemaVersionConstants.Max)
                {
                    string databaseName = $"FHIRCOMPATIBILITYTEST_V{version}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                    FhirStorageTestsFixture fhirStorageTestsFixture = null;
                    try
                    {
                        fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(version, databaseName));
                        await fhirStorageTestsFixture.InitializeAsync();

                        Mediator mediator = fhirStorageTestsFixture.Mediator;

                        var saveResult = await mediator.UpsertResourceAsync(Samples.GetJsonSample("Weight"));
                        var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);
                        var result = (await mediator.GetResourceAsync(new ResourceKey(deserialized.InstanceType, deserialized.Id, deserialized.VersionId))).ToResourceElement(fhirStorageTestsFixture.Deserializer);

                        Assert.NotNull(result);
                        Assert.Equal(deserialized.Id, result.Id);
                    }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException($"Failure using schema version {version}", e);
                    }
                    finally
                    {
                        if (fhirStorageTestsFixture != null)
                        {
                            await fhirStorageTestsFixture.DisposeAsync();
                        }
                    }
                }
            });
        }

        /// <summary>
        /// With <see cref="SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion"/> we are
        /// changing the way _sort works. There might be a situation where code has been upgraded but schema
        /// version has not been upgraded. This test does a sanity check to make sure "old" _sort
        /// still works in such a scenario.
        /// </summary>
        [SkippableFact]
        public async Task GivenADatabaseWithAnEarlierSupportedSchema_WhenSearchingWithSort_SearchIsSuccessful()
        {
            Skip.If(SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion < SchemaVersionConstants.Min, "Schema version required for this test is not supported");

            string databaseName = $"FHIRCOMPATIBILITYTEST_SORT_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            int schemaVersion = SchemaVersionConstants.AddMinMaxForDateAndStringSearchParamVersion - 1;
            var fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(
                schemaVersion,
                databaseName));

            try
            {
                await fhirStorageTestsFixture.InitializeAsync();

                Mediator mediator = fhirStorageTestsFixture.Mediator;

                var saveResult = await mediator.UpsertResourceAsync(Samples.GetDefaultPatient());
                var deserialized = saveResult.RawResourceElement.ToResourceElement(Deserializers.ResourceDeserializer);

                List<Tuple<string, string>> queries = new List<Tuple<string, string>>();
                queries.Add(new Tuple<string, string>("_sort", "birthdate"));

                Core.Models.ResourceElement searchResult = await mediator.SearchResourceAsync("Patient", queries);
                var bundle = searchResult.ResourceInstance as Bundle;

                Assert.NotNull(searchResult);
                Assert.Single(bundle.Entry);
            }
            catch (Exception e)
            {
                throw new InvalidOperationException($"Sort query failed for schema version {schemaVersion}", e);
            }
            finally
            {
                fhirStorageTestsFixture.Dispose();
            }
        }
    }
}
