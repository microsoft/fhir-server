// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions.Parsers
{
    public class ExpressionParserTests
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ISearchParameterExpressionParser _searchParameterExpressionParser = Substitute.For<ISearchParameterExpressionParser>();

        private readonly ExpressionParser _expressionParser;

        public ExpressionParserTests()
        {
            _expressionParser = new ExpressionParser(
                _searchParameterDefinitionManager,
                _searchParameterExpressionParser);
        }

        [Fact]
        public void GivenAModifier_WhenParsed_ThenExceptionShouldBeThrown()
        {
            ResourceType resourceType = ResourceType.Patient;

            string param1 = "ref";
            string modifier = "missing";

            // Practitioner is a valid resource type but is not supported by the search parameter.
            string key = $"{param1}:{modifier}";
            string value = "Seattle";

            SearchParameter searchParameter = SetupSearchParameter(resourceType, param1);

            Expression expression = Substitute.For<Expression>();

            _searchParameterExpressionParser.Parse(searchParameter, SearchParameter.SearchModifierCode.Missing, value).Returns(expression);

            // Parse the expression.
            Expression actualExpression = _expressionParser.Parse(resourceType, key, value);

            // The mock requires the modifier to match so if we get the same expression instance
            // then it means we got the modifier correctly.
            Assert.Equal(expression, actualExpression);
        }

        [Fact]
        public void GivenAChainedParameterThatIsNotReferenceType_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType sourceResourceType = ResourceType.Patient;

            string param1 = "ref1";

            string key = $"{param1}.param";
            string value = "Microsoft";

            // Setup the search parameters.
            SetupSearchParameter(sourceResourceType, param1);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(sourceResourceType, key, value));
        }

        [Fact]
        public void GivenAnInvalidResourceTypeToScope_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType targetResourceType = ResourceType.Organization;

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}:NonExistingResourceType.{param2}";

            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceType);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(sourceResourceType, key, "Error"));
        }

        [Fact]
        public void GivenATargetResourceTypeThatIsNotSupported_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType targetResourceType = ResourceType.Organization;

            string param1 = "ref";
            string param2 = "param";

            // Practitioner is a valid resource type but is not supported by the search paramter.
            string key = $"{param1}:Practitioner.{param2}";

            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceType);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(sourceResourceType, key, "Error"));
        }

        [Fact]
        public void GivenMultipleModifierSeparators_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType resourceType = ResourceType.Patient;

            SetupSearchParameter(resourceType, "param1");

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(resourceType, "param1:param2:param3", "Error"));
        }

        [Fact]
        public void GivenAnInvalidParameterName_WhenParsing_ThenSearchParaemterNotSupportedExceptionShouldBeThrown()
        {
            ResourceType resourceType = ResourceType.Location;
            string invalidParameterName = "...";

            Assert.Throws<SearchParameterNotSupportedException>(() => _expressionParser.Parse(resourceType, invalidParameterName, "value"));
        }

        private SearchParameter SetupSearchParameter(ResourceType resourceType, string paramName)
        {
            SearchParameter searchParameter = new SearchParameter()
            {
                Name = paramName,
                Type = SearchParamType.String,
            };

            _searchParameterDefinitionManager.GetSearchParameter(resourceType, paramName).Returns(searchParameter);

            return searchParameter;
        }

        private void SetupReferenceSearchParameter(ResourceType resourceType, string paramName, params ResourceType[] targetResourceTypes)
        {
            _searchParameterDefinitionManager.GetSearchParameter(resourceType, paramName).Returns(
                new SearchParameter()
                {
                    Name = paramName,
                    Type = SearchParamType.Reference,
                    Target = targetResourceTypes.Cast<ResourceType?>(),
                });
        }

        private Expression SetupExpression(SearchParameter searchParameter, string value)
        {
            Expression expectedExpression = Substitute.For<Expression>();

            _searchParameterExpressionParser.Parse(searchParameter, null, value).Returns(expectedExpression);

            return expectedExpression;
        }
    }
}
