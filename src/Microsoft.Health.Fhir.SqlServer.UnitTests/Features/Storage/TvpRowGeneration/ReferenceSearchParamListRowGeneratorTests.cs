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
    /// Regressions for reference length limits applied by <see cref="ReferenceSearchParamListRowGenerator"/>.
    /// A reference whose id or base URI exceeds its limit is not indexed when rejection is enabled.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSearchParamListRowGeneratorTests : IClassFixture<ModelInfoProviderFixture>
    {
        /// <summary>
        /// Gets input lengths and whether a row is expected for both resource id policies.
        /// </summary>
        public static TheoryData<bool, int, bool> ResourceIdLengths => new TheoryData<bool, int, bool>
        {
            { false, 63, true },
            { false, 64, true },
            { false, 65, false },
            { false, 128, false },
            { false, 129, false },
            { true, 64, true },
            { true, 65, true },
            { true, 128, true },
            { true, 129, false },
        };

        [Theory]
        [MemberData(nameof(ResourceIdLengths))]
        public void GivenAReferenceNearTheIdLimits_WhenGeneratingARow_ThenTheWholeIdIsStoredOrNoRowIsGenerated(bool useLongResourceIds, int length, bool expectedGenerated)
        {
            // Arrange
            var generator = new ReferenceSearchParamListRowGenerator(CreateModel(ResourceIdPolicy.From(useLongResourceIds)), new SearchParameterToSearchValueTypeMap());
            string resourceId = CreateResourceId(length);
            var searchValue = new ReferenceSearchValue(ReferenceKind.External, new Uri("https://external-server.com/fhir/"), "Patient", resourceId);

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, searchValue, results: null, out ReferenceSearchParamListRow row);

            // Assert
            Assert.Equal(expectedGenerated, generated);
            if (!expectedGenerated)
            {
                return;
            }

            Assert.Equal(resourceId, row.ReferenceResourceId);
            Assert.Equal(1, row.ResourceTypeId);
            Assert.Equal(2L, row.ResourceSurrogateId);
            Assert.Equal(3, row.SearchParamId);
            Assert.Equal("https://external-server.com/fhir/", row.BaseUri);
            Assert.Equal((short)4, row.ReferenceResourceTypeId);
            Assert.Null(row.ReferenceResourceVersion);
        }

        [Fact]
        public void GivenAnUntypedReferenceOverTheIdLimit_WhenGeneratingARow_ThenNoRowIsGenerated()
        {
            // Arrange
            var generator = new ReferenceSearchParamListRowGenerator(CreateModel(ResourceIdPolicy.Standard), new SearchParameterToSearchValueTypeMap());
            var searchValue = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null, resourceType: null, CreateResourceId(65));

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, searchValue, results: null, out _);

            // Assert
            Assert.False(generated);
        }

        [Theory]
        [InlineData(false, 65, 64)]
        [InlineData(true, 129, 128)]
        public void GivenRejectionIsDisabled_WhenGeneratingARowForAReferenceOverTheIdLimit_ThenTheIdIsCutOffAtTheLimit(bool useLongResourceIds, int length, int expectedLength)
        {
            // Arrange
            var generator = new ReferenceSearchParamListRowGenerator(CreateModel(ResourceIdPolicy.From(useLongResourceIds, rejectLongReferences: false)), new SearchParameterToSearchValueTypeMap());
            string resourceId = CreateResourceId(length);
            var searchValue = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null, "Patient", resourceId);

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, searchValue, results: null, out ReferenceSearchParamListRow row);

            // Assert
            Assert.True(generated);
            Assert.Equal(resourceId[..expectedLength], row.ReferenceResourceId);
        }

        [Theory]
        [MemberData(nameof(ResourceIdLengths))]
        public void GivenAReferenceTokenCompositeNearTheIdLimits_WhenGeneratingARow_ThenTheWholeIdIsStoredOrNoRowIsGenerated(bool useLongResourceIds, int length, bool expectedGenerated)
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
            Assert.Equal(expectedGenerated, generated);
            if (!expectedGenerated)
            {
                return;
            }

            Assert.Equal(resourceId, row.ReferenceResourceId1);
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

        [Theory]
        [InlineData(128, true)]
        [InlineData(129, false)]
        [InlineData(134, false)]
        public void GivenAReferenceNearTheBaseUriLimit_WhenGeneratingARow_ThenTheWholeBaseUriIsStoredOrNoRowIsGenerated(int length, bool expectedGenerated)
        {
            // Arrange
            var generator = new ReferenceSearchParamListRowGenerator(CreateModel(ResourceIdPolicy.Standard), new SearchParameterToSearchValueTypeMap());
            Uri baseUri = CreateBaseUri(length);
            Assert.Equal(length, baseUri.ToString().Length);
            var searchValue = new ReferenceSearchValue(ReferenceKind.External, baseUri, "Patient", "123");

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, searchValue, results: null, out ReferenceSearchParamListRow row);

            // Assert
            Assert.Equal(expectedGenerated, generated);
            if (expectedGenerated)
            {
                Assert.Equal(baseUri.ToString(), row.BaseUri);
            }
        }

        [Theory]
        [InlineData(129)]
        [InlineData(134)]
        public void GivenAReferenceTokenCompositeOverTheBaseUriLimit_WhenGeneratingARow_ThenNoRowIsGenerated(int length)
        {
            // Arrange
            SqlServerFhirModel model = CreateModel(ResourceIdPolicy.Standard);
            var map = new SearchParameterToSearchValueTypeMap();
            var generator = new ReferenceTokenCompositeSearchParamListRowGenerator(
                model,
                new ReferenceSearchParamListRowGenerator(model, map),
                new TokenSearchParamListRowGenerator(model, map),
                map);
            Uri baseUri = CreateBaseUri(length);
            Assert.Equal(length, baseUri.ToString().Length);
            var reference = new ReferenceSearchValue(ReferenceKind.External, baseUri, "Patient", "123");
            var token = new TokenSearchValue(system: null, code: "test-code", text: null);

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, (reference, token), results: null, out _);

            // Assert
            Assert.False(generated);
        }

        [Fact]
        public void GivenRejectionIsDisabled_WhenGeneratingARowForAReferenceOverTheBaseUriLimit_ThenTheWholeValueIsPassedThrough()
        {
            // Arrange
            var generator = new ReferenceSearchParamListRowGenerator(CreateModel(ResourceIdPolicy.From(false, rejectLongReferences: false)), new SearchParameterToSearchValueTypeMap());
            Uri baseUri = CreateBaseUri(129);
            Assert.Equal(129, baseUri.ToString().Length);
            var searchValue = new ReferenceSearchValue(ReferenceKind.External, baseUri, "Patient", "123");

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, searchValue, results: null, out ReferenceSearchParamListRow row);

            // Assert
            Assert.True(generated);
            Assert.Equal(baseUri.ToString(), row.BaseUri);
        }

        [Fact]
        public void GivenTheReferenceIndex_WhenComparingBaseUriWidths_ThenTheyMatchTheLimitAppliedToReferences()
        {
            Assert.Equal(ResourceIdPolicy.MaxReferenceBaseUriLength, VLatest.ReferenceSearchParam.BaseUri.Metadata.MaxLength);
            Assert.Equal(ResourceIdPolicy.MaxReferenceBaseUriLength, VLatest.ReferenceTokenCompositeSearchParam.BaseUri1.Metadata.MaxLength);
        }

        private static Uri CreateBaseUri(int length)
        {
            const string prefix = "https://example.org/";
            string suffix = length == 134 ? "other/" : string.Empty;
            return new Uri(prefix + new string('a', length - prefix.Length - suffix.Length - 1) + "/" + suffix);
        }

        private static string CreateResourceId(int length)
            => string.Concat(Enumerable.Range(0, length).Select(i => (char)('A' + (i % 26))));

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
    }
}
