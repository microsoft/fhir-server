// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
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
    /// Regressions for the resource id truncation performed by <see cref="ReferenceSearchParamListRowGenerator"/>.
    /// The generator must truncate to the configured maximum resource id length, not to the schema default.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSearchParamListRowGeneratorTests
    {
        [Theory]
        [InlineData(false, 64)]
        [InlineData(true, 100)]
        public void GivenAReferenceWithA100CharacterId_WhenGeneratingARow_ThenTheIdIsTruncatedToTheSelectedLength(bool useLongResourceIds, int expectedLength)
        {
            // Arrange
            ReferenceSearchParamListRowGenerator generator = CreateGenerator(ResourceIdPolicy.From(useLongResourceIds));
            string resourceId = new string('a', 100);
            var searchValue = new ReferenceSearchValue(ReferenceKind.InternalOrExternal, baseUri: null, resourceType: null, resourceId: resourceId);

            // Act
            bool generated = generator.TryGenerateRow(1, 2L, 3, searchValue, results: null, out ReferenceSearchParamListRow row);

            // Assert
            Assert.True(generated);
            Assert.Equal(resourceId[..expectedLength], row.ReferenceResourceId);
        }

        private static ReferenceSearchParamListRowGenerator CreateGenerator(ResourceIdPolicy policy)
        {
            var model = new SqlServerFhirModel(
                new SchemaInformation(SchemaVersionConstants.Min, SchemaVersionConstants.Max),
                Substitute.For<ISearchParameterDefinitionManager>(),
                () => Substitute.For<ISearchParameterStatusDataStore>(),
                Options.Create(new SecurityConfiguration()),
                Substitute.For<IScopeProvider<SqlConnectionWrapperFactory>>(),
                Substitute.For<IMediator>(),
                Substitute.For<ISqlRetryService>(),
                policy,
                NullLogger<SqlServerFhirModel>.Instance);

            return new ReferenceSearchParamListRowGenerator(
                model,
                new SearchParameterToSearchValueTypeMap());
        }
    }
}
