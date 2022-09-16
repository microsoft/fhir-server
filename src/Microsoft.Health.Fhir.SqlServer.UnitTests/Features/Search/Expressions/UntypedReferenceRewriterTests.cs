// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.Category, Categories.Search)]
    public class UntypedReferenceRewriterTests
    {
        private static readonly SearchParameterInfo ReferenceSearchParameterWithOneTargetType = new SearchParameterInfo("p", "p", SearchParamType.Reference, targetResourceTypes: new[] { "Organization" });

        private static readonly SearchParameterInfo ReferenceSearchParameterWithTwoTargetTypes = new SearchParameterInfo("p2", "p2", SearchParamType.Reference, targetResourceTypes: new[] { "Patient", "Practitioner" });

        private static readonly SearchParameterInfo CompositeParameter = new SearchParameterInfo(
            "c",
            "c",
            SearchParamType.Composite,
            components: new[] { new SearchParameterComponentInfo { ResolvedSearchParameter = ReferenceSearchParameterWithTwoTargetTypes }, new SearchParameterComponentInfo() { ResolvedSearchParameter = ReferenceSearchParameterWithOneTargetType }, new SearchParameterComponentInfo() { ResolvedSearchParameter = new SearchParameterInfo("number", "number", SearchParamType.Number) } });

        [Fact]
        public void GivenAnUntypedReferenceExpressionWithOneTargetType_WhenRewritten_ExpressionIncludesType()
        {
            SearchParameterExpression inputExpression = Expression.SearchParameter(
                ReferenceSearchParameterWithOneTargetType,
                Expression.StringEquals(FieldName.ReferenceResourceId, null, "myId", false));

            Expression outputExpression = inputExpression.AcceptVisitor(UntypedReferenceRewriter.Instance);

            Assert.Equal("(Param p (And (StringEquals ReferenceResourceId 'myId') (StringEquals ReferenceResourceType 'Organization')))", outputExpression.ToString());
        }

        [Fact]
        public void GivenAnUntypedReferenceExpressionWithOneTargetTypeWithMultipleOrs_WhenRewritten_ExpressionIncludesType()
        {
            SearchParameterExpression inputExpression = Expression.SearchParameter(
                ReferenceSearchParameterWithOneTargetType,
                Expression.Or(
                    Expression.StringEquals(FieldName.ReferenceResourceId, null, "myId1", false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, null, "myId2", false)));

            Expression outputExpression = inputExpression.AcceptVisitor(UntypedReferenceRewriter.Instance);

            Assert.Equal(
                "(Param p (Or (And (StringEquals ReferenceResourceId 'myId1') (StringEquals ReferenceResourceType 'Organization')) (And (StringEquals ReferenceResourceId 'myId2') (StringEquals ReferenceResourceType 'Organization'))))",
                outputExpression.ToString());
        }

        [Fact]
        public void GivenATypedReferenceExpressionWithOneTargetType_WhenRewritten_DoesNotChange()
        {
            SearchParameterExpression inputExpression = Expression.SearchParameter(
                ReferenceSearchParameterWithOneTargetType,
                Expression.And(
                    Expression.StringEquals(FieldName.ReferenceResourceType, null, "Organization", false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, null, "myId", false)));

            Expression outputExpression = inputExpression.AcceptVisitor(UntypedReferenceRewriter.Instance);

            Assert.Same(inputExpression, outputExpression);
        }

        [Fact]
        public void GivenAnUntypedReferenceExpressionWithMultipleTargetTypes_WhenRewritten_DoesNotChange()
        {
            SearchParameterExpression inputExpression = Expression.SearchParameter(
                ReferenceSearchParameterWithTwoTargetTypes,
                Expression.StringEquals(FieldName.ReferenceResourceId, null, "patientId", false));

            Expression outputExpression = inputExpression.AcceptVisitor(UntypedReferenceRewriter.Instance);

            Assert.Same(inputExpression, outputExpression);
        }

        [Fact]
        public void GivenAnUntypedReferenceExpressionWithOneTargetTypeInACompositeSearchParameter_WhenRewritten_ExpressionIncludesType()
        {
            SearchParameterExpression inputExpression = Expression.SearchParameter(
                CompositeParameter,
                Expression.And(
                    Expression.StringEquals(FieldName.ReferenceResourceId, 0, "patientId", false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, 1, "orgId", false),
                    Expression.Equals(FieldName.Number, 2, 8)));

            Expression outputExpression = inputExpression.AcceptVisitor(UntypedReferenceRewriter.Instance);

            Assert.Equal("(Param c (And (StringEquals [0].ReferenceResourceId 'patientId') (StringEquals [1].ReferenceResourceId 'orgId') (FieldEqual [2].Number 8) (StringEquals [1].ReferenceResourceType 'Organization')))", outputExpression.ToString());
        }

        [Fact]
        public void GivenATypedReferenceExpressionWithOneTargetTypeInACompositeSearchParameter_WhenRewritten_DoesNotChange()
        {
            SearchParameterExpression inputExpression = Expression.SearchParameter(
                CompositeParameter,
                Expression.And(
                    Expression.StringEquals(FieldName.ReferenceResourceId, 0, "patientId", false),
                    Expression.StringEquals(FieldName.ReferenceResourceType, 1, "Organization", false),
                    Expression.StringEquals(FieldName.ReferenceResourceId, 1, "orgId", false),
                    Expression.Equals(FieldName.Number, 2, 8)));

            Expression outputExpression = inputExpression.AcceptVisitor(UntypedReferenceRewriter.Instance);

            Assert.Same(inputExpression, outputExpression);
        }

        [Fact]
        public void GivenCompositeSearchParameterWithTypedAndUntypedReferencesORedTogether_WhenRewritten_ExpressionIncludesType()
        {
            SearchParameterExpression inputExpression = Expression.SearchParameter(
                CompositeParameter,
                Expression.Or(
                    Expression.And(
                        Expression.StringEquals(FieldName.ReferenceResourceId, 0, "patientId", false),
                        Expression.StringEquals(FieldName.ReferenceResourceId, 1, "orgId1", false),
                        Expression.Equals(FieldName.Number, 2, 8)),
                    Expression.And(
                        Expression.StringEquals(FieldName.ReferenceResourceId, 0, "patientId", false),
                        Expression.StringEquals(FieldName.ReferenceResourceId, 1, "orgId2", false),
                        Expression.Equals(FieldName.Number, 2, 8)),
                    Expression.And(
                        Expression.StringEquals(FieldName.ReferenceResourceId, 0, "patientId", false),
                        Expression.StringEquals(FieldName.ReferenceResourceId, 1, "orgId3", false),
                        Expression.Equals(FieldName.Number, 2, 8))));

            Expression outputExpression = inputExpression.AcceptVisitor(UntypedReferenceRewriter.Instance);

            Assert.Equal("(Param c (Or (And (StringEquals [0].ReferenceResourceId 'patientId') (StringEquals [1].ReferenceResourceId 'orgId1') (FieldEqual [2].Number 8) (StringEquals [1].ReferenceResourceType 'Organization')) (And (StringEquals [0].ReferenceResourceId 'patientId') (StringEquals [1].ReferenceResourceId 'orgId2') (FieldEqual [2].Number 8) (StringEquals [1].ReferenceResourceType 'Organization')) (And (StringEquals [0].ReferenceResourceId 'patientId') (StringEquals [1].ReferenceResourceId 'orgId3') (FieldEqual [2].Number 8) (StringEquals [1].ReferenceResourceType 'Organization'))))", outputExpression.ToString());
        }
    }
}
