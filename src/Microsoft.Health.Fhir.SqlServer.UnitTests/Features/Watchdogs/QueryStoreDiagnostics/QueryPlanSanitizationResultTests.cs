// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Reflection;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs.QueryStoreDiagnostics.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Watchdogs.QueryStoreDiagnostics
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class QueryPlanSanitizationResultTests
    {
        [Fact]
        public void GivenTheResultType_WhenInspected_ThenOnlyFactoriesCanCreateIt()
        {
            // Act
            ConstructorInfo[] constructors = typeof(QueryPlanSanitizationResult)
                .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            // Assert
            Assert.NotEmpty(constructors);
            Assert.All(constructors, constructor => Assert.True(constructor.IsPrivate));
        }

        [Fact]
        public void GivenSanitizedXmlWithinTheFieldCap_WhenCreated_ThenCarriesThePlanAndIsNotTruncated()
        {
            // Arrange
            const string xml = "<ShowPlanXML />";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizationResult.Sanitized(xml, originalLength: 64, sanitizedLength: xml.Length);

            // Assert
            Assert.Equal(QueryPlanSanitizer.SanitizedStatus, result.Status);
            Assert.Equal(xml, result.Xml);
            Assert.False(result.Truncated);
            Assert.Equal(64, result.OriginalLength);
            Assert.Equal(xml.Length, result.SanitizedLength);
        }

        [Fact]
        public void GivenSanitizedXmlShorterThanTheSanitizedLength_WhenCreated_ThenTruncationIsDerivedRatherThanDeclared()
        {
            // Arrange
            const string xml = "<ShowPlanXML";

            // Act
            QueryPlanSanitizationResult result = QueryPlanSanitizationResult.Sanitized(xml, originalLength: 512, sanitizedLength: 256);

            // Assert
            Assert.True(result.Truncated);
            Assert.Equal(256, result.SanitizedLength);
            Assert.Equal(xml, result.Xml);
        }

        [Fact]
        public void GivenNullXml_WhenCreatingASanitizedResult_ThenTheIllegalCombinationIsRejected()
        {
            // Act + Assert
            Assert.Throws<ArgumentNullException>(() => QueryPlanSanitizationResult.Sanitized(null, 10, 10));
        }

        [Fact]
        public void GivenASanitizedLengthBelowThePayloadLength_WhenCreatingASanitizedResult_ThenTheIllegalCombinationIsRejected()
        {
            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => QueryPlanSanitizationResult.Sanitized("<ShowPlanXML />", 128, 1));
        }

        [Fact]
        public void GivenNegativeLengths_WhenCreatingResults_ThenTheIllegalCombinationsAreRejected()
        {
            // Act + Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => QueryPlanSanitizationResult.Sanitized("<ShowPlanXML />", -1, 15));
            Assert.Throws<ArgumentOutOfRangeException>(() => QueryPlanSanitizationResult.InvalidXml(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => QueryPlanSanitizationResult.VerificationFailed(-1, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => QueryPlanSanitizationResult.VerificationFailed(0, -1));
        }

        [Fact]
        public void GivenAnyFailureFactory_WhenCreated_ThenTheXmlIsAlwaysNullAndNeverTruncated()
        {
            // Act
            QueryPlanSanitizationResult[] results =
            {
                QueryPlanSanitizationResult.PlanXmlUnavailable(),
                QueryPlanSanitizationResult.InvalidXml(128),
                QueryPlanSanitizationResult.VerificationFailed(128, 64),
            };

            // Assert
            Assert.All(results, result => Assert.Null(result.Xml));
            Assert.All(results, result => Assert.False(result.Truncated));
            Assert.All(results, result => Assert.NotEqual(QueryPlanSanitizer.SanitizedStatus, result.Status));
            Assert.Equal(
                new[] { QueryPlanSanitizer.PlanXmlUnavailableStatus, QueryPlanSanitizer.InvalidXmlStatus, QueryPlanSanitizer.VerificationFailedStatus },
                results.Select(result => result.Status));
        }
    }
}
