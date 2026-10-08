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
using Microsoft.Health.Fhir.Core.Features.Storage;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
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
    public class SearchParameterRowGeneratorTests
    {
        private const int RowCount = 1024;
        private const BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Theory]
        [InlineData("Token")]
        [InlineData("TokenText")]
        [InlineData("String")]
        [InlineData("Reference")]
        [InlineData("Uri")]
        [InlineData("Number")]
        [InlineData("Quantity")]
        [InlineData("Date")]
        [InlineData("Reference-Token")]
        [InlineData("Token-Token")]
        [InlineData("Token-Date")]
        [InlineData("Token-Quantity")]
        [InlineData("Token-String")]
        [InlineData("Token-Number-Number")]
        public void GivenManyValues_WhenGeneratingRows_ThenDeduplicationUsesAllFieldsAndLinearComparisons(string kind)
        {
            // Arrange
            string[] components = (kind == "TokenText" ? "Token" : kind).Split('-');
            var parameter = new SearchParameterInfo(
                "test",
                "test",
                components.Length == 1 ? Enum.Parse<ValueSets.SearchParamType>(components[0]) : ValueSets.SearchParamType.Composite,
                new Uri("https://example.org/search/test"),
                components.Select(c => new SearchParameterComponentInfo
                {
                    ResolvedSearchParameter = new SearchParameterInfo(c, c, Enum.Parse<ValueSets.SearchParamType>(c)),
                }).ToArray());
            var entries = Enumerable.Range(0, RowCount).Select(i =>
            {
                var value = components.Length == 1 ? CreateValue(components[0], i) : new CompositeSearchValue(
                    components.Select(c => (IReadOnlyList<ISearchValue>)new[] { CreateValue(c, i) }).ToArray());
                return new SearchIndexEntry(parameter, value);
            }).ToArray();
            var resources = Wrap(entries.Concat(entries).ToArray());
            var model = CreateModel(parameter);
            var map = new SearchParameterToSearchValueTypeMap();
            var token = new TokenSearchParamListRowGenerator(model, map);
            var text = new StringSearchParamListRowGenerator(model, map);
            var number = new NumberSearchParamListRowGenerator(model, map);
            var quantity = new QuantitySearchParamListRowGenerator(model, map);
            var date = new DateTimeSearchParamListRowGenerator(model, map);
            var reference = new ReferenceSearchParamListRowGenerator(model, map);
            date.GenerateRows(Array.Empty<MergeResourceWrapper>()).ToArray();

            // Act / Assert
            switch (kind)
            {
                case "Token": AssertRows(token.GenerateRows(resources)); break;
                case "TokenText": AssertRows(new TokenTextListRowGenerator(model, map).GenerateRows(resources)); break;
                case "String": AssertRows(text.GenerateRows(resources)); break;
                case "Reference": AssertRows(reference.GenerateRows(resources)); break;
                case "Uri": AssertRows(new UriSearchParamListRowGenerator(model, map).GenerateRows(resources)); break;
                case "Number": AssertRows(number.GenerateRows(resources)); break;
                case "Quantity": AssertRows(quantity.GenerateRows(resources)); break;
                case "Date": AssertRows(date.GenerateRows(resources)); break;
                case "Reference-Token": AssertRows(new ReferenceTokenCompositeSearchParamListRowGenerator(model, reference, token, map).GenerateRows(resources)); break;
                case "Token-Token": AssertRows(new TokenTokenCompositeSearchParamListRowGenerator(model, token, map).GenerateRows(resources)); break;
                case "Token-Date": AssertRows(new TokenDateTimeCompositeSearchParamListRowGenerator(model, token, date, map).GenerateRows(resources)); break;
                case "Token-Quantity": AssertRows(new TokenQuantityCompositeSearchParamListRowGenerator(model, token, quantity, map).GenerateRows(resources)); break;
                case "Token-String": AssertRows(new TokenStringCompositeSearchParamListRowGenerator(model, token, text, map).GenerateRows(resources)); break;
                case "Token-Number-Number": AssertRows(new TokenNumberNumberCompositeSearchParamListRowGenerator(model, token, number, map).GenerateRows(resources)); break;
                default: throw new NotSupportedException(kind);
            }
        }

        [Fact]
        public void GivenManyClaims_WhenGeneratingRows_ThenDeduplicationUsesAllFieldsAndLinearComparisons()
        {
            // Arrange
            var model = Substitute.For<ISqlServerFhirModel>();
            model.GetClaimTypeId("role").Returns((byte)1);
            var claims = Enumerable.Range(0, RowCount).Select(i => KeyValuePair.Create("role", $"Role-{i}")).ToArray();
            var resources = Wrap(Array.Empty<SearchIndexEntry>(), claims.Concat(
                claims.Select(c => KeyValuePair.Create(c.Key, c.Value.ToLowerInvariant()))).ToArray());
            var generator = new ResourceWriteClaimListRowGenerator(model, new SearchParameterToSearchValueTypeMap());

            // Act
            var rows = AssertRows(generator.GenerateRows(resources));

            // Assert
            Assert.Equal(claims.Select(c => c.Value), rows.Select(r => r.ClaimValue));
        }

        private static TRow[] AssertRows<TRow>(IEnumerable<TRow> generated)
            where TRow : struct
        {
            using var enumerator = generated.GetEnumerator();
            Assert.True(enumerator.MoveNext());

            // Capture the live iterator's actual deduplication set, including the separate claim generator.
            // Looking up a static comparer alone would miss regressions in constructor/set wiring.
            var set = Assert.Single(enumerator.GetType().GetFields(InstanceFields)
                .Select(f => f.GetValue(enumerator)).OfType<HashSet<TRow>>());
            var rows = new List<TRow> { enumerator.Current };
            while (enumerator.MoveNext())
            {
                rows.Add(enumerator.Current);
            }

            Assert.Equal(RowCount, rows.Count);
            Assert.Equal(RowCount, set.Count);
            var comparer = set.Comparer;
            int comparisons = 0;
            var countingComparer = EqualityComparer<TRow>.Create(
                (left, right) =>
                {
                    comparisons++;
                    return comparer.Equals(left, right);
                },
                comparer.GetHashCode);
            var countedSet = new HashSet<TRow>(countingComparer);
            foreach (var key in set.Concat(set))
            {
                countedSet.Add(key);
            }

            Assert.Equal(RowCount, countedSet.Count);

            // Permit incidental collisions, not the quadratic chain produced by default row hashing.
            Assert.InRange(comparisons, RowCount, RowCount * 4);
            AssertEveryInstanceFieldParticipatesInEquality(comparer);
            return rows.ToArray();
        }

        private static void AssertEveryInstanceFieldParticipatesInEquality<TRow>(IEqualityComparer<TRow> comparer)
            where TRow : struct
        {
            var fields = typeof(TRow).GetFields(InstanceFields);
            Assert.NotEmpty(fields);
            foreach (var field in fields)
            {
                var context = $"{typeof(TRow).Name}.{field.Name}";
                var rows = new List<TRow> { default };
                foreach (var value in GetFieldValues(field))
                {
                    // Boxed mutation covers future fields without maintaining a second field/constructor list.
                    object boxed = default(TRow);
                    field.SetValue(boxed, value);
                    var changed = (TRow)boxed;
                    Assert.False(EqualityComparer<TRow>.Default.Equals(default, changed), $"{context}: mutation must change default equality.");
                    rows.Add(changed);
                }

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
            }
        }

        private static IEnumerable<object> GetFieldValues(FieldInfo field)
        {
            var underlying = Nullable.GetUnderlyingType(field.FieldType);
            var type = underlying ?? field.FieldType;
            object[] values = type switch
            {
                var t when t == typeof(byte) => new object[] { (byte)1 },
                var t when t == typeof(short) => new object[] { (short)1 },
                var t when t == typeof(long) => new object[] { 1L },
                var t when t == typeof(int) => new object[] { 1 },
                var t when t == typeof(bool) => new object[] { true },
                var t when t == typeof(decimal) => new object[] { 1m, 1.0m, 2m },
                var t when t == typeof(DateTimeOffset) => new object[] { DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.ToOffset(TimeSpan.FromHours(1)), DateTimeOffset.UnixEpoch.AddTicks(1) },
                var t when t == typeof(string) => new object[] { string.Empty, "text", "Text", new string("text".ToCharArray()) },
                _ => throw new NotSupportedException($"Add test values for {field.DeclaringType.Name}.{field.Name} ({field.FieldType})."),
            };
            return underlying == null ? values : values.Prepend(Activator.CreateInstance(underlying));
        }

        private static ISearchValue CreateValue(string kind, int i) => kind switch
        {
            "Token" => new TokenSearchValue("system", $"code-{i}", $"display-{i}"),
            "String" => new StringSearchValue($"text-{i}"),
            "Reference" => new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null, null, $"id-{i}"),
            "Uri" => new UriSearchValue($"https://example.org/{i}", false),
            "Number" => new NumberSearchValue(i + 1m),
            "Quantity" => new QuantitySearchValue("system", "mg", i + 1m),
            "Date" => new DateTimeSearchValue(DateTimeOffset.UnixEpoch.AddDays(i)),
            _ => throw new NotSupportedException(kind),
        };

        private static MergeResourceWrapper[] Wrap(SearchIndexEntry[] entries, KeyValuePair<string, string>[] claims = null) =>
            new[]
            {
                new MergeResourceWrapper(
                    new ResourceWrapper("id", "1", "Patient", null, null, DateTimeOffset.UnixEpoch, false, entries, null, claims, resourceSurrogateId: 1),
                    keepHistory: false,
                    hasVersionToCompare: false),
            };

        private static SqlServerFhirModel CreateModel(SearchParameterInfo parameter)
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
            SetModelField(model, "_resourceTypeToId", new Dictionary<string, short> { ["Patient"] = 1 });
            SetModelField(model, "_searchParamUriToId", new Dictionary<Uri, short>
            {
                [SearchParameterNames.IdUri] = 1,
                [SearchParameterNames.LastUpdatedUri] = 2,
                [parameter.Url] = 3,
            });
            foreach (var field in new[] { "_systemToId", "_quantityCodeToId" })
            {
                var cache = new FhirMemoryCache<int>(field, NullLogger.Instance, ignoreCase: true);
                Assert.True(cache.TryAdd("system", 1));
                Assert.True(cache.TryAdd("mg", 2));
                SetModelField(model, field, cache);
            }

            return model;
        }

        private static void SetModelField(SqlServerFhirModel model, string name, object value) =>
            typeof(SqlServerFhirModel).GetField(name, InstanceFields).SetValue(model, value);
    }
}
