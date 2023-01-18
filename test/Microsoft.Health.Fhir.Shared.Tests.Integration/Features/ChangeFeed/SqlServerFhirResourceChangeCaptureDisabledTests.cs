// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Delete;
using Microsoft.Health.Fhir.SqlServer.Features.ChangeFeed;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.ChangeFeed
{
    /// <summary>
    /// Integration tests for a resource change capture feature.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class SqlServerFhirResourceChangeCaptureDisabledTests
    {
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
                var sqlFixture = new SqlServerFhirStorageTestsFixture(SchemaVersionConstants.Max, databaseName, coreFeatureConfigOptions);

                fhirStorageTestsFixture = new FhirStorageTestsFixture(sqlFixture);
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
                var resourceChangeDataStore = new SqlServerFhirResourceChangeDataStore(sqlFixture.SqlConnectionWrapperFactory, NullLogger<SqlServerFhirResourceChangeDataStore>.Instance, sqlFixture.SchemaInformation);
                var resourceChanges = await resourceChangeDataStore.GetRecordsAsync(1, 200, CancellationToken.None);

                Assert.NotNull(resourceChanges);
                Assert.Empty(resourceChanges);
            }
            finally
            {
                await fhirStorageTestsFixture.DisposeAsync();
            }
        }
    }
}
