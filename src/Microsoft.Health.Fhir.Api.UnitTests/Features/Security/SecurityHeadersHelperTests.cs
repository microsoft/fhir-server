// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Api.Features.Security;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Security
{
    public class SecurityHeadersHelperTests
    {
        [Fact]
        public async void WhenSettingSecurityHeaders_WhenGivenANullContext_ThenExceptionIsThrown()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(async () => await SecurityHeadersHelper.SetSecurityHeaders(null));
        }

        [Fact]
        public async void WhenSettingSecurityHeaders_WhenGivenAnIncorrectType_ThenExceptionIsThrown()
        {
            int notAContext = 1;

            await Assert.ThrowsAsync<ArgumentException>(async () => await SecurityHeadersHelper.SetSecurityHeaders(notAContext));
        }

        [Fact]
        public async void WhenSettingSecurityHeaders_WhenGivenAcontext_TheXContentTypeOptionsHeaderIsSet()
        {
            var defaultHttpContext = new DefaultHttpContext();
            await SecurityHeadersHelper.SetSecurityHeaders(defaultHttpContext);

            Assert.NotNull(defaultHttpContext.Response.Headers);
            Assert.NotEmpty(defaultHttpContext.Response.Headers);
            Assert.True(defaultHttpContext.Response.Headers.TryGetValue("X-Content-Type-Options", out StringValues headerValue));
            Assert.Equal("nosniff", headerValue);
        }
    }
}
