// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Api.Extensions;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Api.UnitTests.Extensions
{
    public class HttpStatusCodeExtensionsTests
    {
        [Theory]
        [InlineData(HttpStatusCode.Continue, "1xx")]
        [InlineData(HttpStatusCode.SwitchingProtocols, "1xx")]
        [InlineData(HttpStatusCode.OK, "2xx")]
        [InlineData(HttpStatusCode.Created, "2xx")]
        [InlineData(HttpStatusCode.Ambiguous, "3xx")]
        [InlineData(HttpStatusCode.Moved, "3xx")]
        [InlineData(HttpStatusCode.BadRequest, "4xx")]
        [InlineData(HttpStatusCode.NotFound, "4xx")]
        [InlineData(HttpStatusCode.InternalServerError, "5xx")]
        [InlineData(HttpStatusCode.BadGateway, "5xx")]
        public void StatusCode_ToStatusCodeClass_Succeeds(HttpStatusCode statusCode, string expectedClass)
        {
            Assert.Equal(expectedClass, statusCode.ToStatusCodeClass());
        }

        [Theory]
        [InlineData((HttpStatusCode)600, "6xx")]
        [InlineData((HttpStatusCode)int.MaxValue, "21474836xx")]
        [InlineData((HttpStatusCode)int.MinValue, "-21474836xx")]
        [InlineData((HttpStatusCode)0, "0xx")]
        [InlineData((HttpStatusCode)1, "0xx")]
        [InlineData((HttpStatusCode)(-200), "-2xx")]
        public void UnmappableStatusCode_Succeeds(HttpStatusCode input, string expectedClass)
        {
            Assert.Equal(expectedClass, input.ToStatusCodeClass());
        }
    }
}
