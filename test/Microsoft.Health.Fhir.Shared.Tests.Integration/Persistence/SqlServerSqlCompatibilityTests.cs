// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
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
                for (int i = SchemaVersionConstants.Min; i <= SchemaVersionConstants.Max; i++)
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
                await fhirStorageTestsFixture?.DisposeAsync();
            }
        }

        /// <summary>
        /// A basic smoke test verifying that the code is compatible with schema versions
        /// all the way back to <see cref="SchemaVersionConstants.Min"/>.
        /// </summary>
        [Fact]
        public async Task GivenADatabaseWithAnEarlierSupportedSchema_WhenUpserting_OperationSucceeds()
        {
            for (int i = SchemaVersionConstants.Min; i <= SchemaVersionConstants.Max; i++)
            {
                string databaseName = $"FHIRCOMPATIBILITYTEST_V{i}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";

                var fhirStorageTestsFixture = new FhirStorageTestsFixture(new SqlServerFhirStorageTestsFixture(i, databaseName));
                try
                {
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
                    throw new InvalidOperationException($"Failure using schema version {i}", e);
                }
                finally
                {
                    await fhirStorageTestsFixture?.DisposeAsync();
                }
            }
        }
    }
}
