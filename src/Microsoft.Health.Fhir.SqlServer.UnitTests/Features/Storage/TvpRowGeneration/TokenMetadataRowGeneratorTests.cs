// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration.Merge;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage.TvpRowGeneration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenMetadataRowGeneratorTests
    {
        private static readonly SearchParameterInfo Tag = new SearchParameterInfo(
            "_tag", "_tag", ValueSets.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-tag"));

        private static readonly SearchParameterInfo Security = new SearchParameterInfo(
            "_security", "_security", ValueSets.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-security"));

        private readonly CapturingTokenGenerator _tokens;
        private readonly CapturingTextGenerator _texts;

        public TokenMetadataRowGeneratorTests()
        {
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max)
            {
                Current = SchemaVersionConstants.Max,
            };
            var model = new SqlServerFhirModel(
                schema,
                Substitute.For<ISearchParameterDefinitionManager>(),
                () => Substitute.For<ISearchParameterStatusDataStore>(),
                Options.Create(new SecurityConfiguration()),
                Substitute.For<IScopeProvider<SqlConnectionWrapperFactory>>(),
                Substitute.For<IMediator>(),
                Substitute.For<ISqlRetryService>(),
                NullLogger<SqlServerFhirModel>.Instance);

            // Seed the model's lookup tables without initializing a database.
            SetModelField(model, "_highestInitializedVersion", schema.Current);
            SetModelField(model, "_resourceTypeToId", new Dictionary<string, short> { ["Patient"] = 1 });
            SetModelField(model, "_searchParamUriToId", new Dictionary<Uri, short>
            {
                [SearchParameterNames.IdUri] = 1,
                [Tag.Url] = 2,
                [Security.Url] = 3,
            });

            _tokens = new CapturingTokenGenerator(model);
            _texts = new CapturingTextGenerator(model);
            var resource = CreateResource(1, new[] { Entry("code", "Display") });
            _tokens.GenerateRows(new[] { resource }).ToArray();
            _texts.GenerateRows(new[] { resource }).ToArray();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenManyMetadataValues_WhenGeneratingRows_ThenDeduplicationUsesLinearComparisons(bool textRows)
        {
            // Arrange
            const int count = 1024;
            var entries = Enumerable.Range(0, count).Select(i => Entry($"code-{i}", $"display-{i}")).ToArray();
            var resource = CreateResource(1, entries.Concat(entries).ToArray());

            // Act
            if (textRows)
            {
                var rows = _texts.GenerateRows(new[] { resource }).ToArray();

                // Assert
                Assert.Equal(count, rows.Length);
                AssertLinearComparisons(rows, _texts.Comparer);
            }
            else
            {
                var rows = _tokens.GenerateRows(new[] { resource }).ToArray();

                // Assert
                Assert.Equal(count, rows.Length);
                AssertLinearComparisons(rows, _tokens.Comparer);
            }
        }

        [Fact]
        public void GivenTokenFieldVariants_WhenComparingRows_ThenPreservesDefaultEquality()
        {
            // Arrange
            var rows = new[]
            {
                default,
                new TokenSearchParamListRow(1, 2, 3, null, "code", null),
                new TokenSearchParamListRow(1, 2, 3, null, new string("code".ToCharArray()), null),
                new TokenSearchParamListRow(2, 2, 3, null, "code", null),
                new TokenSearchParamListRow(1, 4, 3, null, "code", null),
                new TokenSearchParamListRow(1, 2, 4, null, "code", null),
                new TokenSearchParamListRow(1, 2, 3, 0, "code", null),
                new TokenSearchParamListRow(1, 2, 3, 4, "code", null),
                new TokenSearchParamListRow(1, 2, 3, null, "Code", null),
                new TokenSearchParamListRow(1, 2, 3, null, null, null),
                new TokenSearchParamListRow(1, 2, 3, null, string.Empty, null),
                new TokenSearchParamListRow(1, 2, 3, null, "code", string.Empty),
                new TokenSearchParamListRow(1, 2, 3, null, "code", "overflow"),
                new TokenSearchParamListRow(1, 2, 3, null, "code", "Overflow"),
            };

            // Act / Assert
            AssertDefaultEquality(rows, _tokens.Comparer);
        }

        [Fact]
        public void GivenTextFieldVariants_WhenComparingRows_ThenPreservesDefaultEquality()
        {
            // Arrange
            var rows = new[]
            {
                default,
                new TokenTextListRow(1, 2, 3, "text"),
                new TokenTextListRow(1, 2, 3, new string("text".ToCharArray())),
                new TokenTextListRow(2, 2, 3, "text"),
                new TokenTextListRow(1, 4, 3, "text"),
                new TokenTextListRow(1, 2, 4, "text"),
                new TokenTextListRow(1, 2, 3, "Text"),
                new TokenTextListRow(1, 2, 3, null),
                new TokenTextListRow(1, 2, 3, string.Empty),
            };

            // Act / Assert
            AssertDefaultEquality(rows, _texts.Comparer);
        }

        [Fact]
        public void GivenDuplicatesAcrossResourcesAndParameters_WhenGeneratingRows_ThenPreservesScopeAndCasing()
        {
            // Arrange
            var entries = new[]
            {
                Entry("code", "Display"),
                Entry("code", "Display"),
                Entry("Code", "DISPLAY"),
                new SearchIndexEntry(Security, new TokenSearchValue(null, "code", "Display")),
            };
            var resources = new[] { CreateResource(1, entries), CreateResource(2, entries), CreateResource(3, entries) };
            resources[2].ResourceWrapper.IsHistory = true;

            // Act
            var tokens = _tokens.GenerateRows(resources).ToArray();
            var texts = _texts.GenerateRows(resources).ToArray();

            // Assert
            Assert.Equal(6, tokens.Length);
            Assert.Equal(4, texts.Length);
            foreach (var id in new long[] { 1, 2 })
            {
                Assert.Equal(new[] { "code", "Code", "code" }, tokens.Where(r => r.ResourceSurrogateId == id).Select(r => r.Code));
                Assert.Equal(new short[] { 2, 3 }, texts.Where(r => r.ResourceSurrogateId == id).Select(r => r.SearchParamId));
            }

            Assert.All(texts, row => Assert.Equal("Display", row.Text));
            Assert.DoesNotContain(tokens, row => row.ResourceSurrogateId == 3);
            Assert.DoesNotContain(texts, row => row.ResourceSurrogateId == 3);
        }

        [Fact]
        public void GivenCodeOverflowAndBlankValues_WhenGeneratingRows_ThenPreservesFilteringAndOverflow()
        {
            // Arrange
            var prefix = new string('a', (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength);
            var resource = CreateResource(1, new[]
            {
                Entry(prefix, null),
                Entry(prefix + "x", " "),
                Entry(prefix + "x", null),
                Entry(prefix + "y", string.Empty),
                Entry(null, "text-only"),
                new SearchIndexEntry(
                    new SearchParameterInfo("_id", "_id", ValueSets.SearchParamType.Token, SearchParameterNames.IdUri),
                    new TokenSearchValue(null, "resource-id", null)),
            });

            // Act
            var tokens = _tokens.GenerateRows(new[] { resource }).ToArray();
            var texts = _texts.GenerateRows(new[] { resource }).ToArray();

            // Assert
            Assert.Equal(new[] { null, "x", "y" }, tokens.Select(r => r.CodeOverflow));
            Assert.All(tokens, row => Assert.Equal(prefix, row.Code));
            Assert.All(tokens, row => Assert.Null(row.SystemId));
            Assert.Equal("text-only", Assert.Single(texts).Text);
        }

        private static void AssertLinearComparisons<TRow>(TRow[] rows, IEqualityComparer<TRow> comparer)
        {
            int comparisons = 0;
            var countingComparer = EqualityComparer<TRow>.Create(
                (left, right) =>
                {
                    comparisons++;
                    return comparer.Equals(left, right);
                },
                comparer.GetHashCode);
            var set = new HashSet<TRow>(countingComparer);
            foreach (var row in rows.Concat(rows))
            {
                set.Add(row);
            }

            Assert.Equal(rows.Length, set.Count);

            // Allow incidental hash collisions, but not the quadratic chain from hashing only ResourceTypeId.
            Assert.InRange(comparisons, rows.Length, rows.Length * 4);
        }

        private static void AssertDefaultEquality<TRow>(TRow[] rows, IEqualityComparer<TRow> comparer)
        {
            foreach (var left in rows)
            {
                foreach (var right in rows)
                {
                    bool expected = EqualityComparer<TRow>.Default.Equals(left, right);
                    Assert.Equal(expected, comparer.Equals(left, right));
                    if (expected)
                    {
                        Assert.Equal(comparer.GetHashCode(left), comparer.GetHashCode(right));
                    }
                }
            }

            Assert.Equal(rows.Distinct().Count(), new HashSet<TRow>(rows, comparer).Count);
        }

        private static SearchIndexEntry Entry(string code, string text) => new SearchIndexEntry(Tag, new TokenSearchValue(null, code, text));

        private static MergeResourceWrapper CreateResource(long id, IReadOnlyCollection<SearchIndexEntry> entries) =>
            new MergeResourceWrapper(
                new ResourceWrapper(
                    id.ToString(), "1", "Patient", null, null, DateTimeOffset.UnixEpoch, false, entries, null, null, resourceSurrogateId: id),
                keepHistory: false,
                hasVersionToCompare: false);

        private static void SetModelField(SqlServerFhirModel model, string name, object value) =>
            typeof(SqlServerFhirModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(model, value);

        private sealed class CapturingTokenGenerator : TokenSearchParamListRowGenerator
        {
            public CapturingTokenGenerator(SqlServerFhirModel model)
                : base(model, new SearchParameterToSearchValueTypeMap())
            {
            }

            public IEqualityComparer<TokenSearchParamListRow> Comparer { get; private set; }

            internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, TokenSearchValue searchValue, HashSet<TokenSearchParamListRow> results, out TokenSearchParamListRow row)
            {
                Comparer = results.Comparer;
                return base.TryGenerateRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue, results, out row);
            }
        }

        private sealed class CapturingTextGenerator : TokenTextListRowGenerator
        {
            public CapturingTextGenerator(SqlServerFhirModel model)
                : base(model, new SearchParameterToSearchValueTypeMap())
            {
            }

            public IEqualityComparer<TokenTextListRow> Comparer { get; private set; }

            internal override bool TryGenerateRow(short resourceTypeId, long resourceSurrogateId, short searchParamId, TokenSearchValue searchValue, HashSet<TokenTextListRow> results, out TokenTextListRow row)
            {
                Comparer = results.Comparer;
                return base.TryGenerateRow(resourceTypeId, resourceSurrogateId, searchParamId, searchValue, results, out row);
            }
        }
    }
}
