// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Legacy.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using NSubstitute;
using Xunit;
using static Microsoft.Health.Fhir.Core.UnitTests.Features.Search.SearchExpressionTestHelper;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Legacy.Expressions.Parsers
{
    public class LegacyExpressionParserTests
    {
        private readonly IResourceTypeManifestManager _resourceTypeManifestManager = Substitute.For<IResourceTypeManifestManager>();
        private readonly ILegacySearchValueParser _searchValueParser = Substitute.For<ILegacySearchValueParser>();

        private readonly LegacyExpressionParser _expressionParser;

        public LegacyExpressionParserTests()
        {
            _expressionParser = new LegacyExpressionParser(
                _resourceTypeManifestManager,
                _searchValueParser);
        }

        [Fact]
        public void GivenAChainedParameterPointingToASingleResourceType_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            Type sourceResourceType = typeof(Patient);
            Type targetResourceType = typeof(Organization);

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SetupReferenceSearchParam(sourceResourceType, param1, targetResourceType);

            SearchParam searchParam = SetupSearchParam(targetResourceType, param2);

            Expression expectedExpression = SetupExpression(searchParam, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(sourceResourceType), key, value);

            ValidateMultiaryExpression(
                expression,
                MultiaryOperator.Or,
                chainedExpression => ValidateChainedExpression(
                    chainedExpression,
                    sourceResourceType,
                    param1,
                    targetResourceType,
                    actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression)));
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypes_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            Type sourceResourceType = typeof(Patient);
            Type[] targetResourceTypes = new[] { typeof(Organization), typeof(Practitioner) };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SetupReferenceSearchParam(sourceResourceType, param1, targetResourceTypes);

            var expectedTargets = targetResourceTypes.Select(targetResourceType =>
            {
                SearchParam searchParam = SetupSearchParam(targetResourceType, param2);

                Expression expectedExpression = SetupExpression(searchParam, value);

                return new { TargetResourceType = targetResourceType, Expression = expectedExpression };
            })
            .ToArray();

            // Parse the expression.
            Expression expression = _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(sourceResourceType), key, value);

            ValidateMultiaryExpression(
                expression,
                MultiaryOperator.Or,
                expectedTargets.Select(expected =>
                {
                    return (Action<Expression>)(chainedExpression =>
                        ValidateChainedExpression(
                            chainedExpression,
                            sourceResourceType,
                            param1,
                            expected.TargetResourceType,
                            actualSearchExpression => Assert.Equal(expected.Expression, actualSearchExpression)));
                })
                .ToArray());
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypesAndWithResourceTypeSpecified_WhenParsed_ThenOnlyExpressionForTheSpecifiedResourceTypeShouldBeCreated()
        {
            Type sourceResourceType = typeof(Patient);

            // The reference will support both Organization and Practitioner,
            // but we will limit the search to Organization only in the key below.
            Type[] targetResourceTypes = new[] { typeof(Organization), typeof(Practitioner) };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}:Organization.{param2}";
            string value = "Seattle";

            // Setup the search parameters.
            SetupReferenceSearchParam(sourceResourceType, param1, targetResourceTypes);

            Expression[] expectedExpressions = targetResourceTypes.Select(targetResourceType =>
            {
                SearchParam searchParam = SetupSearchParam(targetResourceType, param2);

                return SetupExpression(searchParam, value);
            })
            .ToArray();

            // Parse the expression.
            Expression expression = _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(sourceResourceType), key, value);

            ValidateMultiaryExpression(
                expression,
                MultiaryOperator.Or,
                chainedExpression => ValidateChainedExpression(
                    chainedExpression,
                    sourceResourceType,
                    param1,
                    typeof(Organization),
                    actualSearchExpression => Assert.Equal(expectedExpressions[0], actualSearchExpression)));
        }

        [Fact]
        public void GivenAChainedParameterPointingToMultipleResourceTypesAndSearchParamIsNotSupportedByAllTargetResourceTypes_WhenParsed_ThenOnlyExpressionsForResourceTypeThatSupportsSearchParamShouldBeCreated()
        {
            Type sourceResourceType = typeof(Patient);

            // The reference will support both Organization and Practitioner,
            // but the search value will only be supported by Practitioner.
            Type[] targetResourceTypes = new[] { typeof(Organization), typeof(Practitioner) };

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}.{param2}";
            string value = "Lewis";

            // Setup the search parameters.
            SetupReferenceSearchParam(sourceResourceType, param1, targetResourceTypes);

            // Setup the Organization to not support this search param.
            ResourceTypeManifest manifest = new ResourceTypeManifest(
                typeof(Organization),
                new[] { SetupSearchParam(typeof(Organization), "abc") });

            _resourceTypeManifestManager.GetManifest(typeof(Organization))
                .Returns(manifest);

            // Setup the Practitioner to support this search param.
            SearchParam searchParam = SetupSearchParam(typeof(Practitioner), param2);

            Expression expectedExpression = SetupExpression(searchParam, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(sourceResourceType), key, value);

            ValidateMultiaryExpression(
                expression,
                MultiaryOperator.Or,
                chainedExpression => ValidateChainedExpression(
                    chainedExpression,
                    sourceResourceType,
                    param1,
                    typeof(Practitioner),
                    actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression)));
        }

        [Fact]
        public void GivenANestedChainedParameter_WhenParsed_ThenCorrectExpressionShouldBeCreated()
        {
            Type sourceResourceType = typeof(Patient);
            Type firstTargetResourceType = typeof(Organization);
            Type secondTargetResourceType = typeof(Practitioner);

            string param1 = "ref1";
            string param2 = "ref2";
            string param3 = "param";

            string key = $"{param1}.{param2}.{param3}";
            string value = "Microsoft";

            // Setup the search parameters.
            SetupReferenceSearchParam(sourceResourceType, param1, firstTargetResourceType);
            SetupReferenceSearchParam(firstTargetResourceType, param2, secondTargetResourceType);

            SearchParam searchParam = SetupSearchParam(secondTargetResourceType, param3);

            Expression expectedExpression = SetupExpression(searchParam, value);

            // Parse the expression.
            Expression expression = _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(sourceResourceType), key, value);

            ValidateMultiaryExpression(
                 expression,
                 MultiaryOperator.Or,
                 chainedExpression => ValidateChainedExpression(
                     chainedExpression,
                     sourceResourceType,
                     param1,
                     firstTargetResourceType,
                     nestedExpression => ValidateMultiaryExpression(
                         nestedExpression,
                         MultiaryOperator.Or,
                         nestedChainedExpression => ValidateChainedExpression(
                             nestedChainedExpression,
                             firstTargetResourceType,
                             param2,
                             secondTargetResourceType,
                             actualSearchExpression => Assert.Equal(expectedExpression, actualSearchExpression)))));
        }

        [Fact]
        public void GivenAModifier_WhenParsed_ThenExceptionShouldBeThrown()
        {
            Type resourceType = typeof(Patient);

            string param1 = "ref";
            string modifier = "missing";

            // Practitioner is a valid resource type but is not supported by the search paramter.
            string key = $"{param1}:{modifier}";
            string value = "Seattle";

            SearchParam searchParam = SetupSearchParam(resourceType, param1);

            Expression expression = Substitute.For<Expression>();

            _searchValueParser.Parse(searchParam, modifier, value).Returns(expression);

            // Parse the expression.
            Expression actualExpression = _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(resourceType), key, value);

            // The mock requires the modifier to match so if we get the same expression instance
            // then it means we got the modifier correctly.
            Assert.Equal(expression, actualExpression);
        }

        [Fact]
        public void GivenAChainedParameterThatIsNotReferenceType_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Type sourceResourceType = typeof(Patient);

            string param1 = "ref1";

            string key = $"{param1}.param";
            string value = "Microsoft";

            // Setup the search parameters.
            SetupSearchParam(sourceResourceType, param1);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(sourceResourceType), key, value));
        }

        [Fact]
        public void GivenAnInvalidResourceTypeToScope_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Type sourceResourceType = typeof(Patient);
            Type targetResourceType = typeof(Organization);

            string param1 = "ref";
            string param2 = "param";

            string key = $"{param1}:NonExistingResourceType.{param2}";

            SetupReferenceSearchParam(sourceResourceType, param1, targetResourceType);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(sourceResourceType), key, "Error"));
        }

        [Fact]
        public void GivenATargetResourceTypeThatIsNotSupported_WhenParsing_ThenExceptionShouldBeThrown()
        {
            Type sourceResourceType = typeof(Patient);
            Type targetResourceType = typeof(Organization);

            string param1 = "ref";
            string param2 = "param";

            // Practitioner is a valid resource type but is not supported by the search paramter.
            string key = $"{param1}:Practitioner.{param2}";

            SetupReferenceSearchParam(sourceResourceType, param1, targetResourceType);

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(sourceResourceType), key, "Error"));
        }

        [Fact]
        public void GivenMultipleModifierSeparators_WhenParsing_ThenExceptionShouldBeThrown()
        {
            var resourceType = typeof(Patient);

            SetupSearchParam(resourceType, "param1");

            // Parse the expression.
            Assert.Throws<InvalidSearchOperationException>(() => _expressionParser.Parse(_resourceTypeManifestManager.GetManifest(resourceType), "param1:param2:param3", "Error"));
        }

        private SearchParam SetupSearchParam(Type resourceType, string paramName)
        {
            SearchParam searchParam = new SearchParam(
                resourceType,
                paramName,
                SearchParamType.String,
                StringSearchValue.Parse);

            SetupSearchParam(resourceType, searchParam);

            return searchParam;
        }

        private ReferenceSearchParam SetupReferenceSearchParam(Type resourceType, string paramName, params Type[] targetResourceTypes)
        {
            ReferenceSearchParam searchParam = new ReferenceSearchParam(
                resourceType,
                paramName,
                ReferenceSearchValue.Parse,
                targetResourceTypes);

            SetupSearchParam(resourceType, searchParam);

            return searchParam;
        }

        private void SetupSearchParam(Type resourceType, SearchParam searchParam)
        {
            ResourceTypeManifest manifest = new ResourceTypeManifest(
                resourceType,
                new[] { searchParam });

            _resourceTypeManifestManager.GetManifest(resourceType).Returns(manifest);
        }

        private Expression SetupExpression(SearchParam searchParam, string value)
        {
            Expression searchExpression = Substitute.For<Expression>();

            _searchValueParser.Parse(searchParam, null, value).Returns(searchExpression);

            return searchExpression;
        }
    }
}
