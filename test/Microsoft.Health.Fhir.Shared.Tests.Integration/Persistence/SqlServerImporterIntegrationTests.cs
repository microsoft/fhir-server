// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.FirelySdk.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Ignixa.Features.Operations.Import;
using Microsoft.Health.Fhir.Ignixa.Features.Persistence;
using Microsoft.Health.Fhir.Shared.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Operations.Import;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Persistence
{
    /// <summary>
    /// Integration tests for the SqlImporter class verifying import business logic including
    /// resource batching, error handling, progress tracking, and error store interaction.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.FhirImport)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    [Collection(nameof(SqlImporterClockCollection))]
    public class SqlServerImporterIntegrationTests : IClassFixture<SqlServerFhirStorageTestsFixture>
    {
        private const string EqualitySkip = "Requires AB#206676 (2.2 equality)";
        private const string RetainedTimestamp = "2020-01-02T03:04:05.123+00:00";
        private const string PinnedTimestamp = "2020-06-01T00:00:00+00:00";

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

        [Fact]
        public async Task GivenIgnixaNativeWrites_WhenImporting_ThenStoredRawJsonIsNativeOutputAndReadsBack()
        {
            string id = Guid.NewGuid().ToString("N");
            string ndjson = $$$"""{"resourceType":"Observation","id":"{{{id}}}","meta":{"versionId":"7","lastUpdated":"{{{RetainedTimestamp}}}"},"valueQuantity":{"value":1.50},"effectiveDateTime":"2020-01-01T10:00:00.000+05:00","code":{"text":"a  b"},"status":"final"}""";
            var ignixa = CreateImportParser(native: true);
            var firely = CreateImportParser(native: false);
            var expectedNative = Parse(ignixa, ndjson).ResourceWrapper.RawResource.Data;

            await AssertSuccessfulImportAsync(ignixa, ndjson);

            var row = Assert.Single(await ReadHistoryAsync("Observation", id));
            Assert.Equal(7, row.Version);
            Assert.False(row.IsHistory);
            Assert.False(row.IsDeleted);
            Assert.Equal(expectedNative, row.RawJson);
            using var raw = JsonDocument.Parse(row.RawJson);
            Assert.Equal(
                new[] { "resourceType", "id", "meta", "valueQuantity", "effectiveDateTime", "code", "status" },
                raw.RootElement.EnumerateObject().Select(property => property.Name));
            Assert.Equal("1.50", raw.RootElement.GetProperty("valueQuantity").GetProperty("value").GetRawText());
            Assert.Equal("2020-01-01T10:00:00.000+05:00", raw.RootElement.GetProperty("effectiveDateTime").GetString());
            Assert.Equal("a  b", raw.RootElement.GetProperty("code").GetProperty("text").GetString());
            Assert.Equal(RetainedTimestamp, raw.RootElement.GetProperty("meta").GetProperty("lastUpdated").GetString());

            var jsonParser = CreateFirelyJsonParser();
            string firelyOutput = Parse(firely, ndjson).ResourceWrapper.RawResource.Data;
            Assert.NotEqual(firelyOutput, row.RawJson);
            Assert.True(jsonParser.Parse<Resource>(row.RawJson).IsExactly(jsonParser.Parse<Resource>(firelyOutput)));
        }

        [Fact]
        public async Task GivenIgnixaNativeWrites_WhenImportingPaddedText_ThenRawSqlRetainsSignificantWhitespace()
        {
            string id = Guid.NewGuid().ToString("N");
            string ndjson = $$$"""{"resourceType":"Observation","id":"{{{id}}}","meta":{"versionId":"1","lastUpdated":"{{{RetainedTimestamp}}}"},"status":"final","code":{"text":"  a  b  "}}""";

            await AssertSuccessfulImportAsync(CreateImportParser(native: true), ndjson);

            var row = Assert.Single(await ReadHistoryAsync("Observation", id));
            using var raw = JsonDocument.Parse(row.RawJson);
            Assert.Equal("  a  b  ", raw.RootElement.GetProperty("code").GetProperty("text").GetString());
        }

        [Fact]
        public async Task GivenIgnixaNativeWrites_WhenReimportingSameNdjsonWithKeepVersion_ThenSucceedsWithoutNewVersion()
        {
            string id = Guid.NewGuid().ToString("N");
            string ndjson = PatientLine(id);
            var ignixa = CreateImportParser(native: true);

            await AssertReimportIsIdempotentAsync(id, ignixa, ndjson, ignixa, ndjson);
        }

        [Fact]
        public async Task GivenChangedContentWithSameVersion_WhenReimported_ThenConflict()
        {
            string id = Guid.NewGuid().ToString("N");
            string original = PatientLine(id);
            var ignixa = CreateImportParser(native: true);
            await AssertSuccessfulImportAsync(ignixa, original);
            var before = Assert.Single(await ReadHistoryAsync("Patient", id));

            var (progress, uploadedErrors) = await ImportNdjsonAsync(ignixa, PatientLine(id, active: false));

            Assert.Equal(0, progress.SucceededResources);
            Assert.Equal(1, progress.FailedResources);
            var error = AssertSingleUploadedError(uploadedErrors);
            Assert.Contains("version conflict", error.Details.Text, StringComparison.Ordinal);
            Assert.Contains(id, error.Details.Text, StringComparison.Ordinal);
            Assert.Equal(before, Assert.Single(await ReadHistoryAsync("Patient", id)));
        }

        [Fact(Skip = EqualitySkip)]
        public async Task GivenReorderedPropertiesWithSameVersion_WhenReimported_ThenSucceedsWithoutNewVersion()
        {
            string id = Guid.NewGuid().ToString("N");
            var ignixa = CreateImportParser(native: true);
            string reordered = $$"""{"resourceType":"Patient","id":"{{id}}","meta":{"versionId":"1","lastUpdated":"{{RetainedTimestamp}}"},"gender":"unknown","active":true}""";

            await AssertReimportIsIdempotentAsync(id, ignixa, PatientLine(id), ignixa, reordered);
        }

        [Fact(Skip = EqualitySkip)]
        public async Task GivenFirelyWrittenResource_WhenReimportedWithIgnixaNativeWrites_ThenNoSpuriousVersion()
        {
            string id = Guid.NewGuid().ToString("N");
            string ndjson = $$"""{"resourceType":"Patient","id":"{{id}}","meta":{"versionId":"1","lastUpdated":"{{RetainedTimestamp}}"},"gender":"unknown","active":true}""";

            await AssertReimportIsIdempotentAsync(id, CreateImportParser(native: false), ndjson, CreateImportParser(native: true), ndjson);
        }

        [Fact(Skip = EqualitySkip)]
        public async Task GivenIgnixaWrittenResource_WhenReimportedWithFirely_ThenNoSpuriousVersion()
        {
            string id = Guid.NewGuid().ToString("N");
            string ndjson = $$"""{"resourceType":"Patient","id":"{{id}}","meta":{"versionId":"1","lastUpdated":"{{RetainedTimestamp}}"},"gender":"unknown","active":true}""";

            await AssertReimportIsIdempotentAsync(id, CreateImportParser(native: true), ndjson, CreateImportParser(native: false), ndjson);
        }

        [Theory]
        [InlineData("9", "2019-01-01T00:00:00+00:00")]
        [InlineData("1", PinnedTimestamp, Skip = "Requires AB#206676 (root-scoped SQL meta rewrite)")]
        public async Task GivenContainedMetaBeforeRootMeta_WhenStoreRewritesRootMeta_ThenOnlyRootMetaChanges(string containedVersion, string containedTimestamp)
        {
            string id = Guid.NewGuid().ToString("N");
            string ndjson = $$$"""{"resourceType":"Patient","contained":[{"resourceType":"Organization","id":"org","meta":{"versionId":"{{{containedVersion}}}","lastUpdated":"{{{containedTimestamp}}}"}}],"id":"{{{id}}}","meta":{"versionId":"1"},"active":false}""";
            var ignixa = CreateImportParser(native: true);
            await AssertSuccessfulImportAsync(ignixa, PatientLine(id));
            var original = Assert.Single(await ReadHistoryAsync("Patient", id));

            using var clock = Mock.Property(() => ClockResolver.TimeProvider, new FakeTimeProvider(DateTimeOffset.Parse(PinnedTimestamp, CultureInfo.InvariantCulture)));
            await AssertSuccessfulImportAsync(ignixa, ndjson);

            var history = await ReadHistoryAsync("Patient", id);
            Assert.Collection(
                history,
                first => Assert.Equal(original with { IsHistory = true }, first),
                current =>
                {
                    Assert.Equal(2, current.Version);
                    Assert.False(current.IsHistory);
                    Assert.False(current.IsDeleted);
                    using var raw = JsonDocument.Parse(current.RawJson);
                    var rootMeta = raw.RootElement.GetProperty("meta");
                    Assert.Equal("2", rootMeta.GetProperty("versionId").GetString());
                    Assert.Equal(current.SurrogateId.ToLastUpdated(), rootMeta.GetProperty("lastUpdated").GetDateTimeOffset());
                    var containedMeta = raw.RootElement.GetProperty("contained")[0].GetProperty("meta");
                    Assert.Equal(containedVersion, containedMeta.GetProperty("versionId").GetString());
                    Assert.Equal(containedTimestamp, containedMeta.GetProperty("lastUpdated").GetString());
                });
        }

        [Fact]
        public async Task GivenIgnixaNativeWrites_WhenImportingSoftDeleted_ThenHistoryAndDeleteStateMatchFirely()
        {
            string ignixaId = Guid.NewGuid().ToString("N");
            string firelyId = Guid.NewGuid().ToString("N");
            string ndjson = $$$"""{"resourceType":"Patient","id":"{{{ignixaId}}}","meta":{"versionId":"2","lastUpdated":"2020-01-03T03:04:05.123+00:00","extension":[{"url":"{{{KnownFhirPaths.AzureSoftDeletedExtensionUrl}}}","valueString":"soft-deleted"}]}}""";
            var ignixa = CreateImportParser(native: true);
            var firely = CreateImportParser(native: false);
            string original = PatientLine(ignixaId);
            string expectedOriginal = Parse(ignixa, original).ResourceWrapper.RawResource.Data;
            string expectedDeleted = Parse(ignixa, ndjson).ResourceWrapper.RawResource.Data;
            await AssertSuccessfulImportAsync(ignixa, original);
            await AssertSuccessfulImportAsync(firely, PatientLine(firelyId));

            await AssertSuccessfulImportAsync(ignixa, ndjson);
            await AssertSuccessfulImportAsync(firely, ndjson.Replace(ignixaId, firelyId, StringComparison.Ordinal));

            var ignixaHistory = await ReadHistoryAsync("Patient", ignixaId);
            var firelyHistory = await ReadHistoryAsync("Patient", firelyId);
            Assert.Equal(new[] { (1, true, false), (2, false, true) }, ignixaHistory.Select(row => (row.Version, row.IsHistory, row.IsDeleted)));
            Assert.Equal(new[] { (1, true, false), (2, false, true) }, firelyHistory.Select(row => (row.Version, row.IsHistory, row.IsDeleted)));
            Assert.Equal(expectedOriginal, ignixaHistory[0].RawJson);
            Assert.Equal(expectedDeleted, ignixaHistory[1].RawJson);
            for (int i = 0; i < ignixaHistory.Count; i++)
            {
                Assert.DoesNotContain(KnownFhirPaths.AzureSoftDeletedExtensionUrl, ignixaHistory[i].RawJson, StringComparison.Ordinal);
                AssertExactlyExceptId(ignixaHistory[i].RawJson, firelyHistory[i].RawJson);
            }
        }

        [Theory]
        [InlineData("""{"resourceType":"Patient","id":"patient-1","meta":{"versionId":"7","lastUpdated":"2026-01-02T03:04:05.123Z"},"active":true}""")]
        [InlineData("""{"resourceType":"Patient","id":"patient-1","meta":{"versionId":"7","lastUpdated":"2020-01-01T00:00:00Z","_lastUpdated":{"id":"original"}}}""")]
        [InlineData("""{"resourceType":"Patient","id":"patient-1","meta":{"versionId":"not-a-number","lastUpdated":"2020-01-01T00:00:00Z"}}""")]
        [InlineData("""{"resourceType":"Patient","id":"patient-1","meta":{"lastUpdated":"2026-01-02T03:04:05.123Z"},"extension":[{"url":"http://example.org/date","valueDate":"2001-02-03T04:05:06Z"}],"birthDate":"2001-02-03T04:05:06+10:00","_birthDate":{"extension":[{"url":"http://example.org/note","valueString":"shadow"}]},"deceasedDateTime":"2020-05-06T07:08:09Z","contained":[{"resourceType":"Patient","id":"contained-1","birthDate":"1999-12-31T23:59:59-05:00"},{"resourceType":"Patient","id":"contained-2","birthDate":"2001-02"}]}""")]
        public async Task GivenCorpus_WhenImportedByBothProviders_ThenDeserializedResourcesAreExactlyEqual(string ndjson)
        {
            string ignixaId = Guid.NewGuid().ToString("N");
            string firelyId = Guid.NewGuid().ToString("N");
            string ignixaLine = ndjson.Replace("patient-1", ignixaId, StringComparison.Ordinal);
            var ignixaParser = CreateImportParser(native: true);
            string expectedNative = Parse(ignixaParser, ignixaLine).ResourceWrapper.RawResource.Data;

            await AssertSuccessfulImportAsync(ignixaParser, ignixaLine);
            await AssertSuccessfulImportAsync(CreateImportParser(native: false), ndjson.Replace("patient-1", firelyId, StringComparison.Ordinal));

            var ignixa = Assert.Single(await ReadHistoryAsync("Patient", ignixaId));
            var firely = Assert.Single(await ReadHistoryAsync("Patient", firelyId));
            Assert.Equal(expectedNative, ignixa.RawJson);
            AssertExactlyExceptId(ignixa.RawJson, firely.RawJson);
        }

        [Theory]
        [InlineData("contains/slash")]
        [InlineData("01234567890123456789012345678901234567890123456789012345678901234")]
        public async Task GivenInvalidCorpusId_WhenImportedByBothProviders_ThenBothRejectTheLine(string invalidId)
        {
            string ndjson = PatientLine(invalidId);
            var ignixa = CreateImportParser(native: true);
            var firely = CreateImportParser(native: false);
            Assert.Throws<BadRequestException>(() => Parse(ignixa, ndjson));
            Assert.Throws<BadRequestException>(() => Parse(firely, ndjson));

            var ignixaResult = await ImportNdjsonAsync(ignixa, ndjson);
            var firelyResult = await ImportNdjsonAsync(firely, ndjson);

            Assert.Equal(0, ignixaResult.Progress.SucceededResources);
            Assert.Equal(1, ignixaResult.Progress.FailedResources);
            Assert.Equal(0, firelyResult.Progress.SucceededResources);
            Assert.Equal(1, firelyResult.Progress.FailedResources);
            var ignixaError = AssertSingleUploadedError(ignixaResult.UploadedErrors);
            var firelyError = AssertSingleUploadedError(firelyResult.UploadedErrors);
            Assert.Contains(invalidId, ignixaError.Details.Text, StringComparison.Ordinal);
            Assert.True(ignixaError.IsExactly(firelyError));
            Assert.Empty(await ReadHistoryAsync("Patient", invalidId));
        }

        private static IImportResourceParser CreateImportParser(bool native)
        {
            var firelyWriter = new RawResourceFactory(new FhirJsonSerializer());
            var modelInfo = new VersionSpecificModelInfoProvider();
            var schema = new IgnixaSchemaContext(modelInfo);
            var context = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            context.RequestContext.Method.Returns("PUT");
            context.RequestContext.Uri.Returns(new Uri("https://example.test/$import"));
            var searchIndexer = Substitute.For<ISearchIndexer>();
            searchIndexer.Extract(Arg.Any<ResourceElement>()).Returns(Array.Empty<SearchIndexEntry>());
            var wrapperFactory = new ResourceWrapperFactory(
                native ? new IgnixaRawResourceFactory(firelyWriter, schema, modelInfo) : firelyWriter,
                context,
                searchIndexer,
                Substitute.For<IClaimsExtractor>(),
                Substitute.For<ICompartmentIndexer>(),
                Substitute.For<ISearchParameterDefinitionManager>(),
                Deserializers.ResourceDeserializer);

            return native
                ? new IgnixaImportResourceParser(wrapperFactory, schema, ResourceIdPolicy.Standard)
                : new FirelyImportResourceParser(CreateFirelyJsonParser(), wrapperFactory, ResourceIdPolicy.Standard);
        }

        private static ImportResource Parse(IImportResourceParser parser, string ndjson)
            => parser.Parse(1, 0, Encoding.UTF8.GetByteCount(ndjson) + 1, ndjson, ImportMode.IncrementalLoad);

        private static string PatientLine(string id, bool active = true)
            => $$"""{"resourceType":"Patient","id":"{{id}}","meta":{"versionId":"1","lastUpdated":"{{RetainedTimestamp}}"},"active":{{(active ? "true" : "false")}},"gender":"unknown"}""";

        private static OperationOutcome.IssueComponent AssertSingleUploadedError(IReadOnlyList<string> uploadedErrors)
        {
            var error = Assert.Single(CreateFirelyJsonParser().Parse<OperationOutcome>(Assert.Single(uploadedErrors)).Issue);
            Assert.Equal(OperationOutcome.IssueSeverity.Error, error.Severity);
            Assert.Equal("Failed to process resource at line: 1 with stream start offset: 0", error.Diagnostics);
            return error;
        }

        private static void AssertExactlyExceptId(string ignixaJson, string firelyJson)
        {
            var parser = CreateFirelyJsonParser();
            var ignixa = parser.Parse<Resource>(ignixaJson);
            var firely = parser.Parse<Resource>(firelyJson);
            firely.Id = ignixa.Id;
            Assert.True(ignixa.IsExactly(firely), $"Ignixa: {ignixaJson}\nFirely: {firelyJson}");
        }

        private async Task AssertReimportIsIdempotentAsync(string id, IImportResourceParser first, string original, IImportResourceParser second, string reimport)
        {
            await AssertSuccessfulImportAsync(first, original);
            var before = Assert.Single(await ReadHistoryAsync("Patient", id));

            await AssertSuccessfulImportAsync(second, reimport);

            Assert.Equal(before, Assert.Single(await ReadHistoryAsync("Patient", id)));
        }

        private async Task AssertSuccessfulImportAsync(IImportResourceParser parser, string ndjson)
        {
            var (progress, uploadedErrors) = await ImportNdjsonAsync(parser, ndjson);
            Assert.Empty(uploadedErrors);
            Assert.Equal(1, progress.SucceededResources);
            Assert.Equal(0, progress.FailedResources);
        }

        private async Task<(ImportProcessingProgress Progress, IReadOnlyList<string> UploadedErrors)> ImportNdjsonAsync(IImportResourceParser parser, string ndjson)
        {
            var blob = new Uri("https://example.test/import.ndjson");
            byte[] data = Encoding.UTF8.GetBytes(ndjson + "\n");
            var blobStore = Substitute.For<IIntegrationDataStoreClient>();
            blobStore.DownloadResource(blob, 0, CancellationToken.None).Returns(_ => new MemoryStream(data));
            var loader = new ImportResourceLoader(blobStore, parser, new ImportErrorSerializer(new FhirJsonSerializer()), NullLogger<ImportResourceLoader>.Instance);
            var uploadedErrors = new List<string>();
            var errorStore = Substitute.For<IImportErrorStore>();
            errorStore.UploadErrorsAsync(Arg.Do<string[]>(uploadedErrors.AddRange), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
            var (channel, loadTask) = loader.LoadResources(blob.AbsoluteUri, 0, data.Length, null, ImportMode.IncrementalLoad, CancellationToken.None);

            try
            {
                var progress = await CreateSqlImporter().Import(channel, errorStore, ImportMode.IncrementalLoad, allowNegativeVersions: false, eventualConsistency: false, CancellationToken.None);
                return (progress, uploadedErrors);
            }
            finally
            {
                await loadTask;
            }
        }

        private async Task<List<StoredVersion>> ReadHistoryAsync(string resourceType, string id)
        {
            await using var connection = new SqlConnection(_fixture.TestConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Version, IsHistory, IsDeleted, ResourceSurrogateId, RawResource
                FROM dbo.Resource
                WHERE ResourceTypeId = @typeId AND ResourceId = @id
                ORDER BY Version
                """;
            command.Parameters.Add("@typeId", SqlDbType.SmallInt).Value = _fixture.SqlServerFhirModel.GetResourceTypeId(resourceType);
            command.Parameters.Add("@id", SqlDbType.VarChar, 128).Value = id;
            var converter = new CompressedRawResourceConverter();
            var history = new List<StoredVersion>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                using var compressed = reader.GetStream(4);
                history.Add(new StoredVersion(reader.GetInt32(0), reader.GetBoolean(1), reader.GetBoolean(2), reader.GetInt64(3), converter.ReadCompressedRawResource(compressed)));
            }

            return history;
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

#pragma warning disable CS0618
        private static FhirJsonParser CreateFirelyJsonParser() => new(new ParserSettings { PermissiveParsing = true, TruncateDateTimeToDate = true });
#pragma warning restore CS0618

        private sealed record StoredVersion(int Version, bool IsHistory, bool IsDeleted, long SurrogateId, string RawJson);
    }
}
