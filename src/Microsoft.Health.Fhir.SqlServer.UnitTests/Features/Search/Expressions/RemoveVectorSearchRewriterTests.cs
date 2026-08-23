// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class RemoveVectorSearchRewriterTests
    {
        public RemoveVectorSearchRewriterTests()
        {
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build());
        }

        [Fact]
        public void GivenVectorAndStructuredExpressions_WhenVisited_ThenOnlyVectorExpressionIsRemoved()
        {
            var searchParameter = new SearchParameterInfo(
                name: "SemanticText",
                code: "semantic-text",
                searchParamType: SearchParamType.Special,
                url: new Uri("https://example.org/fhir/SearchParameter/semantic-text"));
            var vectorExpression = new VectorSearchExpression(searchParameter, "query text");
            BinaryExpression structuredExpression = Expression.Equals(FieldName.Number, null, 1);
            Expression structuredConjunction = Expression.And(structuredExpression, structuredExpression);

            Assert.Null(vectorExpression.AcceptVisitor(RemoveVectorSearchRewriter.Instance));
            Assert.Same(structuredExpression, structuredExpression.AcceptVisitor(RemoveVectorSearchRewriter.Instance));
            Assert.Same(structuredExpression, Expression.And(vectorExpression, structuredExpression).AcceptVisitor(RemoveVectorSearchRewriter.Instance));
            Assert.Same(structuredExpression, Expression.And(structuredExpression, vectorExpression).AcceptVisitor(RemoveVectorSearchRewriter.Instance));
            Assert.Equal(
                structuredConjunction.ToString(),
                Expression.And(structuredExpression, vectorExpression, structuredExpression).AcceptVisitor(RemoveVectorSearchRewriter.Instance).ToString());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void GivenSemanticOnlyChain_WhenVisited_ThenEntireChainIsRemoved(bool reversed)
        {
            var referenceSearchParameter = new SearchParameterInfo(
                name: "subject",
                code: "subject",
                searchParamType: SearchParamType.Reference,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
                targetResourceTypes: new[] { KnownResourceTypes.Patient });
            var vectorSearchParameter = new SearchParameterInfo(
                name: "SemanticText",
                code: "semantic-text",
                searchParamType: SearchParamType.Special,
                url: new Uri("https://example.org/fhir/SearchParameter/observation-semantic"));
            var expression = new ChainedExpression(
                new[] { KnownResourceTypes.Observation },
                referenceSearchParameter,
                new[] { KnownResourceTypes.Patient },
                reversed,
                new VectorSearchExpression(vectorSearchParameter, "breathing difficulty"));

            Assert.Null(expression.AcceptVisitor(RemoveVectorSearchRewriter.Instance));
        }
    }
}
