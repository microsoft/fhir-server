// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Integration tests for the SqlImporter class verifying import business logic including
    /// resource batching, error handling, progress tracking, and error store interaction.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class SqlServerImporterIntegrationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private readonly SqlServerFhirStorageTestsFixture _fixture;
        private readonly ITestOutputHelper _testOutputHelper;

        public SqlServerImporterIntegrationTests(SqlServerFhirStorageTestsFixture fixture, ITestOutputHelper testOutputHelper)
        {
            _fixture = fixture;
            _testOutputHelper = testOutputHelper;
        }

        [Fact]
        public async Task GivenValidResource_WhenImporting_ThenProgressReflectsSuccessAndProcessedBytes()
        {
            // Arrange
            var importer = CreateSqlImporter();
            var importErrorStore = CreateSuccessfulErrorStore();

            var channel = Channel.CreateUnbounded<ImportResource>();
            var id = Guid.NewGuid().ToString("N");
            var wrapper = CreateTestPatient(id);
            var resourceLength = wrapper.RawResource.Data.Length;

            // Act
            await channel.Writer.WriteAsync(new ImportResource(0, 0, resourceLength, true, false, false, wrapper));
            channel.Writer.Complete();

            var result = await importer.Import(channel, importErrorStore, ImportMode.InitialLoad, allowNegativeVersions: false, eventualConsistency: false, CancellationToken.None);

            // Assert - verify progress tracking business logic
            Assert.Equal(1, result.SucceededResources);
            Assert.Equal(0, result.FailedResources);
            Assert.Equal(1, result.CurrentIndex); // Index is 0-based input + 1
            Assert.Equal(resourceLength, result.ProcessedBytes);
        }

        [Fact]
        public async Task GivenResourceWithParsingError_WhenImporting_ThenErrorIsUploadedToErrorStoreAndCountedAsFailed()
        {
            // Arrange
            var importer = CreateSqlImporter();
            var uploadedErrors = new List<string>();
            var importErrorStore = Substitute.For<IImportErrorStore>();
            importErrorStore.UploadErrorsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    uploadedErrors.AddRange(callInfo.ArgAt<string[]>(0));
                    return Task.CompletedTask;
                });

            var channel = Channel.CreateUnbounded<ImportResource>();
            var wrapper = CreateTestPatient(Guid.NewGuid().ToString("N"));
            const string expectedError = "Parsing error: Invalid JSON at line 5";

            var errorResource = new ImportResource(0, 0, 100, true, false, false, wrapper);
            errorResource.ImportError = expectedError;

            // Act
            await channel.Writer.WriteAsync(errorResource);
            channel.Writer.Complete();

            var result = await importer.Import(channel, importErrorStore, ImportMode.InitialLoad, allowNegativeVersions: false, eventualConsistency: false, CancellationToken.None);

            // Assert - verify error handling business logic
            Assert.Equal(0, result.SucceededResources);
            Assert.Equal(1, result.FailedResources);
            Assert.Single(uploadedErrors);
            Assert.Equal(expectedError, uploadedErrors[0]);
            await importErrorStore.Received(1).UploadErrorsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenEmptyChannel_WhenImporting_ThenReturnsZeroProgressWithCurrentIndexAtZero()
        {
            // Arrange
            var importer = CreateSqlImporter();
            var importErrorStore = CreateSuccessfulErrorStore();
            var channel = Channel.CreateUnbounded<ImportResource>();

            // Act
            channel.Writer.Complete();
            var result = await importer.Import(channel, importErrorStore, ImportMode.InitialLoad, allowNegativeVersions: false, eventualConsistency: false, CancellationToken.None);

            // Assert - verify empty input edge case
            Assert.Equal(0, result.SucceededResources);
            Assert.Equal(0, result.FailedResources);
            Assert.Equal(0, result.CurrentIndex); // -1 + 1 = 0 for no resources
            Assert.Equal(0, result.ProcessedBytes);
        }

        [Fact]
        public async Task GivenMixedValidAndInvalidResources_WhenImporting_ThenValidResourcesSucceedAndInvalidResourcesFail()
        {
            // Arrange
            var importer = CreateSqlImporter();
            var uploadedErrors = new List<string>();
            var importErrorStore = Substitute.For<IImportErrorStore>();
            importErrorStore.UploadErrorsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(callInfo =>
                {
                    uploadedErrors.AddRange(callInfo.ArgAt<string[]>(0));
                    return Task.CompletedTask;
                });

            var channel = Channel.CreateUnbounded<ImportResource>();
            long totalExpectedBytes = 0;

            // Add 2 valid resources
            for (int i = 0; i < 2; i++)
            {
                var wrapper = CreateTestPatient(Guid.NewGuid().ToString("N"));
                totalExpectedBytes += wrapper.RawResource.Data.Length;
                await channel.Writer.WriteAsync(new ImportResource(i, 0, wrapper.RawResource.Data.Length, true, false, false, wrapper));
            }

            // Add 1 invalid resource with error
            var errorWrapper = CreateTestPatient(Guid.NewGuid().ToString("N"));
            var errorResource = new ImportResource(2, 0, 50, true, false, false, errorWrapper);
            errorResource.ImportError = "Schema validation failed";
            totalExpectedBytes += 50;
            await channel.Writer.WriteAsync(errorResource);

            // Add 1 more valid resource
            var finalWrapper = CreateTestPatient(Guid.NewGuid().ToString("N"));
            totalExpectedBytes += finalWrapper.RawResource.Data.Length;
            await channel.Writer.WriteAsync(new ImportResource(3, 0, finalWrapper.RawResource.Data.Length, true, false, false, finalWrapper));

            channel.Writer.Complete();

            // Act
            var result = await importer.Import(channel, importErrorStore, ImportMode.InitialLoad, allowNegativeVersions: false, eventualConsistency: false, CancellationToken.None);

            // Assert - verify partial success business logic
            Assert.Equal(3, result.SucceededResources);
            Assert.Equal(1, result.FailedResources);
            Assert.Equal(4, result.CurrentIndex); // Last index (3) + 1
            Assert.Single(uploadedErrors);
            Assert.Equal("Schema validation failed", uploadedErrors[0]);
            Assert.Equal(totalExpectedBytes, result.ProcessedBytes);
        }

        [Fact]
        public async Task GivenCancellationRequested_WhenImporting_ThenOperationIsCancelled()
        {
            // Arrange
            var importer = CreateSqlImporter();
            var importErrorStore = Substitute.For<IImportErrorStore>();
            var channel = Channel.CreateUnbounded<ImportResource>();
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            // Act & Assert - verify cancellation is respected
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                importer.Import(channel, importErrorStore, ImportMode.InitialLoad, allowNegativeVersions: false, eventualConsistency: false, cts.Token));
        }

        [Fact]
        public async Task GivenErrorStoreFailure_WhenUploadingErrors_ThenExceptionIsPropagatedToCallers()
        {
            // Arrange
            var importer = CreateSqlImporter();
            var expectedException = new InvalidOperationException("Blob storage unavailable");
            var importErrorStore = Substitute.For<IImportErrorStore>();
            importErrorStore.UploadErrorsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(expectedException));

            var channel = Channel.CreateUnbounded<ImportResource>();
            var wrapper = CreateTestPatient(Guid.NewGuid().ToString("N"));
            var errorResource = new ImportResource(0, 0, 100, true, false, false, wrapper);
            errorResource.ImportError = "Test error";
            await channel.Writer.WriteAsync(errorResource);
            channel.Writer.Complete();

            // Act & Assert - verify error store failures propagate
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                importer.Import(channel, importErrorStore, ImportMode.InitialLoad, allowNegativeVersions: false, eventualConsistency: false, CancellationToken.None));

            Assert.Equal(expectedException.Message, exception.Message);
        }

        private IImportErrorStore CreateSuccessfulErrorStore()
        {
            var importErrorStore = Substitute.For<IImportErrorStore>();
            importErrorStore.UploadErrorsAsync(Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(Task.CompletedTask);
            return importErrorStore;
        }

        private SqlImporter CreateSqlImporter()
        {
            var operationsConfig = Options.Create(new OperationsConfiguration
            {
                Import = new ImportJobConfiguration
                {
                    TransactionSize = 100,
                },
            });

            return new SqlImporter(
                _fixture.SqlServerFhirDataStore,
                _fixture.SqlServerFhirModel,
                operationsConfig,
                NullLogger<SqlImporter>.Instance);
        }

        private ResourceWrapper CreateTestPatient(string id)
        {
            var lastUpdated = DateTimeOffset.UtcNow;
            var versionId = "1";
            var rawResourceData = Samples.GetJson("Patient");

            return new ResourceWrapper(
                id,
                versionId,
                "Patient",
                new RawResource(rawResourceData, FhirResourceFormat.Json, isMetaSet: false),
                new ResourceRequest("POST"),
                lastUpdated,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null,
                searchParameterHash: "hash")
            {
                IsHistory = false,
            };
        }
    }
}
