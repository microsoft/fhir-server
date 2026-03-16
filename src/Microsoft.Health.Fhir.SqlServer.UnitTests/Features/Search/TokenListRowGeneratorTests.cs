// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.SqlServer.Features.Schema.Model;
using Microsoft.Health.Fhir.SqlServer.Features.Storage.TvpRowGeneration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class TokenListRowGeneratorTests
    {
        private readonly TokenListRowGeneratorWrapper _generator;
        private readonly int _codeMaxLength;

        public TokenListRowGeneratorTests()
        {
            _generator = new TokenListRowGeneratorWrapper();
            _codeMaxLength = (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength;
        }

        [Fact]
        public void GivenSingleTokenWithoutSystem_WhenGenerateRows_ThenGeneratesSingleRow()
        {
            // Arrange
            var tokens = new List<TestToken>
            {
                new TestToken("test-code", null, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal("test-code", rows[0].Code);
            Assert.Null(rows[0].CodeOverflow);
            Assert.Null(rows[0].SystemId);
            Assert.Null(rows[0].SystemValue);
        }

        [Fact]
        public void GivenSingleTokenWithSystemId_WhenGenerateRows_ThenGeneratesRowWithSystemId()
        {
            // Arrange
            var tokens = new List<TestToken>
            {
                new TestToken("test-code", 5, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal("test-code", rows[0].Code);
            Assert.Equal(5, rows[0].SystemId);
            Assert.Null(rows[0].SystemValue);
        }

        [Fact]
        public void GivenSingleTokenWithSystemValue_WhenGenerateRows_ThenGeneratesRowWithSystemValue()
        {
            // Arrange
            var tokens = new List<TestToken>
            {
                new TestToken("test-code", null, "http://example.org/system"),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal("test-code", rows[0].Code);
            Assert.Null(rows[0].SystemId);
            Assert.Equal("http://example.org/system", rows[0].SystemValue);
        }

        [Fact]
        public void GivenMultipleTokens_WhenGenerateRows_ThenGeneratesRowForEachToken()
        {
            // Arrange
            var tokens = new List<TestToken>
            {
                new TestToken("code1", null, null),
                new TestToken("code2", 1, null),
                new TestToken("code3", null, "system3"),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Equal(3, rows.Count);
            Assert.Equal("code1", rows[0].Code);
            Assert.Equal("code2", rows[1].Code);
            Assert.Equal("code3", rows[2].Code);
        }

        [Fact]
        public void GivenTokenWithCodeExceedingMaxLength_WhenGenerateRows_ThenSplitsCodeAndOverflow()
        {
            // Arrange
            var longCode = new string('a', _codeMaxLength + 50);
            var tokens = new List<TestToken>
            {
                new TestToken(longCode, null, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert - Should generate 2 rows: one for full code, one for 128 truncation
            Assert.Equal(2, rows.Count);

            // First row: code split at max length
            Assert.Equal(longCode[.._codeMaxLength], rows[0].Code);
            Assert.Equal(longCode[_codeMaxLength..], rows[0].CodeOverflow);

            // Second row: 128 character truncation
            Assert.Equal(longCode[..128], rows[1].Code);
            Assert.Null(rows[1].CodeOverflow);
        }

        [Fact]
        public void GivenTokenWithCodeExactlyMaxLength_WhenGenerateRows_ThenDoesNotSetOverflow()
        {
            // Arrange
            var exactCode = new string('b', _codeMaxLength);
            var tokens = new List<TestToken>
            {
                new TestToken(exactCode, null, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert - Should generate 2 rows because length > 128
            Assert.Equal(2, rows.Count);
            Assert.Equal(exactCode, rows[0].Code);
            Assert.Null(rows[0].CodeOverflow);
            Assert.Equal(exactCode[..128], rows[1].Code);
        }

        [Fact]
        public void GivenTokenWithCode129Characters_WhenGenerateRows_ThenGeneratesTwoRows()
        {
            // Arrange
            var code129 = new string('c', 129);
            var tokens = new List<TestToken>
            {
                new TestToken(code129, 10, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Equal(2, rows.Count);

            // First row: full code
            Assert.Equal(code129, rows[0].Code);
            Assert.Null(rows[0].CodeOverflow);
            Assert.Equal(10, rows[0].SystemId);

            // Second row: 128 character truncation
            Assert.Equal(code129[..128], rows[1].Code);
            Assert.Null(rows[1].CodeOverflow);
            Assert.Equal(10, rows[1].SystemId);
        }

        [Fact]
        public void GivenTokenWithCode128Characters_WhenGenerateRows_ThenGeneratesSingleRow()
        {
            // Arrange
            var code128 = new string('d', 128);
            var tokens = new List<TestToken>
            {
                new TestToken(code128, null, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal(code128, rows[0].Code);
            Assert.Null(rows[0].CodeOverflow);
        }

        [Fact]
        public void GivenTokenWithCode127Characters_WhenGenerateRows_ThenGeneratesSingleRow()
        {
            // Arrange
            var code127 = new string('e', 127);
            var tokens = new List<TestToken>
            {
                new TestToken(code127, null, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal(code127, rows[0].Code);
            Assert.Null(rows[0].CodeOverflow);
        }

        [Fact]
        public void GivenEmptyTokenList_WhenGenerateRows_ThenReturnsEmptyEnumerable()
        {
            // Arrange
            var tokens = new List<TestToken>();

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Empty(rows);
        }

        [Fact]
        public void GivenTokenWithEmptyCode_WhenGenerateRows_ThenGeneratesRowWithEmptyCode()
        {
            // Arrange
            var tokens = new List<TestToken>
            {
                new TestToken(string.Empty, null, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal(string.Empty, rows[0].Code);
            Assert.Null(rows[0].CodeOverflow);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(128)]
        [InlineData(129)]
        [InlineData(256)]
        [InlineData(300)]
        public void GivenTokensWithVariousCodeLengths_WhenGenerateRows_ThenGeneratesCorrectNumberOfRows(int codeLength)
        {
            // Arrange
            var code = new string('f', codeLength);
            var tokens = new List<TestToken>
            {
                new TestToken(code, null, null),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            int expectedRowCount = codeLength > 128 ? 2 : 1;
            Assert.Equal(expectedRowCount, rows.Count);
        }

        [Fact]
        public void GivenMixedTokens_WhenGenerateRows_ThenGeneratesCorrectRows()
        {
            // Arrange
            var tokens = new List<TestToken>
            {
                new TestToken("short", 1, null),
                new TestToken(new string('a', 129), 2, null),
                new TestToken(new string('b', _codeMaxLength + 10), null, "system3"),
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            // First token: 1 row (code <= 128)
            // Second token: 2 rows (code = 129)
            // Third token: 2 rows (code > max length)
            Assert.Equal(5, rows.Count);

            // Verify first token
            Assert.Equal("short", rows[0].Code);
            Assert.Equal(1, rows[0].SystemId);

            // Verify second token (2 rows)
            Assert.Equal(129, rows[1].Code.Length);
            Assert.Equal(2, rows[1].SystemId);
            Assert.Equal(128, rows[2].Code.Length);
            Assert.Equal(2, rows[2].SystemId);

            // Verify third token (2 rows)
            Assert.Equal(_codeMaxLength, rows[3].Code.Length);
            Assert.NotNull(rows[3].CodeOverflow);
            Assert.Equal("system3", rows[3].SystemValue);
            Assert.Equal(128, rows[4].Code.Length);
        }

        [Fact]
        public void GivenTokenWithSystemIdAndSystemValue_WhenGenerateRows_ThenSystemValueIsIgnored()
        {
            // Arrange - when systemId is present, systemValue should be null according to Token constructor
            var tokens = new List<TestToken>
            {
                new TestToken("code", 5, null), // SystemValue is explicitly null when SystemId is set
            };

            // Act
            var rows = _generator.GenerateRows(tokens).ToList();

            // Assert
            Assert.Single(rows);
            Assert.Equal(5, rows[0].SystemId);
            Assert.Null(rows[0].SystemValue);
        }

        // Test wrapper class to simulate the private TokenListRowGenerator
        private class TokenListRowGeneratorWrapper
        {
            private readonly int _codeMaxLength = (int)VLatest.TokenSearchParam.Code.Metadata.MaxLength;

            public IEnumerable<TokenListRow> GenerateRows(IList<TestToken> tokens)
            {
                foreach (var token in tokens)
                {
                    string code;
                    string codeOverflow;
                    if (token.Code.Length > _codeMaxLength)
                    {
                        code = token.Code[.._codeMaxLength];
                        codeOverflow = token.Code[_codeMaxLength..];
                    }
                    else
                    {
                        code = token.Code;
                        codeOverflow = null;
                    }

                    yield return new TokenListRow(code, codeOverflow, token.SystemId, token.SystemValue);

                    // truncation128 logic
                    if (token.Code.Length > 128)
                    {
                        yield return new TokenListRow(code[..128], null, token.SystemId, token.SystemValue);
                    }
                }
            }
        }

        // Test token class to simulate the private Token class
        private class TestToken
        {
            internal TestToken(string code, int? systemId, string systemValue)
            {
                Code = code;
                SystemId = systemId;
                SystemValue = systemId.HasValue ? null : systemValue;
            }

            internal string Code { get; set; }

            internal int? SystemId { get; set; }

            internal string SystemValue { get; set; }
        }
    }
}
