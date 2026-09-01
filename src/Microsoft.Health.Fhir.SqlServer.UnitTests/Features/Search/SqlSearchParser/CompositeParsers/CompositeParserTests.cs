// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SqlSearchParser.CompositeParsers;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SqlSearchParser.CompositeParsers
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class CompositeParserTests
    {
        private static readonly SqlSearchParameterDefinitionManager MockDefManager =
            ParserTestHelper.CreateMockDefinitionManager();

        [Fact]
        public void GivenTokenStringComposite_WhenBuildWhereClause_ThenCombinesTokenAndStringConditions()
        {
            var parser = new TokenStringCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("http://sys|code$stringval", string.Empty);

            Assert.Contains("Code1", result);
            Assert.Contains("SystemId1", result);
            Assert.Contains("Text2", result);
            Assert.Contains("AND", result);
        }

        [Fact]
        public void GivenTokenTokenComposite_WhenBuildWhereClause_ThenCombinesTwoTokenConditions()
        {
            var parser = new TokenTokenCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("code1$code2", string.Empty);

            Assert.Contains("Code1", result);
            Assert.Contains("Code2", result);
            Assert.Contains("AND", result);
        }

        [Fact]
        public void GivenTokenDateTimeComposite_WhenBuildWhereClause_ThenCombinesTokenAndDateConditions()
        {
            var parser = new TokenDateTimeCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("code$2024-01-15", string.Empty);

            Assert.Contains("Code1", result);
            Assert.Contains("DateTime2", result);
            Assert.Contains("AND", result);
        }

        [Fact]
        public void GivenTokenQuantityComposite_WhenBuildWhereClause_ThenCombinesTokenAndQuantityConditions()
        {
            var parser = new TokenQuantityCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("code$100", string.Empty);

            Assert.Contains("Code1", result);
            Assert.Contains("Value2", result);
            Assert.Contains("AND", result);
        }

        [Fact]
        public void GivenTokenNumberNumberComposite_WhenBuildWhereClause_ThenCombinesThreeComponents()
        {
            var parser = new TokenNumberNumberCompositeSqlParser(MockDefManager);
            var result = parser.BuildWhereClause("code$100$200", string.Empty);

            Assert.Contains("Code1", result);
            Assert.Contains("Value2", result);
            Assert.Contains("Value3", result);
        }

        [Fact]
        public void GivenTwoComponentComposite_WhenValueHasNoDollarSign_ThenThrows()
        {
            var parser = new TokenStringCompositeSqlParser(MockDefManager);
            Assert.Throws<InvalidOperationException>(() =>
                parser.BuildWhereClause("nodollarsign", string.Empty));
        }

        [Fact]
        public void GivenThreeComponentComposite_WhenValueHasOnlyOneDollarSign_ThenThrows()
        {
            var parser = new TokenNumberNumberCompositeSqlParser(MockDefManager);
            Assert.Throws<InvalidOperationException>(() =>
                parser.BuildWhereClause("code$100", string.Empty));
        }

        [Fact]
        public void GivenReferenceTokenComposite_WhenBuildWhereClause_ThenCombinesReferenceAndTokenConditions()
        {
            var fhirModel = ParserTestHelper.CreateMockFhirModel(("Patient", 1));
            var parser = new ReferenceTokenCompositeSqlParser(MockDefManager, fhirModel);
            var result = parser.BuildWhereClause("Patient/123$active", string.Empty);

            Assert.Contains("ReferenceResourceId1", result);
            Assert.Contains("Code2", result);
            Assert.Contains("AND", result);
        }
    }
}
