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
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage.TvpRowGeneration
{
    /// <summary>
    /// Tests for URI values longer than the URI search index column.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class UriSearchParamListRowGeneratorTests
    {
        private const int IndexedLength = 256; // VLatest.UriSearchParam.Uri.Metadata.MaxLength
        private const string Prefix = "https://careplan.invalid/";

        private static readonly SearchParameterInfo InstantiatesUri = new SearchParameterInfo(
            name: "instantiates-uri",
            code: "instantiates-uri",
            searchParamType: SearchParamType.Uri,
            url: new Uri("http://hl7.org/fhir/SearchParameter/CarePlan-instantiates-uri"));

        public static TheoryData<string, string, string> CollidingValues => new TheoryData<string, string, string>
        {
            // Two different values that share the first 256 characters.
            { CreateUri(270, "allow"), CreateUri(270, "private"), "instantiates-uri" },

            // A value of exactly 256 characters and a longer value that starts with it.
            { new string('x', 256), new string('x', 256) + "more", "instantiates-uri" },

            // The same value twice is a duplicate, not a collision.
            { CreateUri(270, "same"), CreateUri(270, "same"), null },

            // Values that differ within the first 256 characters.
            { "https://a.example/" + new string('b', 300), "https://b.example/" + new string('b', 300), null },

            // Short values are compared in full.
            { CreateUri(255, "a"), CreateUri(255, "b"), null },
        };

        [Fact]
        public void GivenTheIndexedLength_WhenReadFromTheSchema_ThenItMatchesTheColumn()
        {
            Assert.Equal(IndexedLength, UriSearchParamListRowGenerator.IndexedUriMaxLength);
            Assert.Equal(IndexedLength, (int)VLatest.UriSearchParam.Uri.Metadata.MaxLength);
        }

        [Theory]
        [InlineData(255, 255)]
        [InlineData(256, 256)]
        [InlineData(257, 256)]
        [InlineData(1000, 256)]
        public void GivenAUri_WhenGeneratingARow_ThenAtMostTheIndexedLengthIsRetained(int length, int expectedLength)
        {
            // Arrange
            var generator = new UriSearchParamListRowGenerator(CreateModel(), new SearchParameterToSearchValueTypeMap());
            string uri = CreateUri(length, "x");

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, new UriSearchValue(uri, false), results: null, out UriSearchParamListRow row);

            // Assert
            Assert.True(generated);
            Assert.Equal(uri[..expectedLength], row.Uri);
            Assert.Equal(1, row.ResourceTypeId);
            Assert.Equal(2L, row.ResourceSurrogateId);
            Assert.Equal(3, row.SearchParamId);
        }

        [Fact]
        public void GivenTwoUrisThatDifferOnlyPastTheIndexedLength_WhenGeneratingRows_ThenOneRowIsGenerated()
        {
            // Arrange
            var generator = new UriSearchParamListRowGenerator(CreateModel(), new SearchParameterToSearchValueTypeMap());
            string allow = CreateUri(270, "allow");
            string @private = CreateUri(270, "private");
            MergeResourceWrapper[] resources = Wrap(
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(allow, false)),
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(@private, false)));

            // Act
            UriSearchParamListRow[] rows = generator.GenerateRows(resources).ToArray();

            // Assert
            UriSearchParamListRow row = Assert.Single(rows);
            Assert.Equal(allow[..IndexedLength], row.Uri);
            Assert.Equal(@private[..IndexedLength], row.Uri);
        }

        [Fact]
        public void GivenUrisLongerThanTheIndexedLength_WhenLogging_ThenOneWarningPerSearchParameterIsLoggedWithoutTheValues()
        {
            // Arrange
            var logger = new CapturingLogger();
            string first = CreateUri(270, "allow");
            string second = CreateUri(300, "private");
            MergeResourceWrapper[] resources = Wrap(
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(first, false)),
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(second, false)),
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(IndexedLength, "x"), false)));

            // Act
            UriSearchParamListRowGenerator.LogTruncatedUris(resources, logger);

            // Assert
            (LogLevel level, string message) = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, level);
            Assert.Contains("ResourceType=CarePlan", message, StringComparison.Ordinal);
            Assert.Contains($"SearchParameter={InstantiatesUri.Url}", message, StringComparison.Ordinal);
            Assert.Contains("Count=2", message, StringComparison.Ordinal);
            Assert.Contains("MaxLength=300", message, StringComparison.Ordinal);
            Assert.Contains("IndexedLength=256", message, StringComparison.Ordinal);
            Assert.DoesNotContain(Prefix, message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(255)]
        [InlineData(256)]
        public void GivenUrisWithinTheIndexedLength_WhenLogging_ThenNothingIsLogged(int length)
        {
            // Arrange
            var logger = new CapturingLogger();
            MergeResourceWrapper[] resources = Wrap(new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(length, "x"), false)));

            // Act
            UriSearchParamListRowGenerator.LogTruncatedUris(resources, logger);

            // Assert
            Assert.Empty(logger.Entries);
        }

        [Fact]
        public void GivenAHistoryResourceWithALongUri_WhenLogging_ThenNothingIsLogged()
        {
            // Arrange
            var logger = new CapturingLogger();
            MergeResourceWrapper[] resources = Wrap(isHistory: true, new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(300, "x"), false)));

            // Act
            UriSearchParamListRowGenerator.LogTruncatedUris(resources, logger);

            // Assert
            Assert.Empty(logger.Entries);
        }

        [Theory]
        [MemberData(nameof(CollidingValues))]
        public void GivenTwoValuesOfOneSearchParameter_WhenCheckingForCollisions_ThenOnlyValuesSharingTheIndexedPrefixCollide(string first, string second, string expected)
        {
            // Arrange
            ResourceWrapper resource = Wrap(
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(first, false)),
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(second, false)))[0].ResourceWrapper;

            // Act
            string result = UriSearchParamListRowGenerator.FindCollidingUriSearchParameter(resource);

            // Assert
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GivenValuesSharingTheIndexedPrefixInDifferentSearchParameters_WhenCheckingForCollisions_ThenNoneIsFound()
        {
            // Arrange
            var profile = new SearchParameterInfo("_profile", "_profile", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/Resource-profile"));
            ResourceWrapper resource = Wrap(
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "allow"), false)),
                new SearchIndexEntry(profile, new UriSearchValue(CreateUri(270, "private"), false)))[0].ResourceWrapper;

            // Act
            string result = UriSearchParamListRowGenerator.FindCollidingUriSearchParameter(resource);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void GivenSeveralCollidingSearchParameters_WhenGettingCollisions_ThenEachIsReturnedOnce()
        {
            // Arrange
            var profile = new SearchParameterInfo("_profile", "_profile", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/Resource-profile"));
            ResourceWrapper resource = Wrap(
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "a"), false)),
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "b"), false)),
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "c"), false)),
                new SearchIndexEntry(profile, new UriSearchValue(CreateUri(300, "a"), false)),
                new SearchIndexEntry(profile, new UriSearchValue(CreateUri(300, "b"), false)))[0].ResourceWrapper;

            // Act
            IReadOnlyList<SearchParameterInfo> result = UriSearchParamListRowGenerator.GetCollidingUriSearchParameters(resource);

            // Assert
            Assert.Equal(new[] { InstantiatesUri.Url, profile.Url }, result.Select(p => p.Url));
        }

        [Fact]
        public void GivenAResourceWithCollidingValues_WhenExcludingForReindex_ThenOnlyThatSearchParameterIsExcludedAndReported()
        {
            // Arrange
            var logger = new CapturingLogger();
            var profile = new SearchParameterInfo("_profile", "_profile", SearchParamType.Uri, new Uri("http://hl7.org/fhir/SearchParameter/Resource-profile"));
            var status = new SearchParameterInfo("status", "status", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/CarePlan-status"));
            var profileEntry = new SearchIndexEntry(profile, new UriSearchValue(CreateUri(300, "only"), false));
            var statusEntry = new SearchIndexEntry(status, new TokenSearchValue(null, "active", null));
            ResourceWrapper colliding = Wrap(
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "allow"), false)),
                new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "private"), false)),
                profileEntry,
                statusEntry)[0].ResourceWrapper;
            var otherEntry = new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "allow"), false));
            ResourceWrapper other = Wrap(otherEntry)[0].ResourceWrapper;

            // Act
            List<string> warnings = SqlServerFhirDataStore.ExcludeCollidingUriSearchValues(new[] { colliding, other }, logger);

            // Assert
            Assert.Equal(new[] { profileEntry, statusEntry }, colliding.SearchIndices);
            Assert.Equal(new[] { otherEntry }, other.SearchIndices);
            string warning = Assert.Single(warnings);
            Assert.Contains("CarePlan/cp-a", warning, StringComparison.Ordinal);
            Assert.Contains("'instantiates-uri'", warning, StringComparison.Ordinal);
            Assert.Contains("256", warning, StringComparison.Ordinal);
            Assert.DoesNotContain(Prefix, warning, StringComparison.Ordinal);
            (LogLevel level, string message) = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Warning, level);
            Assert.Contains($"SearchParameter={InstantiatesUri.Url}", message, StringComparison.Ordinal);
            Assert.DoesNotContain(Prefix, message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenResourcesWithoutCollisions_WhenExcludingForReindex_ThenNothingChanges()
        {
            // Arrange
            var logger = new CapturingLogger();
            var first = new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "same"), false));
            var second = new SearchIndexEntry(InstantiatesUri, new UriSearchValue(CreateUri(270, "same"), false));
            ResourceWrapper resource = Wrap(first, second)[0].ResourceWrapper;
            IReadOnlyCollection<SearchIndexEntry> before = resource.SearchIndices;

            // Act
            List<string> warnings = SqlServerFhirDataStore.ExcludeCollidingUriSearchValues(new[] { resource }, logger);

            // Assert
            Assert.Empty(warnings);
            Assert.Same(before, resource.SearchIndices);
            Assert.Empty(logger.Entries);
        }

        private static string CreateUri(int length, string suffix)
        {
            // The shared 'b' run ensures URIs with different suffixes only differ after the indexed length when length > 256.
            int fill = length - Prefix.Length - suffix.Length;
            return fill >= 0 ? Prefix + new string('b', fill) + suffix : (Prefix + suffix)[..length];
        }

        private static MergeResourceWrapper[] Wrap(params SearchIndexEntry[] entries) => Wrap(isHistory: false, entries);

        private static MergeResourceWrapper[] Wrap(bool isHistory, params SearchIndexEntry[] entries) =>
            new[]
            {
                new MergeResourceWrapper(
                    new ResourceWrapper("cp-a", "1", "CarePlan", null, null, DateTimeOffset.UnixEpoch, false, entries, null, null, resourceSurrogateId: 1) { IsHistory = isHistory },
                    keepHistory: false,
                    hasVersionToCompare: false),
            };

        private static SqlServerFhirModel CreateModel()
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
                ResourceIdPolicy.Standard,
                NullLogger<SqlServerFhirModel>.Instance);

            SetModelField(model, "_highestInitializedVersion", schema.Current);
            SetModelField(model, "_resourceTypeToId", new Dictionary<string, short> { ["CarePlan"] = 1 });
            SetModelField(model, "_searchParamUriToId", new Dictionary<Uri, short>
            {
                [SearchParameterNames.IdUri] = 1,
                [SearchParameterNames.LastUpdatedUri] = 2,
                [InstantiatesUri.Url] = 3,
            });
            return model;
        }

        private static void SetModelField(SqlServerFhirModel model, string name, object value) =>
            typeof(SqlServerFhirModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(model, value);

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
