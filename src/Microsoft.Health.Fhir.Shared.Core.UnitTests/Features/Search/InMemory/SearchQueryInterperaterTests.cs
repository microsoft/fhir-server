// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.InMemory;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.InMemory
{
    public class SearchQueryInterperaterTests : IAsyncLifetime
    {
        private ExpressionParser _expressionParser;
        private InMemoryIndex _memoryIndex;
        private SearchQueryInterpreter _searchQueryInterperater;

        public Task DisposeAsync()
        {
            return Task.CompletedTask;
        }

        public async Task InitializeAsync()
        {
            _searchQueryInterperater = new SearchQueryInterpreter();

            var fixture = new SearchParameterFixtureData();
            var manager = await fixture.GetSearchDefinitionManagerAsync();

            var fhirRequestContextAccessor = new FhirRequestContextAccessor();
            var supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(manager);
            var searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(manager, fhirRequestContextAccessor);
            var typedElementToSearchValueConverterManager = GetTypeConverterAsync().Result;

            var referenceParser = new ReferenceSearchValueParser(fhirRequestContextAccessor);
            var referenceToElementResolver = new LightweightReferenceToElementResolver(referenceParser, ModelInfoProvider.Instance);
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var searchIndexer = new TypedElementSearchIndexer(supportedSearchParameterDefinitionManager, typedElementToSearchValueConverterManager, referenceToElementResolver, modelInfoProvider, logger);
            _expressionParser = new ExpressionParser(() => searchableSearchParameterDefinitionManager, new SearchParameterExpressionParser(referenceParser));
            _memoryIndex = new InMemoryIndex(searchIndexer);

            _memoryIndex.IndexResources(Samples.GetDefaultPatient(), Samples.GetDefaultObservation().UpdateId("example"));
        }

        protected async Task<ITypedElementToSearchValueConverterManager> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            return fhirTypedElementToSearchValueConverterManager;
        }

        [Fact]
        public void GivenASearchQueryInterpreter_WhenSearchingByNameOnPatient_ThenCorrectResultsAreReturned()
        {
            var expression = _expressionParser.Parse(new[] { "Patient" }, "name", "Jim");

            var evaluator = expression.AcceptVisitor(_searchQueryInterperater, default);

            var results = evaluator
                .Invoke(_memoryIndex.Index.Values.SelectMany(x => x))
                .ToArray();

            Assert.Single(results);
        }

        [Fact]
        public void GivenASearchQueryInterpreter_WhenSearchingByDobOnPatient_ThenCorrectResultsAreReturned()
        {
            var expression = _expressionParser.Parse(new[] { "Patient" }, "birthdate", "gt1950");

            var evaluator = expression.AcceptVisitor(_searchQueryInterperater, default);

            var results = evaluator
                .Invoke(_memoryIndex.Index.Values.SelectMany(x => x))
                .ToArray();

            Assert.Single(results);
        }

        [Fact]
        public void GivenASearchQueryInterpreter_WhenSearchingByDobOnPatientWithRange_ThenCorrectResultsAreReturned()
        {
            var expression = _expressionParser.Parse(new[] { "Patient" }, "birthdate", "1974");

            var evaluator = expression.AcceptVisitor(_searchQueryInterperater, default);

            var results = evaluator
                .Invoke(_memoryIndex.Index.Values.SelectMany(x => x))
                .ToArray();

            Assert.Single(results);
        }

        [Fact]
        public void GivenASearchQueryInterpreter_WhenSearchingByValueOnObservation_ThenCorrectResultsAreReturned()
        {
            var expression = _expressionParser.Parse(new[] { "Observation" }, "value-quantity", "lt70");

            var evaluator = expression.AcceptVisitor(_searchQueryInterperater, default);

            var results = evaluator
                .Invoke(_memoryIndex.Index.Values.SelectMany(x => x))
                .ToArray();

            Assert.Single(results);
        }
    }
}
