// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Xunit;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

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
        public async Task SmartLauncherWillInitiateLaunchSequenceAndSignInAsync()
        {
            ChromeOptions options = new ChromeOptions();

            // options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--incognito");

            FhirResponse<Patient> response = await _fixture.FhirClient.CreateAsync(Samples.GetDefaultPatient());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Patient patient = response.Resource;

            using (var driver = new ChromeDriver(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), options))
            {
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

                driver.Navigate().GoToUrl(_fixture.SmartLauncherUrl);

                var patientElement = driver.FindElement(By.Id("patient"));
                patientElement.SendKeys(patient.Id);

                var launchButton = driver.FindElement(By.Id("launchButton"));
                if (launchButton.Enabled)
                {
                    launchButton.Click();
                }

                var testUserName = TestUsers.AdminUser.UserId;
                var testUserPassword = TestUsers.AdminUser.Password;

                // Launcher opens a new tab, switch to it
                driver.SwitchTo().Window(driver.WindowHandles[1]);

                var driverUrl = driver.Url;
                while (!driver.Url.StartsWith($"https://login.microsoftonline.com"))
                {
                    driverUrl = driver.Url;
                    Thread.Sleep(TimeSpan.FromMilliseconds(100));
                }

                driver.SwitchTo().ActiveElement().SendKeys(testUserName);
                Advance();

                driver.FindElementByName("passwd").SendKeys(testUserPassword);
                Advance();

                /*
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