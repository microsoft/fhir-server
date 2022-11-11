// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;
using SearchParamType = Hl7.Fhir.Model.SearchParamType;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions.Parsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ExpressionParserTests
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ISearchParameterExpressionParser _searchParameterExpressionParser = Substitute.For<ISearchParameterExpressionParser>();
        private RequestContextAccessor<IFhirRequestContext> _contextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();

        private readonly ExpressionParser _expressionParser;

        public ExpressionParserTests()
        {
            _expressionParser = new ExpressionParser(
                () => _searchParameterDefinitionManager,
                _searchParameterExpressionParser,
                _contextAccessor);
        }

        [Fact]
        public void GivenAChainedParameterPointingToASingleResourceType_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType targetResourceType = ResourceType.Organization;

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SearchParameterInfo referenceSearchParameter = SetupReferenceSearchParameter(
                sourceResourceType,
                param1,
                targetResourceType);

            SearchParameterInfo searchParameter = SetupSearchParameter(targetResourceType, param2);

            Expression expectedExpression = SetupExpression(searchParameter, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(new[] { sourceResourceType.ToString() }, key, value);

            ValidateChainedExpression(
                expression,
                sourceResourceType,
                referenceSearchParameter,
                targetResourceType.ToString(),
                actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression));
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypes_WhenParsed_Throws()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType[] targetResourceTypes = new[] { ResourceType.Organization, ResourceType.Practitioner };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceTypes);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(new[] { sourceResourceType.ToString() }, key, value));
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypesAndWithResourceTypeSpecified_WhenParsed_ThenOnlyExpressionForTheSpecifiedResourceTypeShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;

            // The reference will support both Organization and Practitioner,
            // but we will limit the search to Organization only in the key below.
            ResourceType[] targetResourceTypes = new[] { ResourceType.Organization, ResourceType.Practitioner };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}:Organization.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SearchParameterInfo referenceSearchParameter = SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceTypes);

            Expression[] expectedExpressions = targetResourceTypes.Select(targetResourceType =>
                {
                    SearchParameterInfo searchParameter = SetupSearchParameter(targetResourceType, param2);

                    return SetupExpression(searchParameter, value);
                })
                .ToArray();

            // Parse the expression.
            Expression expression = _expressionParser.Parse(new[] { sourceResourceType.ToString() }, key, value);

            ValidateChainedExpression(
                expression,
                sourceResourceType,
                referenceSearchParameter,
                ResourceType.Organization.ToString(),
                actualSearchExpression => Assert.Equal(expectedExpressions[0], actualSearchExpression));
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypesAndSearchParamIsNotSupportedByAllTargetResourceTypes_WhenParsed_ThenOnlyExpressionsForResourceTypeThatSupportsSearchParamShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;

            // The reference will support both Organization and Practitioner,
            // but the search value will only be supported by Practitioner.
            ResourceType[] targetResourceTypes = new[] { ResourceType.Organization, ResourceType.Practitioner };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Lewis";

            // Setup the search parameters.
            SearchParameterInfo referenceSearchParameter = SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceTypes);

            // Setup the Organization to not support this search param.
            _searchParameterDefinitionManager.GetSearchParameter(ResourceType.Organization.ToString(), param2)
                .Returns(x => throw new SearchParameterNotSupportedException(x.ArgAt<string>(0), x.ArgAt<string>(1)));

            // Setup the Practitioner to support this search param.
            SearchParameterInfo searchParameter = SetupSearchParameter(ResourceType.Practitioner, param2);

            Expression expectedExpression = SetupExpression(searchParameter, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(new[] { sourceResourceType.ToString() }, key, value);

            ValidateChainedExpression(
                expression,
                sourceResourceType,
                referenceSearchParameter,
                ResourceType.Practitioner.ToString(),
                actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression));
        }

        [Fact]
        public void GivenANestedChainedParameter_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType firstTargetResourceType = ResourceType.Organization;
            ResourceType secondTargetResourceType = ResourceType.Practitioner;

            string param1 = "ref1";
            string param2 = "ref2";
            string param3 = "param";

            string key = $"{param1}.{param2}.{param3}";
            string value = "Microsoft";

            // Setup the search parameters.
            SearchParameterInfo referenceSearchParameter1 = SetupReferenceSearchParameter(sourceResourceType, param1, firstTargetResourceType);
            SearchParameterInfo referenceSearchParameter2 = SetupReferenceSearchParameter(firstTargetResourceType, param2, secondTargetResourceType);

            SearchParameterInfo searchParameter = SetupSearchParameter(secondTargetResourceType, param3);

            Expression expectedExpression = SetupExpression(searchParameter, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(new[] { sourceResourceType.ToString() }, key, value);

            ValidateChainedExpression(
                expression,
                sourceResourceType,
                referenceSearchParameter1,
                firstTargetResourceType.ToString(),
                nestedExpression => ValidateChainedExpression(
                    nestedExpression,
                    firstTargetResourceType,
                    referenceSearchParameter2,
                    secondTargetResourceType.ToString(),
                    actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression)));
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

            SearchParameterInfo searchParameter = SetupSearchParameter(resourceType, param1);

            Expression expression = Substitute.For<Expression>();

            _searchParameterExpressionParser.Parse(searchParameter, new SearchModifier(SearchModifierCode.Missing), value).Returns(expression);

            // Parse the expression.
            Expression actualExpression = _expressionParser.Parse(new[] { resourceType.ToString() }, key, value);

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
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(new[] { sourceResourceType.ToString() }, key, value));
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
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(new[] { sourceResourceType.ToString() }, key, "Error"));
        }

        [Fact]
        public void GivenATargetResourceTypeThatIsNotSupported_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType sourceResourceType = ResourceType.Patient;
            ResourceType targetResourceType = ResourceType.Organization;

            string param1 = "ref";
            string param2 = "param";

            // Practitioner is a valid resource type but is not supported by the search parameter.
            string key = $"{param1}:Practitioner.{param2}";

            SetupReferenceSearchParameter(sourceResourceType, param1, targetResourceType);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(new[] { sourceResourceType.ToString() }, key, "Error"));
        }

        [Fact]
        public void GivenMultipleModifierSeparators_WhenParsing_ThenExceptionShouldBeThrown()
        {
            ResourceType resourceType = ResourceType.Patient;

            SetupSearchParameter(resourceType, "param1");

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(new[] { resourceType.ToString() }, "param1:param2:param3", "Error"));
        }

        [Fact]
        public void GivenAnInvalidParameterName_WhenParsing_ThenSearchParaemterNotSupportedExceptionShouldBeThrown()
        {
            ResourceType resourceType = ResourceType.Location;
            string invalidParameterName = "...";

            Assert.Throws<SearchParameterNotSupportedException>(() => _expressionParser.Parse(new[] { resourceType.ToString() }, invalidParameterName, "value"));
        }

        private SearchParameterInfo SetupSearchParameter(ResourceType resourceType, string paramName)
        {
            SearchParameterInfo searchParameter = new SearchParameter
            {
                Name = paramName,
                Code = paramName,
                Type = SearchParamType.String,
                Url = $"http://testparameter/{resourceType}-{paramName}",
            }.ToInfo();

            _searchParameterDefinitionManager.GetSearchParameter(resourceType.ToString(), paramName).Returns(searchParameter);

            return searchParameter;
        }

        private SearchParameterInfo SetupReferenceSearchParameter(ResourceType resourceType, string paramName, params ResourceType[] targetResourceTypes)
        {
            SearchParameterInfo referenceSearchParam = new SearchParameter
            {
                Name = paramName,
                Code = paramName,
                Type = SearchParamType.Reference,
                Target = targetResourceTypes.Cast<ResourceType?>(),
            }.ToInfo();

            _searchParameterDefinitionManager.GetSearchParameter(resourceType.ToString(), paramName).Returns(
                referenceSearchParam);

            return referenceSearchParam;
        }

        private Expression SetupExpression(SearchParameterInfo searchParameter, string value)
        {
            Expression expectedExpression = Substitute.For<Expression>();

            _searchParameterExpressionParser.Parse(searchParameter, null, value).Returns(expectedExpression);

            return expectedExpression;
        }
    }
}
