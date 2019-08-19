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
    public class HttpExtensionsTests
    {
        private readonly ILogger<string> _logger;

        public HttpExtensionsTests()
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

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToTrue_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "true");

            var isPretty = context.GetIsPretty(_logger);

            Assert.True(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToFalse_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "false");

            var isPretty = context.GetIsPretty(_logger);

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToTrueInCaps_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_PRETTY", "TRUE");

            var isPretty = context.GetIsPretty(_logger);

            Assert.True(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToFalseInCaps_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_PRETTY", "FALSE");

            var isPretty = context.GetIsPretty(_logger);

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToTrueInPascalCase_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "True");

            var isPretty = context.GetIsPretty(_logger);

            Assert.True(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToFalseInPascalCase_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "False");

            var isPretty = context.GetIsPretty(_logger);

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToTrueWithAddedWhiteSpace_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "        true   ");

            var isPretty = context.GetIsPretty(_logger);

            Assert.True(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToUnrecognizableInput_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "abc");

            var isPretty = context.GetIsPretty(_logger);

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationUnset_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();

            var isPretty = context.GetIsPretty(_logger);

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToZero_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "0");

            var isPretty = context.GetIsPretty(_logger);

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenARequestWithPrettyIndentationSetToOne_WhenSerializingTheResponse_ThenPrettyIndentationIsNotApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "1");

            var isPretty = context.GetIsPretty(_logger);

            Assert.False(isPretty);
        }

        [Fact]
        public void GivenAnXmlRequestWithPrettyIndentationSetToTrue_WhenSerializingTheResponse_ThenPrettyIndentationIsApplied()
        {
            var context = new DefaultHttpContext();
            context.Request.QueryString = QueryString.Create("_pretty", "true");
            context.Request.QueryString.Add("_format", "xml");

            var isPretty = context.GetIsPretty(_logger);

            Assert.True(isPretty);
        }
    }
}
