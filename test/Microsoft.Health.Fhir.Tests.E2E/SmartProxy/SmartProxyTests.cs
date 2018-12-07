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
using Xunit.Abstractions;

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
        public async Task SmartLauncherShouldBeRunningAndResponding()
        {
            var client = new HttpClient();
            var result = await client.GetAsync(_fixture.SmartLauncherUrl);
            Assert.True(result.IsSuccessStatusCode);
        }

        [Fact]
        public void SmartLauncherWillInitiateLaunchSequenceAndSignIn()
        {
            ChromeOptions options = new ChromeOptions();

            // options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--incognito");

            using (var driver = new ChromeDriver(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), options))
            {
                /*
                void Advance()
                {
                    while (true)
                    {
                        try
                        {
                            var button = driver.FindElementById("idSIButton9");
                            if (button.Enabled)
                            {
                                button.Click();
                                return;
                            }
                        }
                        catch (StaleElementReferenceException)
                        {
                        }
                    }
                }
                */

                driver.Navigate().GoToUrl(_fixture.SmartLauncherUrl);

                while (!driver.Url.StartsWith(_fixture.SmartLauncherUrl))
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(100));
                }

                var fhirUrlElement = driver.FindElement(By.Id("fhirurl"));
                Assert.Equal(Environment.GetEnvironmentVariable("FhirServerUrl"), fhirUrlElement.GetAttribute("value"));

                /*
                driver.SwitchTo().ActiveElement().SendKeys(_config["TestUserName"]);
                Advance();

                driver.FindElementByName("passwd").SendKeys(_config["TestUserPassword"]);
                Advance();

                driver.Navigate().GoToUrl($"https://localhost:{_server.Port}/Home/About");

                while (!driver.Url.StartsWith($"https://localhost:{_server.Port}/Home/About"))
                {

                    Thread.Sleep(TimeSpan.FromMilliseconds(100));

                }

                var element = driver.FindElement(By.Id("tokenfield"));
                String elementval = element.GetAttribute("value");

                var jwtHandler = new JwtSecurityTokenHandler();

                Assert.True(jwtHandler.CanReadToken(elementval));

                var token = jwtHandler.ReadJwtToken(elementval);
                var aud = token.Claims.Where(c => c.Type == "aud");

                Assert.Single(aud);

                var tokenAudience = aud.First().Value;

                Assert.Equal(_config["FhirServerUrl"], tokenAudience);
                */
            }
        }
    }
}