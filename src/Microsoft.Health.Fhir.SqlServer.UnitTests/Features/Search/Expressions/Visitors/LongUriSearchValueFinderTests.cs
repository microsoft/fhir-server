// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search.Expressions.Visitors;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors.QueryGenerators;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.Expressions.Visitors
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class LongUriSearchValueFinderTests : IClassFixture<ModelInfoProviderFixture>
    {
        private const int IndexedLength = 256; // VLatest.UriSearchParam.Uri.Metadata.MaxLength

        private static readonly SearchParameterInfo InstantiatesUri = new SearchParameterInfo(
            name: "instantiates-uri",
            code: "instantiates-uri",
            searchParamType: SearchParamType.Uri,
            url: new Uri("http://hl7.org/fhir/SearchParameter/CarePlan-instantiates-uri"));

        private static readonly SearchParameterInfo Profile = new SearchParameterInfo(
            name: "_profile",
            code: "_profile",
            searchParamType: SearchParamType.Uri,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Resource-profile"));

        private static readonly SearchParameterInfo Name = new SearchParameterInfo(
            name: "name",
            code: "name",
            searchParamType: SearchParamType.String,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));

        private static readonly SearchParameterInfo Subject = new SearchParameterInfo(
            name: "subject",
            code: "subject",
            searchParamType: SearchParamType.Reference,
            url: new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        [Fact]
        public void GivenTheIndexedLength_WhenReadFromTheSchema_ThenItIs256()
        {
            Assert.Equal(IndexedLength, LongUriSearchValueFinder.IndexedUriMaxLength);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(IndexedLength - 1)]
        public void GivenAUriValueShorterThanTheIndexedLength_WhenVisited_ThenNothingIsFound(int length)
        {
            // Arrange
            Expression expression = Expression.SearchParameter(InstantiatesUri, Expression.StringEquals(FieldName.Uri, null, CreateUri(length), false));

            // Act
            IReadOnlyCollection<string> codes = expression.AcceptVisitor(LongUriSearchValueFinder.Instance, null);

            // Assert
            Assert.Null(codes);
        }

        [Theory]
        [InlineData(IndexedLength)]
        [InlineData(IndexedLength + 1)]
        [InlineData(1000)]
        public void GivenAUriValueOfAtLeastTheIndexedLength_WhenVisited_ThenTheSearchParameterIsFound(int length)
        {
            // Arrange
            Expression expression = Expression.SearchParameter(InstantiatesUri, Expression.StringEquals(FieldName.Uri, null, CreateUri(length), false));

            // Act
            IReadOnlyCollection<string> codes = expression.AcceptVisitor(LongUriSearchValueFinder.Instance, null);

            // Assert
            Assert.Equal(new[] { "instantiates-uri" }, codes);
        }

        [Fact]
        public void GivenALongUriBelowModifierValue_WhenVisited_ThenTheSearchParameterIsFound()
        {
            // Arrange
            Expression expression = Expression.SearchParameter(
                InstantiatesUri,
                Expression.And(
                    Expression.StartsWith(FieldName.Uri, null, CreateUri(IndexedLength), false),
                    Expression.NotStartsWith(FieldName.Uri, null, "urn:", false)));

            // Act
            IReadOnlyCollection<string> codes = expression.AcceptVisitor(LongUriSearchValueFinder.Instance, null);

            // Assert
            Assert.Equal(new[] { "instantiates-uri" }, codes);
        }

        [Fact]
        public void GivenALongValueOnANonUriField_WhenVisited_ThenNothingIsFound()
        {
            // Arrange
            Expression expression = Expression.SearchParameter(Name, Expression.StringEquals(FieldName.String, null, CreateUri(1000), false));

            // Act
            IReadOnlyCollection<string> codes = expression.AcceptVisitor(LongUriSearchValueFinder.Instance, null);

            // Assert
            Assert.Null(codes);
        }

        [Fact]
        public void GivenLongUriValuesInNestedAndRepeatedExpressions_WhenVisited_ThenEachSearchParameterIsFoundOnce()
        {
            // Arrange
            Expression longInstantiatesUri = Expression.SearchParameter(InstantiatesUri, Expression.StringEquals(FieldName.Uri, null, CreateUri(300), false));
            Expression expression = Expression.And(
                Expression.SearchParameter(Name, Expression.StringEquals(FieldName.String, null, "smith", false)),
                Expression.Union(UnionOperator.All, new[] { longInstantiatesUri, longInstantiatesUri }),
                Expression.Chained(
                    new[] { "Observation" },
                    Subject,
                    new[] { "Patient" },
                    false,
                    Expression.SearchParameter(Profile, Expression.StringEquals(FieldName.Uri, null, CreateUri(400), false))));

            // Act
            IReadOnlyCollection<string> codes = expression.AcceptVisitor(LongUriSearchValueFinder.Instance, null);

            // Assert
            Assert.Equal(new[] { "_profile", "instantiates-uri" }, Sorted(codes));
        }

        private static string CreateUri(int length)
        {
            const string prefix = "https://careplan.invalid/";
            return length <= prefix.Length ? prefix[..length] : prefix + new string('b', length - prefix.Length);
        }

        private static string[] Sorted(IReadOnlyCollection<string> codes)
        {
            var result = new List<string>(codes);
            result.Sort(StringComparer.Ordinal);
            return result.ToArray();
        }
    }
}
