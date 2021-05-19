// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations.Everything;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Everything
{
    public class PatientEverythingServiceTests
    {
        private readonly ISearchService _searchService = Substitute.For<ISearchService>();
        private readonly ISearchOptionsFactory _searchOptionsFactory = Substitute.For<ISearchOptionsFactory>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();

        private readonly PatientEverythingService _patientEverythingService;

        public PatientEverythingServiceTests()
        {
            _patientEverythingService = new PatientEverythingService(() => _searchService.CreateMockScope(), _searchOptionsFactory, _searchParameterDefinitionManager, _compartmentDefinitionManager);
        }

        [Fact]
        public async Task GivenInputParameters_WhenSearch_ThenCorrectResultsAreReturned()
        {
            var searchOptions = new SearchOptions();
            _searchOptionsFactory.Create(Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);
            _searchOptionsFactory.Create(ResourceType.Patient.ToString(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<Tuple<string, string>>>())
                .Returns(searchOptions);

            var searchResult = new SearchResult(Enumerable.Empty<SearchResultEntry>(), null, null, new Tuple<string, string>[0]);
            _searchService.SearchAsync(Arg.Any<SearchOptions>(), CancellationToken.None).Returns(searchResult);

            SearchResult actualResult = await _patientEverythingService.SearchAsync("123", null, null, null, null, null, CancellationToken.None);

            Assert.Equal(searchResult.ContinuationToken, actualResult.ContinuationToken);
            Assert.Equal(searchResult.Results, actualResult.Results);
        }
    }
}
