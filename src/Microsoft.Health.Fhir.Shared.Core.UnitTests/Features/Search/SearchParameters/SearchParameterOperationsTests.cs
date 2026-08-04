// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Parameters
{
    /// <summary>
    /// Covers the interaction between <see cref="SearchParameterOperations"/> and
    /// <see cref="SearchParameterStatusManager"/> when a SearchParameter resource is deleted after its registry entry
    /// has already been removed. Both types are real here on purpose: the defect this guards against was a named
    /// argument in the call between them that silently dropped the ignoreSearchParameterNotSupportedException flag.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.SearchParameterStatus)]
    public class SearchParameterOperationsTests
    {
        private const string MissingUrl = "http://hl7.org/fhir/us/core/SearchParameter/us-core-patient-gender-identity";

        private readonly ISearchParameterStatusDataStore _searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly SearchParameterOperations _searchParameterOperations;

        public SearchParameterOperationsTests()
        {
            // The url reached the Deleted status, which is what makes SearchParameterOperations remove it from the
            // definition manager while the SearchParameter resource itself is still in the data store.
            _searchParameterStatusDataStore
                .GetSearchParameterStatuses(Arg.Any<CancellationToken>(), Arg.Any<DateTimeOffset?>())
                .Returns(new[]
                {
                    new ResourceSearchParameterStatus
                    {
                        Status = SearchParameterStatus.Deleted,
                        Uri = new Uri(MissingUrl),
                        LastUpdated = DateTimeOffset.UtcNow,
                    },
                });

            _searchParameterDefinitionManager
                .GetSearchParameter(MissingUrl)
                .Returns(x => throw new SearchParameterNotSupportedException(new Uri(MissingUrl)));

            // No active reindex job. default gives (found: false, id: null) without an explicit cast on null.
            (bool Found, string Id) noActiveReindexJob = default;

            _fhirOperationDataStore
                .CheckActiveReindexJobsAsync(Arg.Any<CancellationToken>())
                .Returns(noActiveReindexJob);

            var statusManager = new SearchParameterStatusManager(
                _searchParameterStatusDataStore,
                _searchParameterDefinitionManager,
                Substitute.For<ISearchParameterSupportResolver>(),
                Substitute.For<IMediator>(),
                NullLogger<SearchParameterStatusManager>.Instance);

            _searchParameterOperations = new SearchParameterOperations(
                statusManager,
                _searchParameterDefinitionManager,
                ModelInfoProvider.Instance,
                Substitute.For<ISearchParameterSupportResolver>(),
                Substitute.For<IDataStoreSearchParameterValidator>(),
                () => _fhirOperationDataStore.CreateMockScope(),
                () => Substitute.For<ISearchService>().CreateMockScope(),
                Substitute.For<IFhirDataStore>().CreateMockScopeProvider(),
                Substitute.For<IResourceWrapperFactory>(),
                NullLogger<SearchParameterOperations>.Instance);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task GivenASearchParameterMissingFromTheRegistry_WhenDeletingItWithTheIgnoreFlag_ThenNoExceptionIsThrown(bool isHardDelete)
        {
            await _searchParameterOperations.DeleteSearchParameterAsync(
                CreateSearchParameterRawResource(),
                CancellationToken.None,
                ignoreSearchParameterNotSupportedException: true,
                isHardDelete: isHardDelete);

            // The registry does not know the url, so no status row is written for it.
            await _searchParameterStatusDataStore
                .DidNotReceive()
                .UpsertStatuses(Arg.Any<IReadOnlyList<ResourceSearchParameterStatus>>(), Arg.Any<CancellationToken>(), Arg.Any<long?>());
        }

        [Fact]
        public async Task GivenASearchParameterMissingFromTheRegistry_WhenDeletingItWithoutTheIgnoreFlag_ThenSearchParameterNotSupportedExceptionIsThrown()
        {
            await Assert.ThrowsAsync<SearchParameterNotSupportedException>(
                () => _searchParameterOperations.DeleteSearchParameterAsync(
                    CreateSearchParameterRawResource(),
                    CancellationToken.None,
                    ignoreSearchParameterNotSupportedException: false,
                    isHardDelete: true));
        }

        private static RawResource CreateSearchParameterRawResource()
        {
            return new RawResource(
                $"{{\"resourceType\":\"SearchParameter\",\"id\":\"us-core-patient-gender-identity\",\"url\":\"{MissingUrl}\"}}",
                FhirResourceFormat.Json,
                isMetaSet: false);
        }
    }
}
