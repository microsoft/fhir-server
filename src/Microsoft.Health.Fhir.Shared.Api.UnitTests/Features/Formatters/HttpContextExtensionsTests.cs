// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class HttpContextExtensionsTests
    {
        public HttpContextExtensionsTests()
        {
        }

        [Fact]
        public void GivenARequestWithSummaryType_WhenSerializingTheResponse_ThenTheCorrectSummeryTypeIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_summary", "text");

            var summary = context.GetSummaryTypeOrDefault();

            Assert.Equal(SummaryType.Text, summary);
        }

        [Fact]
        public void GivenARequestWithCapsSummaryType_WhenSerializingTheResponse_ThenTheCorrectSummeryTypeIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_SUMMARY", "DATA");

            var summary = context.GetSummaryTypeOrDefault();

            Assert.Equal(SummaryType.Data, summary);
        }

        [Fact]
        public void GivenARequestWithCountType_WhenSerializingTheResponse_ThenTheCorrectSummeryTypeIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_count", "0");

            var summary = context.GetSummaryTypeOrDefault();

            Assert.Equal(SummaryType.Count, summary);
        }

        [Fact]
        public void GivenARequestWithUnknownSummaryType_WhenSerializingTheResponse_DefaultSummaryReturned()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_summary", "abc");

            var summary = context.GetSummaryTypeOrDefault();

            Assert.Equal(SummaryType.False, summary);
        }

        [Theory]
        [InlineData(500)]
        [InlineData(400)]
        [InlineData(202)]
        public void GivenARequestWithNon200Response_WhenSerializingTheResponse_ThenFalseIsReturned(int statusCode)
        {
            var context = new DefaultHttpContext();
            context.Response.StatusCode = statusCode;

            Assert.Equal(SummaryType.False, context.GetSummaryTypeOrDefault());
        }

        [Fact]
        public void GivenARequestWithNoSummaryType_WhenSerializingTheResponse_ThenFalseIsReturned()
        {
            var context = new DefaultHttpContext();

            var summary = context.GetSummaryTypeOrDefault();

            Assert.Equal(SummaryType.False, summary);
        }

        [Fact]
        public void GivenARequestWithElementsParam_WhenSerializingTheResponse_ThenTheCorrectElementsAreReturned()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_elements", "prop1");

            var elements = context.GetElementsOrDefault();

            Assert.Collection(elements, el => Assert.Equal("prop1", el));
        }

        [Fact]
        public void GivenARequestWithCapsElementsParam_WhenSerializingTheResponse_ThenTheCorrectElementsAreReturned()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_ELEMENTS", "PROP2");

            var elements = context.GetElementsOrDefault();

            Assert.Collection(elements, el => Assert.Equal("PROP2", el));
        }

        [Fact]
        public void GivenARequestWithMultipleElementsParam_WhenSerializingTheResponse_ThenTheCorrectElementsAreReturned()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_elements", "prop1,prop2");

            var elements = context.GetElementsOrDefault();

            Assert.Collection(elements, el => Assert.Equal("prop1", el), el => Assert.Equal("prop2", el));
        }

        [Theory]
        [InlineData(500)]
        [InlineData(400)]
        [InlineData(202)]
        public void GivenARequestWithNon200Response_WhenSerializingTheResponse_ThenNullIsReturned(int statusCode)
        {
            var context = new DefaultHttpContext();
            context.Response.StatusCode = statusCode;

            Assert.Null(context.GetElementsOrDefault());
        }

        [Fact]
        public void GivenARequestWithNoElementsParam_WhenSerializingTheResponse_ThenNullIsReturned()
        {
            var context = new DefaultHttpContext();

            var elements = context.GetElementsOrDefault();

            Assert.Null(elements);
        }

        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        public void GivenARequestWithEmptyElementsParam_WhenSerializingTheResponse_ThenNullIsReturned(string elementsParam)
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_elements", elementsParam);

            var elements = context.GetElementsOrDefault();

            Assert.Null(elements);
        }

        [Theory]
        [InlineData("true")]
        [InlineData("True")]
        [InlineData("TRUE")]
        [InlineData("   true  ")]
        public void GivenARequestWithPrettyIndentationSetToTrue_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied(string input)
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", input);

            var isPretty = context.GetPrettyOrDefault();

            Assert.True(isPretty);
        }

        [Theory]
        [InlineData("false")]
        [InlineData("False")]
        [InlineData("FALSE")]
        [InlineData("   false  ")]
        public void GivenARequestWithPrettyIndentationSetToFalse_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied(string input)
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", input);

            var isPretty = context.GetPrettyOrDefault();

            Assert.False(isPretty);
        }

        [Theory]
        [InlineData("true")]
        [InlineData("True")]
        [InlineData("TRUE")]
        [InlineData("   true  ")]
        public void GivenAnXmlRequestWithPrettyIndentationSetToTrue_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied(string input)
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", input);
            context.Request.QueryString.Add("_format", "xml");

            var isPretty = context.GetPrettyOrDefault();

            Assert.True(isPretty);
        }

        [Theory]
        [InlineData("false")]
        [InlineData("False")]
        [InlineData("FALSE")]
        [InlineData("   false  ")]
        public void GivenAnXmlRequestWithPrettyIndentationSetToFalse_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied(string input)
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", input);
            context.Request.QueryString.Add("_format", "xml");

            var isPretty = context.GetPrettyOrDefault();

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToTrueInCaps_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_PRETTY", "True");

            var isPretty = context.GetPrettyOrDefault();

            Assert.True(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToFalseInCaps_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_PRETTY", "False");

            var isPretty = context.GetPrettyOrDefault();

            Assert.False(isPretty);
        }

        [Theory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("1")]
        [InlineData("0")]
        public void GivenARequestWithPrettyIndentationSetToUnrecognizableInput_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied(string input)
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", input);

            var isPretty = context.GetPrettyOrDefault();

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationUnset_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();

            var isPretty = context.GetPrettyOrDefault();

            Assert.False(isPretty);
        }
    }
}
