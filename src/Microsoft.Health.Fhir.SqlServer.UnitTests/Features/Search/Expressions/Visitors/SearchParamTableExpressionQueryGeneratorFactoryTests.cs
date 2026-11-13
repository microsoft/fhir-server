// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors
{
    /// <summary>
    /// Unit tests for SearchParamTableExpressionQueryGeneratorFactory.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParamTableExpressionQueryGeneratorFactoryTests
    {
        private readonly SearchParamTableExpressionQueryGeneratorFactory _factory;

        public SearchParamTableExpressionQueryGeneratorFactoryTests()
        {
            var searchParameterToSearchValueTypeMap = new SearchParameterToSearchValueTypeMap();
            _factory = new SearchParamTableExpressionQueryGeneratorFactory(searchParameterToSearchValueTypeMap);
        }

        [Fact]
        public void GivenMissingFieldExpressionWithReferenceResourceType_WhenVisited_ThenReturnsReferenceQueryGenerator()
        {
            var expression = new MissingFieldExpression(FieldName.ReferenceResourceType, null);

            var generator = _factory.VisitMissingField(expression, null);

            Assert.IsType<ReferenceQueryGenerator>(generator);
        }

        [Fact]
        public void GivenMissingFieldExpressionWithReferenceBaseUri_WhenVisited_ThenReturnsReferenceQueryGenerator()
        {
            var expression = new MissingFieldExpression(FieldName.ReferenceBaseUri, null);

            var generator = _factory.VisitMissingField(expression, null);

            Assert.IsType<ReferenceQueryGenerator>(generator);
        }

        [Fact]
        public void GivenMissingFieldExpressionWithTokenSystem_WhenVisited_ThenReturnsTokenQueryGenerator()
        {
            var expression = new MissingFieldExpression(FieldName.TokenSystem, null);

            var generator = _factory.VisitMissingField(expression, null);

            Assert.IsType<TokenQueryGenerator>(generator);
        }
    }
}
