// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Api.Features.Formatters;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    public class HttpContextExtensionsTests
    {
        private readonly ILogger<string> _logger;

        public HttpContextExtensionsTests()
        {
            _logger = new NullLogger<string>();
        }

        [Fact]
        public void GivenARequestWithSummaryType_WhenSerializingTheResponse_ThenTheCorrectSummeryTypeIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_summary", "text");

            var summary = context.GetSummaryType(_logger);

            Assert.Equal(SummaryType.Text, summary);
        }

        [Fact]
        public void GivenARequestWithCapsSummaryType_WhenSerializingTheResponse_ThenTheCorrectSummeryTypeIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_SUMMARY", "DATA");

            var summary = context.GetSummaryType(_logger);

            Assert.Equal(SummaryType.Data, summary);
        }

        [Fact]
        public void GivenARequestWithUnknownSummaryType_WhenSerializingTheResponse_ThenFalseIsReturned()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_summary", "abc");

            Assert.Throws<ArgumentException>(() => context.GetSummaryType(_logger));
        }

        [Fact]
        public void GivenARequestWithNoSummaryType_WhenSerializingTheResponse_ThenFalseIsReturned()
        {
            var context = new DefaultHttpContext();

            var summary = context.GetSummaryType(_logger);

            Assert.Equal(SummaryType.False, summary);
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

            var isPretty = context.GetIsPretty();

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

            var isPretty = context.GetIsPretty();

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

            var isPretty = context.GetIsPretty();

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

            var isPretty = context.GetIsPretty();

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToTrueInCaps_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_PRETTY", "True");

            var isPretty = context.GetIsPretty();

            Assert.True(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToFalseInCaps_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_PRETTY", "False");

            var isPretty = context.GetIsPretty();

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

            var isPretty = context.GetIsPretty();

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationUnset_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();

            var isPretty = context.GetIsPretty();

            Assert.False(isPretty);
        }
    }
}
