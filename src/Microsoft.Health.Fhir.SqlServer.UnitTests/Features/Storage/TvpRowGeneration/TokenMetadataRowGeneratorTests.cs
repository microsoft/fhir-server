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
        private const int RowCount = 1024;
        private static readonly SearchParameterInfo Tag = new SearchParameterInfo(
            "_tag", "_tag", ValueSets.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-tag"));

        private readonly TokenSearchParamListRow[] _tokenRows;
        private readonly TokenTextListRow[] _textRows;
        private readonly IEqualityComparer<TokenSearchParamListRow> _tokenComparer;
        private readonly IEqualityComparer<TokenTextListRow> _textComparer;

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
            });

            var entries = Enumerable.Range(0, RowCount)
                .Select(i => new SearchIndexEntry(Tag, new TokenSearchValue(null, $"code-{i}", $"display-{i}"))).ToArray();
            var resource = new MergeResourceWrapper(
                new ResourceWrapper(
                    "id", "1", "Patient", null, null, DateTimeOffset.UnixEpoch, false, entries.Concat(entries).ToArray(), null, null, resourceSurrogateId: 1),
                keepHistory: false,
                hasVersionToCompare: false);
            var tokens = new CapturingTokenGenerator(model);
            var texts = new CapturingTextGenerator(model);
            _tokenRows = tokens.GenerateRows(new[] { resource }).ToArray();
            _textRows = texts.GenerateRows(new[] { resource }).ToArray();
            _tokenComparer = tokens.Comparer;
            _textComparer = texts.Comparer;
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenManyMetadataValues_WhenGeneratingRows_ThenDeduplicationUsesLinearComparisons(bool textRows)
        {
            // Arrange / Act / Assert
            if (textRows)
            {
                AssertLinearComparisons(_textRows, _textComparer);
            }
            else
            {
                AssertLinearComparisons(_tokenRows, _tokenComparer);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenEveryInstanceField_WhenComparingRows_ThenPreservesDefaultEquality(bool textRows)
        {
            // Arrange / Act / Assert
            if (textRows)
            {
                AssertEveryInstanceFieldParticipatesInEquality(_textComparer);
            }
            else
            {
                AssertEveryInstanceFieldParticipatesInEquality(_tokenComparer);
            }
        }

        private static void AssertEveryInstanceFieldParticipatesInEquality<TRow>(IEqualityComparer<TRow> comparer)
            where TRow : struct
        {
            var fields = typeof(TRow).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.NotEmpty(fields);
            foreach (var field in fields)
            {
                var context = $"{typeof(TRow).Name}.{field.Name}";
                var rows = new List<TRow> { default };
                foreach (var value in GetFieldValues(field))
                {
                    // Boxed mutation covers future fields without updating a constructor or field list.
                    object boxed = default(TRow);
                    field.SetValue(boxed, value);
                    var changed = (TRow)boxed;

                    Assert.False(EqualityComparer<TRow>.Default.Equals(default, changed), $"{context}: mutation must change default equality.");
                    rows.Add(changed);
                }

                AssertDefaultEquality(rows, comparer, context);
            }
        }

        private static object[] GetFieldValues(FieldInfo field) =>
            field.FieldType switch
            {
                var type when type == typeof(short) => new object[] { (short)1 },
                var type when type == typeof(long) => new object[] { 1L },
                var type when type == typeof(int) => new object[] { 1 },
                var type when type == typeof(int?) => new object[] { 0, 1 },
                var type when type == typeof(string) => new object[] { string.Empty, "text", "Text", new string("text".ToCharArray()) },
                _ => throw new NotSupportedException($"Add test values for {field.DeclaringType.Name}.{field.Name} ({field.FieldType})."),
            };

        private static void AssertLinearComparisons<TRow>(TRow[] rows, IEqualityComparer<TRow> comparer)
        {
            Assert.Equal(RowCount, rows.Length);
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

            // Allow incidental hash collisions, but not the quadratic chain observed with default row hashing.
            Assert.InRange(comparisons, rows.Length, rows.Length * 4);
        }

        private static void AssertDefaultEquality<TRow>(IReadOnlyList<TRow> rows, IEqualityComparer<TRow> comparer, string context)
        {
            foreach (var left in rows)
            {
                foreach (var right in rows)
                {
                    bool expected = EqualityComparer<TRow>.Default.Equals(left, right);
                    Assert.True(expected == comparer.Equals(left, right), $"{context}: comparer differs from default equality.");
                    if (expected)
                    {
                        Assert.Equal(comparer.GetHashCode(left), comparer.GetHashCode(right));
                    }
                }
            }

            Assert.Equal(rows.Distinct().Count(), new HashSet<TRow>(rows, comparer).Count);
        }

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
