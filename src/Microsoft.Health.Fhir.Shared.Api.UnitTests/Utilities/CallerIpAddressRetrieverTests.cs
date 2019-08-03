// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Utilities;
using Microsoft.Health.Fhir.Core.Features.Context;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Utilities
{
    public class CallerIpAddressRetrieverTests
    {
        private static readonly IPAddress CallerIpAddress = new IPAddress(new byte[] { 0xA, 0x0, 0x0, 0x0 }); // 10.0.0.0
        private const string ForwardedIpAddress = "10.0.0.1";

        private readonly IFhirRequestContextAccessor _fhirRequestContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        private readonly IHttpContextAccessor _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        private readonly CallerIpAddressRetriever _callerIpAddressRetriever;

        private readonly IFhirRequestContext _fhirRequestContext = Substitute.For<IFhirRequestContext>();
        private readonly DefaultHttpContext _httpContext = new DefaultHttpContext();

        public CallerIpAddressRetrieverTests()
        {
            _fhirRequestContextAccessor.FhirRequestContext.Returns(_fhirRequestContext);

            _httpContext.Connection.RemoteIpAddress = CallerIpAddress;

            _httpContextAccessor.HttpContext.Returns(_httpContext);

            _callerIpAddressRetriever = new CallerIpAddressRetriever(_fhirRequestContextAccessor, _httpContextAccessor);
        }

        [Fact]
        public void GivenThereIsForwardedHeader_WhenRetrieved_ThenForwardedHeaderValueShouldBeReturned()
        {
            var requestHeaders = new Dictionary<string, StringValues>()
            {
                { "X-Forwarded-For", ForwardedIpAddress },
            };

            _fhirRequestContext.RequestHeaders.Returns(requestHeaders);

            Assert.Equal(ForwardedIpAddress, _callerIpAddressRetriever.CallerIpAddress);
        }

        [Fact]
        public void GivenThereIsNoForwardedHeader_WhenRetrieved_ThenRemoteIpAddressSouldBeReturned()
        {
            Assert.Equal("10.0.0.0", _callerIpAddressRetriever.CallerIpAddress);
        }
    }
}
