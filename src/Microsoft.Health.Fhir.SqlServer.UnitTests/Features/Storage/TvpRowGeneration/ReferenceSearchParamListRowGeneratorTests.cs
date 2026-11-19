// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage.TvpRowGeneration
{
    /// <summary>
    /// Regressions for the resource id truncation performed by <see cref="ReferenceSearchParamListRowGenerator"/>.
    /// The generator must truncate to the configured maximum resource id length, not to the schema default.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSearchParamListRowGeneratorTests : IClassFixture<ModelInfoProviderFixture>
    {
        private static readonly SearchParameterInfo Performer = new SearchParameterInfo(
            name: "performer",
            code: "performer",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-performer"));

        private static readonly SearchParameterInfo Subject = new SearchParameterInfo(
            name: "subject",
            code: "subject",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        /// <summary>
        /// Gets input lengths and independently specified retained lengths for both resource id policies.
        /// </summary>
        public static TheoryData<bool, int, int> ResourceIdLengths => new TheoryData<bool, int, int>
        {
            { false, 63, 63 },
            { false, 64, 64 },
            { false, 65, 64 },
            { false, 100, 64 },
            { false, 127, 64 },
            { false, 128, 64 },
            { false, 129, 64 },
            { true, 63, 63 },
            { true, 64, 64 },
            { true, 65, 65 },
            { true, 100, 100 },
            { true, 127, 127 },
            { true, 128, 128 },
            { true, 129, 128 },
        };

        [Theory]
        [MemberData(nameof(ResourceIdLengths))]
        public void GivenAReferenceNearTheIdLimits_WhenGeneratingARow_ThenTheExpectedPrefixIsRetained(bool useLongResourceIds, int length, int expectedLength)
        {
            // Arrange
            var generator = new ReferenceSearchParamListRowGenerator(CreateModel(ResourceIdPolicy.From(useLongResourceIds)), new SearchParameterToSearchValueTypeMap());
            string resourceId = CreateResourceId(length);
            var searchValue = new ReferenceSearchValue(ReferenceKind.External, new Uri("https://external-server.com/fhir/"), "Patient", resourceId);

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, searchValue, results: null, out ReferenceSearchParamListRow row);

            // Assert
            Assert.True(generated);
            Assert.Equal(resourceId[..expectedLength], row.ReferenceResourceId);
            Assert.Equal(1, row.ResourceTypeId);
            Assert.Equal(2L, row.ResourceSurrogateId);
            Assert.Equal(3, row.SearchParamId);
            Assert.Equal("https://external-server.com/fhir/", row.BaseUri);
            Assert.Equal((short)4, row.ReferenceResourceTypeId);
            Assert.Null(row.ReferenceResourceVersion);
        }

        [Theory]
        [MemberData(nameof(ResourceIdLengths))]
        public void GivenAReferenceTokenCompositeNearTheIdLimits_WhenGeneratingARow_ThenTheExpectedPrefixIsRetained(bool useLongResourceIds, int length, int expectedLength)
        {
            // Arrange
            SqlServerFhirModel model = CreateModel(ResourceIdPolicy.From(useLongResourceIds));
            var map = new SearchParameterToSearchValueTypeMap();
            var generator = new ReferenceTokenCompositeSearchParamListRowGenerator(
                model,
                new ReferenceSearchParamListRowGenerator(model, map),
                new TokenSearchParamListRowGenerator(model, map),
                map);
            string resourceId = CreateResourceId(length);
            var reference = new ReferenceSearchValue(ReferenceKind.External, new Uri("https://external-server.com/fhir/"), "Patient", resourceId);
            var token = new TokenSearchValue(system: null, code: "test-code", text: null);

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, (reference, token), results: null, out ReferenceTokenCompositeSearchParamListRow row);

            // Assert
            Assert.True(generated);
            Assert.Equal(resourceId[..expectedLength], row.ReferenceResourceId1);
            Assert.Equal(1, row.ResourceTypeId);
            Assert.Equal(2L, row.ResourceSurrogateId);
            Assert.Equal(3, row.SearchParamId);
            Assert.Equal("https://external-server.com/fhir/", row.BaseUri1);
            Assert.Equal((short)4, row.ReferenceResourceTypeId1);
            Assert.Null(row.ReferenceResourceVersion1);
            Assert.Null(row.SystemId2);
            Assert.Equal("test-code", row.Code2);
            Assert.Null(row.CodeOverflow2);
        }

        [Fact]
        public void GivenReferencesWithOverlongIds_WhenLogging_ThenOneWarningPerSearchParameterIsLoggedWithoutTheValues()
        {
            // Arrange
            var logger = new CapturingLogger();
            string first = CreateResourceId(65);
            MergeResourceWrapper[] resources = Wrap(
                new SearchIndexEntry(Performer, Overlong(first, 65)),
                new SearchIndexEntry(Performer, Overlong(CreateResourceId(70), 70)),
                new SearchIndexEntry(Performer, new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, "Patient", CreateResourceId(64))),
                new SearchIndexEntry(Subject, Overlong(CreateResourceId(100), 100)));

            // Act
            ReferenceSearchParamListRowGenerator.LogTruncatedReferences(resources, 64, logger);

            // Assert
            Assert.Collection(
                logger.Entries.OrderBy(e => e.Message, StringComparer.Ordinal),
                entry => AssertEntry(entry, Performer, count: 2, maxLength: 70),
                entry => AssertEntry(entry, Subject, count: 1, maxLength: 100));
            Assert.All(logger.Entries, entry => Assert.DoesNotContain(first[..64], entry.Message, StringComparison.Ordinal));
        }

        [Fact]
        public void GivenAReferenceTokenCompositeWithAnOverlongId_WhenLogging_ThenItIsLogged()
        {
            // Arrange
            var logger = new CapturingLogger();
            var composite = new CompositeSearchValue(new[]
            {
                new ISearchValue[] { Overlong(CreateResourceId(65), 65) },
                new ISearchValue[] { new TokenSearchValue(system: null, code: "test-code", text: null) },
            });
            MergeResourceWrapper[] resources = Wrap(new SearchIndexEntry(Performer, composite));

            // Act
            ReferenceSearchParamListRowGenerator.LogTruncatedReferences(resources, 64, logger);

            // Assert
            AssertEntry(Assert.Single(logger.Entries), Performer, count: 1, maxLength: 65);
        }

        [Fact]
        public void GivenReferencesWithinTheIdLimitOrHistory_WhenLogging_ThenNothingIsLogged()
        {
            // Arrange
            var logger = new CapturingLogger();
            var withinLimit = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, "Patient", CreateResourceId(64));

            // Act
            ReferenceSearchParamListRowGenerator.LogTruncatedReferences(Wrap(new SearchIndexEntry(Performer, withinLimit)), 64, logger);
            ReferenceSearchParamListRowGenerator.LogTruncatedReferences(Wrap(isHistory: true, new SearchIndexEntry(Performer, Overlong(CreateResourceId(65), 65))), 64, logger);

            // Assert
            Assert.Empty(logger.Entries);
        }

        private static string CreateResourceId(int length)
            => string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

        private static ReferenceSearchValue Overlong(string resourceId, int length)
            => new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, "Patient", resourceId[..Math.Min(resourceId.Length, 64)]) { OverlongResourceIdLength = length };

        private static void AssertEntry((LogLevel Level, string Message) entry, SearchParameterInfo searchParameter, int count, int maxLength)
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.Contains("ResourceType=Observation", entry.Message, StringComparison.Ordinal);
            Assert.Contains($"SearchParameter={searchParameter.Url}", entry.Message, StringComparison.Ordinal);
            Assert.Contains($"Count={count}", entry.Message, StringComparison.Ordinal);
            Assert.Contains($"MaxLength={maxLength}", entry.Message, StringComparison.Ordinal);
            Assert.Contains("IndexedLength=64", entry.Message, StringComparison.Ordinal);
        }

        private static MergeResourceWrapper[] Wrap(params SearchIndexEntry[] entries) => Wrap(isHistory: false, entries);

        private static MergeResourceWrapper[] Wrap(bool isHistory, params SearchIndexEntry[] entries) =>
            new[]
            {
                new MergeResourceWrapper(
                    new ResourceWrapper("obs-1", "1", "Observation", null, null, DateTimeOffset.UnixEpoch, false, entries, null, null, resourceSurrogateId: 1) { IsHistory = isHistory },
                    keepHistory: false,
                    hasVersionToCompare: false),
            };

        private static SqlServerFhirModel CreateModel(ResourceIdPolicy policy)
        {
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = SchemaVersionConstants.Max };
            var model = new SqlServerFhirModel(
                schema,
                Substitute.For<ISearchParameterDefinitionManager>(),
                () => Substitute.For<ISearchParameterStatusDataStore>(),
                Options.Create(new SecurityConfiguration()),
                Substitute.For<IScopeProvider<SqlConnectionWrapperFactory>>(),
                Substitute.For<IMediator>(),
                Substitute.For<ISqlRetryService>(),
                policy,
                NullLogger<SqlServerFhirModel>.Instance);

            SetModelField(model, "_highestInitializedVersion", schema.Current);
            SetModelField(model, "_resourceTypeToId", new Dictionary<string, short> { ["Patient"] = 4 });
            SetModelField(model, "_searchParamUriToId", new Dictionary<Uri, short> { [SearchParameterNames.IdUri] = 5 });
            return model;
        }

        private static void SetModelField(SqlServerFhirModel model, string name, object value)
            => typeof(SqlServerFhirModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(model, value);

        private sealed class CapturingLogger : ILogger
        {
            public List<(LogLevel Level, string Message)> Entries { get; } = new List<(LogLevel, string)>();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                Entries.Add((logLevel, formatter(state, exception)));
            }
        }
    }
}
