// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions
{
    [Trait("Traits.OwningTeam", OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class LastUpdatedToResourceSurrogateIdRewriterTests
    {
        [InlineData(BinaryOperator.GreaterThan, "2020-09-24T12:00:00.500Z", BinaryOperator.GreaterThanOrEqual, "2020-09-24T12:00:00.501Z")]
        [InlineData(BinaryOperator.GreaterThan, "2020-09-24T12:00:00.5001Z", BinaryOperator.GreaterThanOrEqual, "2020-09-24T12:00:00.501Z")]
        [InlineData(BinaryOperator.GreaterThanOrEqual, "2020-09-24T12:00:00.500Z", BinaryOperator.GreaterThanOrEqual, "2020-09-24T12:00:00.500Z")]
        [InlineData(BinaryOperator.GreaterThanOrEqual, "2020-09-24T12:00:00.5001Z", BinaryOperator.GreaterThanOrEqual, "2020-09-24T12:00:00.501Z")]
        [InlineData(BinaryOperator.LessThan, "2020-09-24T12:00:00.500Z", BinaryOperator.LessThan, "2020-09-24T12:00:00.500Z")]
        [InlineData(BinaryOperator.LessThan, "2020-09-24T12:00:00.5001Z", BinaryOperator.LessThan, "2020-09-24T12:00:00.501Z")] // will yield 500, 499
        [InlineData(BinaryOperator.LessThanOrEqual, "2020-09-24T12:00:00.500Z", BinaryOperator.LessThan, "2020-09-24T12:00:00.501Z")]
        [InlineData(BinaryOperator.LessThanOrEqual, "2020-09-24T12:00:00.5001Z", BinaryOperator.LessThan, "2020-09-24T12:00:00.501Z")] // will yield 500, 499
        [Theory]
        public void GivenAnExpressionOverLastUpdated_WhenTranslatedToResourceSurrogateId_HasCorrectRanges(BinaryOperator inputOperator, string inputDateTimeOffset, BinaryOperator expectedOperator, string expectedDateTimeOffset)
        {
            var input = new BinaryExpression(inputOperator, FieldName.DateTimeStart, null, DateTimeOffset.Parse(inputDateTimeOffset));

            var output = input.AcceptVisitor(LastUpdatedToResourceSurrogateIdRewriter.Instance, null);

            BinaryExpression binaryOutput = Assert.IsType<BinaryExpression>(output);
            Assert.Equal(SqlFieldName.ResourceSurrogateId, binaryOutput.FieldName);
            Assert.Equal(expectedOperator, binaryOutput.BinaryOperator);
            Assert.Equal(DateTimeOffset.Parse(expectedDateTimeOffset), ResourceSurrogateIdHelper.ResourceSurrogateIdToLastUpdated((long)binaryOutput.Value));
        }
    }
}
