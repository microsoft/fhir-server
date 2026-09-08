// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Data.SqlClient;
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
            var result = _parser.BuildWhereClause(string.Empty, string.Empty, new ParserOptions());

            // Assert
            Assert.Equal("1=1", result);
        }

        [Fact]
        public void GivenIdOnly_WhenBuildWhereClause_ThenGeneratesReferenceIdConditionOnly()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("123", string.Empty, options);

            // Assert
            Assert.Equal("t.ReferenceResourceId = @p0", result);
            Assert.Equal("123", command.Parameters["@p0"].Value);
            Assert.Equal(System.Data.SqlDbType.VarChar, command.Parameters["@p0"].SqlDbType);
        }

        [Fact]
        public void GivenRelativeReference_WhenBuildWhereClause_ThenGeneratesIdAndTypeConditions()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            short patientTypeId = 42;
            _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
                .Returns(x =>
                {
                    x[1] = patientTypeId;
                    return true;
                });

            // Act
            var result = _parser.BuildWhereClause("Patient/123", string.Empty, options);

            // Assert
            Assert.Equal("t.ReferenceResourceId = @p0 AND t.ReferenceResourceTypeId = 42", result);
            Assert.Equal("123", command.Parameters["@p0"].Value);
            Assert.Single(command.Parameters);
        }

        [Fact]
        public void GivenAbsoluteUrl_WhenBuildWhereClause_ThenGeneratesIdTypeAndBaseUriConditions()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            short patientTypeId = 5;
            _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
                .Returns(x =>
                {
                    x[1] = patientTypeId;
                    return true;
                });

            // Act
            var result = _parser.BuildWhereClause("http://server/Patient/123", string.Empty, options);

            // Assert
            Assert.Equal("t.ReferenceResourceId = @p0 AND t.ReferenceResourceTypeId = 5 AND t.BaseUri = @p1", result);
            Assert.Equal("123", command.Parameters["@p0"].Value);
            Assert.Equal("http://server", command.Parameters["@p1"].Value);
            Assert.Equal(System.Data.SqlDbType.VarChar, command.Parameters["@p1"].SqlDbType);
        }

        [Fact]
        public void GivenTypeModifier_WhenBuildWhereClause_ThenUsesModifierAsResourceType()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            short practitionerTypeId = 99;
            _fhirModel.TryGetResourceTypeId("Practitioner", out Arg.Any<short>())
                .Returns(x =>
                {
                    x[1] = practitionerTypeId;
                    return true;
                });

            // Act
            var result = _parser.BuildWhereClause("123", "Practitioner", options);

            // Assert
            Assert.Equal("t.ReferenceResourceId = @p0 AND t.ReferenceResourceTypeId = 99", result);
            Assert.Equal("123", command.Parameters["@p0"].Value);
            Assert.Single(command.Parameters);
        }

        [Fact]
        public void GivenUnknownResourceType_WhenBuildWhereClause_ThenReturnsNeverTrue()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            _fhirModel.TryGetResourceTypeId("UnknownType", out Arg.Any<short>()).Returns(false);

            // Act
            var result = _parser.BuildWhereClause("UnknownType/123", string.Empty, options);

            // Assert
            Assert.Equal("1=0", result);
            Assert.Empty(command.Parameters);
        }

        [Fact]
        public void GivenRelativeReferenceWithSingleQuote_WhenBuildWhereClause_ThenKeepsQuoteOutOfSqlText()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);
            short patientTypeId = 10;
            _fhirModel.TryGetResourceTypeId("Patient", out Arg.Any<short>())
                .Returns(x =>
                {
                    x[1] = patientTypeId;
                    return true;
                });

            // Act
            var result = _parser.BuildWhereClause("Patient/O'Brien", string.Empty, options);

            // Assert
            Assert.Equal("t.ReferenceResourceId = @p0 AND t.ReferenceResourceTypeId = 10", result);
            Assert.Equal("O'Brien", command.Parameters["@p0"].Value);
            Assert.DoesNotContain("O'Brien", result);
            Assert.DoesNotContain("O''Brien", result);
        }

        [Fact]
        public void GivenColumnSuffix_WhenBuildWhereClause_ThenAppendsSuffixToColumnNames()
        {
            // Arrange
            using var command = new SqlCommand();
            var options = ParserTestHelper.CreateParserOptions(command);

            // Act
            var result = _parser.BuildWhereClause("123", string.Empty, options, columnSuffix: 1);

            // Assert
            Assert.Equal("t.ReferenceResourceId1 = @p0", result);
            Assert.Equal("123", command.Parameters["@p0"].Value);
        }
    }
}
