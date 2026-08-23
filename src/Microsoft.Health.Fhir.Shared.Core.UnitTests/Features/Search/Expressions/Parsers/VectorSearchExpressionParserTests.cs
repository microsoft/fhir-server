// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SearchModifierCode = Microsoft.Health.Fhir.ValueSets.SearchModifierCode;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions.Parsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class VectorSearchExpressionParserTests
    {
        private const string SearchParameterCode = "semantic-text";
        private static readonly Uri SearchParameterCanonical = new Uri("http://example.org/fhir/SearchParameter/semantic-text");
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly IReferenceSearchValueParser _referenceSearchValueParser = Substitute.For<IReferenceSearchValueParser>();
        private readonly IVectorSearchParameterResolver _vectorSearchParameterResolver = Substitute.For<IVectorSearchParameterResolver>();
        private readonly SearchParameterExpressionParser _parser;

        public VectorSearchExpressionParserTests()
        {
            _parser = new SearchParameterExpressionParser(_referenceSearchValueParser, _vectorSearchParameterResolver);
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build());
        }

        [Fact]
        public void GivenVectorResolverIsRegistered_WhenParserIsResolved_ThenResolverAwareConstructorIsUsed()
        {
            SearchParameterInfo searchParameter = CreateEnabledVectorSearchParameter();
            var services = new ServiceCollection();
            services.AddSingleton(_referenceSearchValueParser);
            services.AddSingleton(_vectorSearchParameterResolver);
            services.AddSingleton<ISearchParameterExpressionParser, SearchParameterExpressionParser>();
            using ServiceProvider serviceProvider = services.BuildServiceProvider();
            ISearchParameterExpressionParser parser = serviceProvider.GetRequiredService<ISearchParameterExpressionParser>();

            VectorSearchExpression expression = Assert.IsType<VectorSearchExpression>(parser.Parse(searchParameter, null, "breathing difficulty"));

            Assert.Same(searchParameter, expression.Parameter);
            _vectorSearchParameterResolver.Received(1).GetSearchParameter(SearchParameterCanonical);
        }

        [Fact]
        public void GivenStandardFhirVectorQuery_WhenParsed_ThenVectorExpressionIsCreated()
        {
            const string resourceType = "Resource";
            const string queryText = "breathing difficulty overnight";
            SearchParameterInfo searchParameter = CreateEnabledVectorSearchParameter();
            _searchParameterDefinitionManager.GetSearchParameter(resourceType, SearchParameterCode).Returns(searchParameter);
            var expressionParser = new ExpressionParser(() => _searchParameterDefinitionManager, _parser);

            VectorSearchExpression expression = Assert.IsType<VectorSearchExpression>(
                expressionParser.Parse(new[] { resourceType }, SearchParameterCode, queryText));

            Assert.Same(searchParameter, expression.Parameter);
            Assert.Equal(queryText, expression.QueryText);
        }

        [Fact]
        public void GivenReverseChainedVectorQuery_WhenParsed_ThenVectorLeafAndRelationshipArePreserved()
        {
            const string queryText = "breathing difficulty overnight";
            SearchParameterInfo vectorSearchParameter = CreateEnabledVectorSearchParameter();
            var referenceSearchParameter = new SearchParameterInfo(
                name: "subject",
                code: "subject",
                searchParamType: SearchParamType.Reference,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
                targetResourceTypes: new[] { KnownResourceTypes.Patient });
            _searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Observation, "subject").Returns(referenceSearchParameter);
            _searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Observation, SearchParameterCode).Returns(vectorSearchParameter);
            var expressionParser = new ExpressionParser(() => _searchParameterDefinitionManager, _parser);

            ChainedExpression expression = Assert.IsType<ChainedExpression>(expressionParser.Parse(
                new[] { KnownResourceTypes.Patient },
                "_has:Observation:subject:semantic-text",
                queryText));

            Assert.True(expression.Reversed);
            Assert.Equal(new[] { KnownResourceTypes.Observation }, expression.ResourceTypes);
            Assert.Equal(new[] { KnownResourceTypes.Patient }, expression.TargetResourceTypes);
            Assert.Same(referenceSearchParameter, expression.ReferenceSearchParameter);
            VectorSearchExpression vectorExpression = Assert.IsType<VectorSearchExpression>(expression.Expression);
            Assert.Same(vectorSearchParameter, vectorExpression.Parameter);
            Assert.Equal(queryText, vectorExpression.QueryText);
        }

        [Fact]
        public void GivenForwardChainedVectorQuery_WhenParsed_ThenVectorLeafAndRelationshipArePreserved()
        {
            const string queryText = "mobility concerns";
            SearchParameterInfo vectorSearchParameter = CreateEnabledVectorSearchParameter();
            var referenceSearchParameter = new SearchParameterInfo(
                name: "subject",
                code: "subject",
                searchParamType: SearchParamType.Reference,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
                targetResourceTypes: new[] { KnownResourceTypes.Patient });
            _searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Observation, "subject").Returns(referenceSearchParameter);
            _searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Patient, SearchParameterCode).Returns(vectorSearchParameter);
            var expressionParser = new ExpressionParser(() => _searchParameterDefinitionManager, _parser);

            ChainedExpression expression = Assert.IsType<ChainedExpression>(expressionParser.Parse(
                new[] { KnownResourceTypes.Observation },
                "subject:Patient.semantic-text",
                queryText));

            Assert.False(expression.Reversed);
            Assert.Equal(new[] { KnownResourceTypes.Observation }, expression.ResourceTypes);
            Assert.Equal(new[] { KnownResourceTypes.Patient }, expression.TargetResourceTypes);
            Assert.Same(referenceSearchParameter, expression.ReferenceSearchParameter);
            VectorSearchExpression vectorExpression = Assert.IsType<VectorSearchExpression>(expression.Expression);
            Assert.Same(vectorSearchParameter, vectorExpression.Parameter);
            Assert.Equal(queryText, vectorExpression.QueryText);
        }

        [Fact]
        public void GivenVectorSearchParameter_WhenParsed_ThenVectorExpressionIsCreated()
        {
            const string queryText = "breathing difficulty overnight";
            SearchParameterInfo searchParameter = CreateEnabledVectorSearchParameter();

            VectorSearchExpression expression = Assert.IsType<VectorSearchExpression>(_parser.Parse(searchParameter, null, queryText));

            Assert.Same(searchParameter, expression.Parameter);
            Assert.Equal(queryText, expression.QueryText);
        }

        [Fact]
        public void GivenVectorQueryContainingComma_WhenParsed_ThenQueryRemainsSingleValue()
        {
            const string queryText = "asthma, overnight changes";

            VectorSearchExpression expression = Assert.IsType<VectorSearchExpression>(_parser.Parse(CreateEnabledVectorSearchParameter(), null, queryText));

            Assert.Equal(queryText, expression.QueryText);
        }

        [Theory]
        [InlineData(SearchModifierCode.Contains)]
        [InlineData(SearchModifierCode.Exact)]
        [InlineData(SearchModifierCode.Missing)]
        public void GivenVectorSearchParameterWithModifier_WhenParsed_ThenInvalidSearchOperationExceptionIsThrown(SearchModifierCode modifierCode)
        {
            var modifier = new SearchModifier(modifierCode);

            Assert.Throws<InvalidSearchOperationException>(() => _parser.Parse(CreateVectorSearchParameter(), modifier, "breathing difficulty"));
        }

        [Fact]
        public void GivenVectorExpression_WhenRendered_ThenQueryTextIsNotExposed()
        {
            const string queryText = "sensitive clinical text";
            VectorSearchExpression expression = Assert.IsType<VectorSearchExpression>(_parser.Parse(CreateEnabledVectorSearchParameter(), null, queryText));

            Assert.DoesNotContain(queryText, expression.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void GivenVectorSearchIsNotRegistered_WhenParsed_ThenSearchParameterNotSupportedExceptionIsThrown()
        {
            var parser = new SearchParameterExpressionParser(_referenceSearchValueParser);

            Assert.Throws<SearchParameterNotSupportedException>(() => parser.Parse(CreateVectorSearchParameter(), null, "breathing difficulty"));
        }

        [Fact]
        public void GivenVectorSearchParameterIsNotEnabled_WhenParsed_ThenSearchParameterNotSupportedExceptionIsThrown()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter();
            _vectorSearchParameterResolver.GetSearchParameter(searchParameter.Url)
                .Returns(_ => throw new SearchParameterNotSupportedException(searchParameter.Url));

            Assert.Throws<SearchParameterNotSupportedException>(() => _parser.Parse(searchParameter, null, "breathing difficulty"));
        }

        private SearchParameterInfo CreateEnabledVectorSearchParameter()
        {
            SearchParameterInfo searchParameter = CreateVectorSearchParameter();
            _vectorSearchParameterResolver.GetSearchParameter(searchParameter.Url).Returns(searchParameter);
            return searchParameter;
        }

        private static SearchParameterInfo CreateVectorSearchParameter()
        {
            return new SearchParameterInfo(
                SearchParameterCode,
                SearchParameterCode,
                SearchParamType.Special,
                SearchParameterCanonical,
                expression: "Resource.text.div",
                baseResourceTypes: new[] { "Resource" },
                vectorConfig: new VectorSearchParameterConfig(),
                definitionStatus: "active");
        }
    }
}
