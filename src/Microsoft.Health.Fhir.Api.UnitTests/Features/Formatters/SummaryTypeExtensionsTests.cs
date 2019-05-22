// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Stu3.Api.Features.Formatters;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Formatters
{
    public class SummaryTypeExtensionsTests
    {
        private readonly ILogger<string> _logger;

        public SummaryTypeExtensionsTests()
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
        public void GivenARequestWitNoSummaryType_WhenSerializingTheResponse_ThenFalseIsReturned()
        {
            var context = new DefaultHttpContext();

            var summary = context.GetSummaryType(_logger);

            Assert.Equal(SummaryType.False, summary);
        }
    }
}
