// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlChainLinkExpressionTests
    {
        private static readonly SearchParameterInfo ReferenceSearchParam = new SearchParameterInfo(
            name: "subject",
            code: "subject",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        [Fact]
        public void GivenValidParameters_WhenConstructed_ThenPropertiesAreSetCorrectly()
        {
            var resourceTypes = new[] { "Observation" };
            var targetResourceTypes = new[] { "Patient" };
            var expressionOnSource = Expression.Equals(FieldName.TokenCode, null, "code1");
            var expressionOnTarget = Expression.Equals(FieldName.TokenCode, null, "code2");

            var expression = new SqlChainLinkExpression(
                resourceTypes,
                ReferenceSearchParam,
                targetResourceTypes,
                reversed: false,
                expressionOnSource,
                expressionOnTarget);

            Assert.Same(resourceTypes, expression.ResourceTypes);
            Assert.Same(ReferenceSearchParam, expression.ReferenceSearchParameter);
            Assert.Same(targetResourceTypes, expression.TargetResourceTypes);
            Assert.False(expression.Reversed);
            Assert.Same(expressionOnSource, expression.ExpressionOnSource);
            Assert.Same(expressionOnTarget, expression.ExpressionOnTarget);
        }

        [Fact]
        public void GivenNullResourceTypes_WhenConstructed_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SqlChainLinkExpression(
                    null,
                    ReferenceSearchParam,
                    new[] { "Patient" },
                    reversed: false));
        }

        [Fact]
        public void GivenNullReferenceSearchParameter_WhenConstructed_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SqlChainLinkExpression(
                    new[] { "Observation" },
                    null,
                    new[] { "Patient" },
                    reversed: false));
        }

        [Fact]
        public void GivenNullTargetResourceTypes_WhenConstructed_ThenThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new SqlChainLinkExpression(
                    new[] { "Observation" },
                    ReferenceSearchParam,
                    null,
                    reversed: false));
        }

        [Fact]
        public void GivenNullExpressions_WhenConstructed_ThenExpressionsAreNull()
        {
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: null,
                expressionOnTarget: null);

            Assert.Null(expression.ExpressionOnSource);
            Assert.Null(expression.ExpressionOnTarget);
        }

        [Fact]
        public void GivenReversedTrue_WhenToString_ThenIncludesReverse()
        {
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: true);

            var result = expression.ToString();

            Assert.Contains("Reverse", result);
            Assert.Contains("SqlChainLink", result);
            Assert.Contains("subject", result);
            Assert.Contains("Patient", result);
        }

        [Fact]
        public void GivenReversedFalse_WhenToString_ThenDoesNotIncludeReverse()
        {
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var result = expression.ToString();

            Assert.DoesNotContain("Reverse", result);
            Assert.Contains("SqlChainLink", result);
        }

        [Fact]
        public void GivenMultipleTargetResourceTypes_WhenToString_ThenIncludesAllTypes()
        {
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient", "Practitioner", "Organization" },
                reversed: false);

            var result = expression.ToString();

            Assert.Contains("Patient", result);
            Assert.Contains("Practitioner", result);
            Assert.Contains("Organization", result);
        }

        [Fact]
        public void GivenExpressionOnSource_WhenToString_ThenIncludesSource()
        {
            var sourceExpression = Expression.Equals(FieldName.TokenCode, null, "testCode");
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: sourceExpression);

            var result = expression.ToString();

            Assert.Contains("Source:", result);
        }

        [Fact]
        public void GivenExpressionOnTarget_WhenToString_ThenIncludesTarget()
        {
            var targetExpression = Expression.Equals(FieldName.TokenCode, null, "testCode");
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: targetExpression);

            var result = expression.ToString();

            Assert.Contains("Target:", result);
        }

        [Fact]
        public void GivenNullExpressions_WhenToString_ThenDoesNotIncludeSourceOrTarget()
        {
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var result = expression.ToString();

            Assert.DoesNotContain("Source:", result);
            Assert.DoesNotContain("Target:", result);
        }

        [Fact]
        public void GivenSameValues_WhenValueInsensitiveEquals_ThenReturnsTrue()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            Assert.True(expression1.ValueInsensitiveEquals(expression2));
            Assert.True(expression2.ValueInsensitiveEquals(expression1));
        }

        [Fact]
        public void GivenNull_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            Assert.False(expression.ValueInsensitiveEquals(null));
        }

        [Fact]
        public void GivenDifferentExpressionType_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var chainExpression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var otherExpression = Expression.Equals(FieldName.TokenCode, null, "test");

            Assert.False(chainExpression.ValueInsensitiveEquals(otherExpression));
        }

        [Fact]
        public void GivenDifferentResourceTypes_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "DiagnosticReport" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentResourceTypesLength_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation", "DiagnosticReport" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentTargetResourceTypes_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Practitioner" },
                reversed: false);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentTargetResourceTypesLength_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient", "Practitioner" },
                reversed: false);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentReferenceSearchParameter_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var param1 = new SearchParameterInfo(
                name: "subject",
                code: "subject",
                searchParamType: SearchParamType.Reference,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

            var param2 = new SearchParameterInfo(
                name: "patient",
                code: "patient",
                searchParamType: SearchParamType.Reference,
                url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-patient"));

            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                param1,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                param2,
                new[] { "Patient" },
                reversed: false);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentReversed_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: true);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenBothExpressionsOnSourceNull_WhenValueInsensitiveEquals_ThenReturnsTrue()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: null);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: null);

            Assert.True(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenOneExpressionOnSourceNull_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var sourceExpression = Expression.Equals(FieldName.TokenCode, null, "test");

            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: sourceExpression);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: null);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
            Assert.False(expression2.ValueInsensitiveEquals(expression1));
        }

        [Fact]
        public void GivenBothExpressionsOnTargetNull_WhenValueInsensitiveEquals_ThenReturnsTrue()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: null);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: null);

            Assert.True(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenOneExpressionOnTargetNull_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var targetExpression = Expression.Equals(FieldName.TokenCode, null, "test");

            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: targetExpression);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: null);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
            Assert.False(expression2.ValueInsensitiveEquals(expression1));
        }

        [Fact]
        public void GivenDifferentExpressionOnSource_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var sourceExpression1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var sourceExpression2 = Expression.Equals(FieldName.TokenSystem, null, "system1"); // Different field

            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: sourceExpression1);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: sourceExpression2);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenDifferentExpressionOnTarget_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var targetExpression1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var targetExpression2 = Expression.Equals(FieldName.TokenSystem, null, "system1"); // Different field

            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: targetExpression1);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: targetExpression2);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenSameExpressionTypeOnSourceDifferentValues_WhenValueInsensitiveEquals_ThenReturnsTrue()
        {
            // Value-insensitive equals should ignore parameter values, only structure matters
            var sourceExpression1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var sourceExpression2 = Expression.Equals(FieldName.TokenCode, null, "code2");

            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: sourceExpression1);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: sourceExpression2);

            Assert.True(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenSameExpressionTypeOnTargetDifferentValues_WhenValueInsensitiveEquals_ThenReturnsTrue()
        {
            // Value-insensitive equals should ignore parameter values, only structure matters
            var targetExpression1 = Expression.Equals(FieldName.TokenCode, null, "code1");
            var targetExpression2 = Expression.Equals(FieldName.TokenCode, null, "code2");

            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: targetExpression1);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnTarget: targetExpression2);

            Assert.True(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenMultipleResourceTypesInDifferentOrder_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation", "DiagnosticReport" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "DiagnosticReport", "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenMultipleTargetResourceTypesInDifferentOrder_WhenValueInsensitiveEquals_ThenReturnsFalse()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient", "Practitioner" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Practitioner", "Patient" },
                reversed: false);

            Assert.False(expression1.ValueInsensitiveEquals(expression2));
        }

        [Fact]
        public void GivenSameValuesWithExpressions_WhenAddValueInsensitiveHashCode_ThenProducesSameHashCode()
        {
            var sourceExpression = Expression.Equals(FieldName.TokenCode, null, "code1");
            var targetExpression = Expression.Equals(FieldName.TokenCode, null, "code2");

            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                sourceExpression,
                targetExpression);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                sourceExpression,
                targetExpression);

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.Equal(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }

        [Fact]
        public void GivenDifferentValues_WhenAddValueInsensitiveHashCode_ThenProducesDifferentHashCodes()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "DiagnosticReport" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.NotEqual(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }

        [Fact]
        public void GivenNullExpressions_WhenAddValueInsensitiveHashCode_ThenHandlesGracefully()
        {
            var expression = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false,
                expressionOnSource: null,
                expressionOnTarget: null);

            var hashCode = default(HashCode);

            var exception = Record.Exception(() => expression.AddValueInsensitiveHashCode(ref hashCode));

            Assert.Null(exception);
        }

        [Fact]
        public void GivenMultipleResourceTypes_WhenAddValueInsensitiveHashCode_ThenIncludesAllTypes()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation", "DiagnosticReport" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.NotEqual(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }

        [Fact]
        public void GivenMultipleTargetResourceTypes_WhenAddValueInsensitiveHashCode_ThenIncludesAllTypes()
        {
            var expression1 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient", "Practitioner" },
                reversed: false);

            var expression2 = new SqlChainLinkExpression(
                new[] { "Observation" },
                ReferenceSearchParam,
                new[] { "Patient" },
                reversed: false);

            var hashCode1 = default(HashCode);
            expression1.AddValueInsensitiveHashCode(ref hashCode1);

            var hashCode2 = default(HashCode);
            expression2.AddValueInsensitiveHashCode(ref hashCode2);

            Assert.NotEqual(hashCode1.ToHashCode(), hashCode2.ToHashCode());
        }
    }
}
