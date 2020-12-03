// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public static class SearchExpressionTestHelper
    {
        internal static void ValidateSearchParameterExpression(Expression expression, string paramName, Action<Expression> valueValidator)
        {
            SearchParameterExpression parameterExpression = Assert.IsType<SearchParameterExpression>(expression);
            Assert.Equal(paramName, parameterExpression.Parameter.Name);
            valueValidator(parameterExpression.Expression);
        }

        internal static void ValidateResourceTypeSearchParameterExpression(Expression expression, string typeName)
        {
            ValidateSearchParameterExpression(expression, SearchParameterNames.ResourceType, e => ValidateStringExpression(e, FieldName.TokenCode, StringOperator.Equals, typeName, false));
        }

        public static void ValidateChainedExpression(
            Expression expression,
            Hl7.Fhir.Model.ResourceType resourceType,
            SearchParameterInfo referenceSearchParam,
            string targetResourceType,
            Action<Expression> childExpressionValidator)
        {
            ChainedExpression chainedExpression = Assert.IsType<ChainedExpression>(expression);

            Assert.Equal(resourceType.ToString(), chainedExpression.ResourceType);
            Assert.Equal(referenceSearchParam, chainedExpression.ReferenceSearchParameter);
            Assert.Equal(targetResourceType, chainedExpression.TargetResourceType);

            childExpressionValidator(chainedExpression.Expression);
        }

        public static void ValidateChainedExpression(
            Expression expression,
            Type resourceType,
            SearchParameterInfo referenceSearchParam,
            Type targetResourceType,
            Action<Expression> childExpressionValidator)
        {
            ChainedExpression chainedExpression = Assert.IsType<ChainedExpression>(expression);

            Assert.Equal(resourceType.ToString(), chainedExpression.ResourceType);
            Assert.Equal(referenceSearchParam, chainedExpression.ReferenceSearchParameter);
            Assert.Equal(targetResourceType.ToString(), chainedExpression.TargetResourceType.ToString());

            childExpressionValidator(chainedExpression.Expression);
        }

        public static void ValidateMultiaryExpression(
            Expression expression,
            MultiaryOperator multiaryOperator,
            params Action<Expression>[] valueValidators)
        {
            MultiaryExpression multiaryExpression = Assert.IsType<MultiaryExpression>(expression);

            Assert.Equal(multiaryOperator, multiaryExpression.MultiaryOperation);

            Assert.Collection(
                multiaryExpression.Expressions,
                valueValidators);
        }

        public static void ValidateEqualsExpression(Expression expression, FieldName expectedFieldName, object expectedValue)
        {
            ValidateBinaryExpression(expression, expectedFieldName, BinaryOperator.Equal, expectedValue);
        }

        public static void ValidateBinaryExpression(
            Expression expression,
            FieldName expectedFieldName,
            BinaryOperator expectedBinaryOperator,
            object expectedValue)
        {
            BinaryExpression equalExpression = Assert.IsType<BinaryExpression>(expression);

            Assert.Equal(expectedFieldName, equalExpression.FieldName);
            Assert.Equal(expectedBinaryOperator, equalExpression.BinaryOperator);
            Assert.Equal(expectedValue, equalExpression.Value);
        }

        public static void ValidateStringExpression(
            Expression expression,
            FieldName expectedFieldName,
            StringOperator expectedStringOperator,
            string expectedValue,
            bool expectedIgnoreCase)
        {
            StringExpression stringExpression = Assert.IsType<StringExpression>(expression);

            Assert.Equal(expectedStringOperator, stringExpression.StringOperator);
            Assert.Equal(expectedFieldName, stringExpression.FieldName);
            Assert.Equal(expectedValue, stringExpression.Value);
            Assert.Equal(expectedIgnoreCase, stringExpression.IgnoreCase);
        }

        public static void ValidateDateTimeBinaryOperatorExpression(
            Expression expression,
            FieldName expectedFieldName,
            BinaryOperator expectedExpression,
            DateTimeOffset expectedValue)
        {
            BinaryExpression bExpression = Assert.IsType<BinaryExpression>(expression);

            Assert.Equal(expectedExpression, bExpression.BinaryOperator);
            Assert.Equal(expectedFieldName, bExpression.FieldName);
            Assert.Equal(expectedValue, bExpression.Value);
        }

        public static void ValidateMissingParamExpression(
            Expression expression,
            string expectedParamName,
            bool expectedIsMissing)
        {
            MissingSearchParameterExpression mpExpression = Assert.IsType<MissingSearchParameterExpression>(expression);

            Assert.Equal(expectedParamName, mpExpression.Parameter.Name);
            Assert.Equal(expectedIsMissing, mpExpression.IsMissing);
        }

        public static void ValidateMissingFieldExpression(
            Expression expression,
            FieldName expectedFieldName)
        {
            MissingFieldExpression mfExpression = Assert.IsType<MissingFieldExpression>(expression);

            Assert.Equal(expectedFieldName, mfExpression.FieldName);
        }

        public static void ValidateCompartmentSearchExpression(
            Expression expression,
            string compartmentType,
            string compartmentId)
        {
            CompartmentSearchExpression compartmentSearchExpression = Assert.IsType<CompartmentSearchExpression>(expression);

            Assert.Equal(compartmentType, compartmentSearchExpression.CompartmentType);
            Assert.Equal(compartmentId, compartmentSearchExpression.CompartmentId);
        }

        public static IEnumerable<object[]> GetEnumAsMemberData<TEnum>(Predicate<TEnum> predicate = null)
        {
            return Enum.GetNames(typeof(TEnum))
                .Select(name => (TEnum)Enum.Parse(typeof(TEnum), name))
                .Where(comparator => predicate?.Invoke(comparator) ?? true)
                .Select(comparator => new object[] { comparator });
        }
    }
}
