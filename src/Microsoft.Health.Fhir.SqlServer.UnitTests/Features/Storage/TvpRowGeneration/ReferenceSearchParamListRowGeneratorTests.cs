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
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Schema;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage.TvpRowGeneration
{
    /// <summary>
    /// Regressions for <see cref="ReferenceSearchParamListRowGenerator"/> and the reference half of
    /// <see cref="ReferenceTokenCompositeSearchParamListRowGenerator"/>. A reference is stored whole or rejected, never shortened.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSearchParamListRowGeneratorTests : IClassFixture<ModelInfoProviderFixture>
    {
        private const string BaseUriPrefix = "https://example.org/";

        /// <summary>
        /// Gets the active schema version, the reference id length, and the capacity that rejects it (or null when it is stored whole).
        /// A typed id longer than 128 characters cannot be constructed, so the latest-schema column cap is covered by <see cref="LiteralLengths"/>.
        /// </summary>
        public static TheoryData<int, int, int?> ResourceIdLengths => new TheoryData<int, int, int?>
        {
            { (int)SchemaVersion.V117, 63, null },
            { (int)SchemaVersion.V117, 64, null },
            { (int)SchemaVersion.V117, 65, 64 },
            { SchemaVersionConstants.Max, 127, null },
            { SchemaVersionConstants.Max, 128, null },
        };

        /// <summary>
        /// Gets the active schema version, the literal reference length, and the capacity that rejects it (or null when it is stored whole).
        /// </summary>
        public static TheoryData<int, int, int?> LiteralLengths => new TheoryData<int, int, int?>
        {
            { (int)SchemaVersion.V117, 64, null },
            { (int)SchemaVersion.V117, 65, 64 },
            { SchemaVersionConstants.Max, 128, null },
            { SchemaVersionConstants.Max, 129, 128 },
        };

        /// <summary>
        /// Gets the base URI length and the capacity that rejects it (or null when it is stored whole).
        /// </summary>
        public static TheoryData<int, int?> BaseUriLengths => new TheoryData<int, int?>
        {
            { 127, null },
            { 128, null },
            { 129, 128 },
            { 134, 128 },
        };

        [Theory]
        [MemberData(nameof(ResourceIdLengths))]
        public void GivenAReferenceIdNearTheColumnCapacity_WhenGeneratingARow_ThenTheWholeIdIsStoredOrRejected(int schemaVersion, int length, int? expectedCapacity)
        {
            var generator = CreateReferenceGenerator(schemaVersion);
            var reference = TestReferenceSearchValues.Resource(ReferenceKind.External, new Uri("https://external-server.com/fhir/"), "Patient", CreateText(length), ResourceIdPolicy.Extended);

            AssertStoredWholeOrRejected(
                () => generator.TryGenerateRow(1, 2L, 3, reference, results: null, out ReferenceSearchParamListRow row) ? row : default,
                row => AssertReferenceRow(reference, row),
                expectedCapacity,
                FieldName.ReferenceResourceId);
        }

        [Theory]
        [MemberData(nameof(ResourceIdLengths))]
        public void GivenAReferenceTokenCompositeIdNearTheColumnCapacity_WhenGeneratingARow_ThenTheWholeIdIsStoredOrRejected(int schemaVersion, int length, int? expectedCapacity)
        {
            var generator = CreateCompositeGenerator(schemaVersion);
            var reference = TestReferenceSearchValues.Resource(ReferenceKind.External, new Uri("https://external-server.com/fhir/"), "Patient", CreateText(length), ResourceIdPolicy.Extended);

            AssertStoredWholeOrRejected(
                () => generator.TryGenerateRow(1, 2L, 3, (reference, new TokenSearchValue(system: null, code: "test-code", text: null)), results: null, out ReferenceTokenCompositeSearchParamListRow row) ? row : default,
                row => AssertCompositeRow(reference, row),
                expectedCapacity,
                FieldName.ReferenceResourceId);
        }

        [Theory]
        [MemberData(nameof(BaseUriLengths))]
        public void GivenAReferenceBaseUriNearTheColumnCapacity_WhenGeneratingARow_ThenTheWholeBaseUriIsStoredOrRejected(int length, int? expectedCapacity)
        {
            var generator = CreateReferenceGenerator(SchemaVersionConstants.Max);
            var reference = TestReferenceSearchValues.Resource(ReferenceKind.External, CreateBaseUri(length), "Patient", "p1");

            AssertStoredWholeOrRejected(
                () => generator.TryGenerateRow(1, 2L, 3, reference, results: null, out ReferenceSearchParamListRow row) ? row : default,
                row => AssertReferenceRow(reference, row),
                expectedCapacity,
                FieldName.ReferenceBaseUri);
        }

        [Theory]
        [MemberData(nameof(BaseUriLengths))]
        public void GivenAReferenceTokenCompositeBaseUriNearTheColumnCapacity_WhenGeneratingARow_ThenTheWholeBaseUriIsStoredOrRejected(int length, int? expectedCapacity)
        {
            var generator = CreateCompositeGenerator(SchemaVersionConstants.Max);
            var reference = TestReferenceSearchValues.Resource(ReferenceKind.External, CreateBaseUri(length), "Patient", "p1");

            AssertStoredWholeOrRejected(
                () => generator.TryGenerateRow(1, 2L, 3, (reference, new TokenSearchValue(system: null, code: "test-code", text: null)), results: null, out ReferenceTokenCompositeSearchParamListRow row) ? row : default,
                row => AssertCompositeRow(reference, row),
                expectedCapacity,
                FieldName.ReferenceBaseUri);
        }

        [Theory]
        [MemberData(nameof(LiteralLengths))]
        public void GivenALiteralReferenceNearTheColumnCapacity_WhenGeneratingARow_ThenTheWholeLiteralIsStoredOrRejected(int schemaVersion, int length, int? expectedCapacity)
        {
            const string prefix = "urn:uuid:";
            var generator = CreateReferenceGenerator(schemaVersion);
            var reference = ReferenceSearchValue.FromLiteral(prefix + CreateText(length - prefix.Length));

            AssertStoredWholeOrRejected(
                () => generator.TryGenerateRow(1, 2L, 3, reference, results: null, out ReferenceSearchParamListRow row) ? row : default,
                row =>
                {
                    Assert.Equal(reference.ResourceId, row.ReferenceResourceId);
                    Assert.Null(row.BaseUri);
                    Assert.Null(row.ReferenceResourceTypeId);
                },
                expectedCapacity,
                FieldName.ReferenceResourceId);
        }

        private static void AssertStoredWholeOrRejected<TRow>(Func<TRow> generate, Action<TRow> assertRow, int? expectedCapacity, FieldName expectedComponent)
        {
            if (expectedCapacity == null)
            {
                assertRow(generate());
                return;
            }

            var exception = Assert.Throws<ReferenceStorageCapacityException>(() => generate());
            Assert.Equal(expectedComponent, exception.Component);
            Assert.Equal(expectedCapacity.Value, exception.Capacity);
        }

        private static void AssertReferenceRow(ReferenceSearchValue reference, ReferenceSearchParamListRow row)
        {
            Assert.Equal(reference.BaseUri.ToString(), row.BaseUri);
            Assert.Equal(reference.ResourceId, row.ReferenceResourceId);
            Assert.Equal(1, row.ResourceTypeId);
            Assert.Equal(2L, row.ResourceSurrogateId);
            Assert.Equal(3, row.SearchParamId);
            Assert.Equal((short)4, row.ReferenceResourceTypeId);
            Assert.Null(row.ReferenceResourceVersion);
        }

        private static void AssertCompositeRow(ReferenceSearchValue reference, ReferenceTokenCompositeSearchParamListRow row)
        {
            Assert.Equal(reference.BaseUri.ToString(), row.BaseUri1);
            Assert.Equal(reference.ResourceId, row.ReferenceResourceId1);
            Assert.Equal(1, row.ResourceTypeId);
            Assert.Equal(2L, row.ResourceSurrogateId);
            Assert.Equal(3, row.SearchParamId);
            Assert.Equal((short)4, row.ReferenceResourceTypeId1);
            Assert.Null(row.ReferenceResourceVersion1);
            Assert.Null(row.SystemId2);
            Assert.Equal("test-code", row.Code2);
            Assert.Null(row.CodeOverflow2);
        }

        private static ReferenceSearchParamListRowGenerator CreateReferenceGenerator(int schemaVersion)
        {
            (SqlServerFhirModel model, SchemaInformation schema) = CreateModel(schemaVersion);
            return new ReferenceSearchParamListRowGenerator(model, new SearchParameterToSearchValueTypeMap(), new SqlReferenceStorageValidator(schema));
        }

        private static ReferenceTokenCompositeSearchParamListRowGenerator CreateCompositeGenerator(int schemaVersion)
        {
            (SqlServerFhirModel model, SchemaInformation schema) = CreateModel(schemaVersion);
            var map = new SearchParameterToSearchValueTypeMap();
            return new ReferenceTokenCompositeSearchParamListRowGenerator(
                model,
                new ReferenceSearchParamListRowGenerator(model, map, new SqlReferenceStorageValidator(schema)),
                new TokenSearchParamListRowGenerator(model, map),
                map);
        }

        private static string CreateText(int length)
            => string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

        private static Uri CreateBaseUri(int length)
            => new Uri(BaseUriPrefix + new string('a', length - BaseUriPrefix.Length - 1) + "/");

        private static (SqlServerFhirModel Model, SchemaInformation Schema) CreateModel(int schemaVersion)
        {
            var schema = new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max) { Current = schemaVersion };
            var model = new SqlServerFhirModel(
                schema,
                Substitute.For<ISearchParameterDefinitionManager>(),
                () => Substitute.For<ISearchParameterStatusDataStore>(),
                Options.Create(new SecurityConfiguration()),
                Substitute.For<IScopeProvider<SqlConnectionWrapperFactory>>(),
                Substitute.For<IMediator>(),
                Substitute.For<ISqlRetryService>(),
                ResourceIdPolicy.Extended,
                NullLogger<SqlServerFhirModel>.Instance);

            SetModelField(model, "_highestInitializedVersion", schemaVersion);
            SetModelField(model, "_resourceTypeToId", new Dictionary<string, short> { ["Patient"] = 4 });
            SetModelField(model, "_searchParamUriToId", new Dictionary<Uri, short> { [SearchParameterNames.IdUri] = 5 });
            return (model, schema);
        }

        private static void SetModelField(SqlServerFhirModel model, string name, object value)
            => typeof(SqlServerFhirModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(model, value);
    }
}
