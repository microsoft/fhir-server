// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.BaseParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class ReferenceSqlParserTests
    {
        private readonly ISqlServerFhirModel _fhirModel;
        private readonly ReferenceSqlParser _parser;

        public ReferenceSqlParserTests()
        {
            _fhirModel = Substitute.For<ISqlServerFhirModel>();
            _parser = new ReferenceSqlParser(ParserTestHelper.CreateMockDefinitionManager(), _fhirModel);
        }

        [Fact]
        public void GivenEmptyValue_WhenBuildWhereClause_ThenReturnsAlwaysTrue()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause(string.Empty, string.Empty);

            // Assert
            Assert.Equal("1=1", result);
        }

        [Fact]
        public void GivenIdOnly_WhenBuildWhereClause_ThenGeneratesReferenceIdConditionOnly()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("123", string.Empty);

            // Assert
            Assert.Equal("t.ReferenceResourceId = '123'", result);
        }

        [Fact]
        public void GivenRelativeReference_WhenBuildWhereClause_ThenGeneratesIdAndTypeConditions()
        {
            // Arrange
            short patientTypeId = 42;
            _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
                .Returns(x =>
                {
                    x[1] = patientTypeId;
                    return true;
                });

            // Act
            var result = _parser.BuildWhereClause("Patient/123", string.Empty);

            // Assert
            Assert.Contains("t.ReferenceResourceId = '123'", result);
            Assert.Contains("t.ReferenceResourceTypeId = 42", result);
        }

        [Fact]
        public void GivenAbsoluteUrl_WhenBuildWhereClause_ThenGeneratesIdTypeAndBaseUriConditions()
        {
            // Arrange
            short patientTypeId = 5;
            _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
                .Returns(x =>
                {
                    x[1] = patientTypeId;
                    return true;
                });

            // Act
            var result = _parser.BuildWhereClause("http://server/Patient/123", string.Empty);

            // Assert
            Assert.Contains("t.ReferenceResourceId = '123'", result);
            Assert.Contains("t.ReferenceResourceTypeId = 5", result);
            Assert.Contains("t.BaseUri = 'http://server'", result);
        }

        [Fact]
        public void GivenTypeModifier_WhenBuildWhereClause_ThenUsesModifierAsResourceType()
        {
            // Arrange
            short practitionerTypeId = 99;
            _fhirModel.TryGetResourceTypeId("Practitioner", out Arg.Any<short>())
                .Returns(x =>
                {
                    x[1] = practitionerTypeId;
                    return true;
                });

            // Act
            var result = _parser.BuildWhereClause("123", "Practitioner");

            // Assert
            Assert.Contains("t.ReferenceResourceId = '123'", result);
            Assert.Contains("t.ReferenceResourceTypeId = 99", result);
        }

        [Fact]
        public void GivenUnknownResourceType_WhenBuildWhereClause_ThenReturnsNeverTrue()
        {
            // Arrange
            _fhirModel.TryGetResourceTypeId("UnknownType", out Arg.Any<short>()).Returns(false);

            // Act
            var result = _parser.BuildWhereClause("UnknownType/123", string.Empty);

            // Assert
            Assert.Equal("1=0", result);
        }

        [Fact]
        public void GivenRelativeReferenceWithSingleQuote_WhenBuildWhereClause_ThenEscapesId()
        {
            // Arrange
            short patientTypeId = 10;
            _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
                .Returns(x =>
                {
                    x[1] = patientTypeId;
                    return true;
                });

            // Act
            var result = _parser.BuildWhereClause("Patient/O'Brien", string.Empty);

            // Assert
            Assert.Contains("O''Brien", result);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnNames()
        {
            // Arrange / Act
            var result = _parser.BuildWhereClause("123", string.Empty, columnSuffix: 1);

            // Assert
            Assert.Contains("t.ReferenceResourceId1 = '123'", result);
        }
    }
}
