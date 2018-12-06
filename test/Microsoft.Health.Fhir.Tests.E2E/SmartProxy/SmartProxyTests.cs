// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.SmartProxy
{
    public class SmartProxyTests : IClassFixture<SmartProxyTestFixture>
    {
        private SmartProxyTestFixture _fixture;

        public SmartProxyTests(SmartProxyTestFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task SmartLauncherShouldBeRunningAndResponsing()
        {
            var client = new HttpClient();

            var result = await client.GetAsync($"https://localhost:{_fixture.Port}/");

            Assert.True(result.IsSuccessStatusCode);
        }
    }
}