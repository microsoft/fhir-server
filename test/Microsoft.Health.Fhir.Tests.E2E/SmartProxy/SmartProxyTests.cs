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
using Newtonsoft.Json.Linq;
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

            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--incognito");

            options.AcceptInsecureCertificates = true;

            FhirResponse<Patient> response = await _fixture.FhirClient.CreateAsync(Samples.GetDefaultPatient());
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Patient patient = response.Resource;

            // VSTS Hosted agents set the ChromeWebDriver Env, locally that is not the case
            // https://docs.microsoft.com/en-us/azure/devops/pipelines/test/continuous-test-selenium?view=vsts#decide-how-you-will-deploy-and-test-your-app
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ChromeWebDriver")))
            {
                Environment.SetEnvironmentVariable("ChromeWebDriver", Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
            }

            using (var driver = new ChromeDriver(Environment.GetEnvironmentVariable("ChromeWebDriver"), options))
            {
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(20);

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

                int waitCount = 0;
                while (!driver.Url.StartsWith($"https://login.microsoftonline.com"))
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(1000));
                    Assert.InRange(waitCount++, 0, 10);
                }

                driver.FindElementByName("loginfmt").SendKeys(testUserName);
                Advance();

                driver.FindElementByName("passwd").SendKeys(testUserPassword);
                Advance();

                // Consent, should only be done if we can find the button
                try
                {
                    var button = driver.FindElementById("idSIButton9");
                    Advance();
                }
                catch (NoSuchElementException)
                {
                    // Nothing to do, we are assuming that we are at the SMART App screen.
                }

                var tokenResponseElement = driver.FindElement(By.Id("tokenresponsefield"));
                var tokenResponseText = tokenResponseElement.GetAttribute("value");

                // It can take some time for the token to apear, we will wait
                waitCount = 0;
                while (string.IsNullOrWhiteSpace(tokenResponseText))
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(1000));
                    tokenResponseText = tokenResponseElement.GetAttribute("value");
                    Assert.InRange(waitCount++, 0, 10);
                }

                var tokenResponse = JObject.Parse(tokenResponseElement.GetAttribute("value"));
                var jwtHandler = new JwtSecurityTokenHandler();
                Assert.True(jwtHandler.CanReadToken(tokenResponse["access_token"].ToString()));
                var token = jwtHandler.ReadJwtToken(tokenResponse["access_token"].ToString());
                var aud = token.Claims.Where(c => c.Type == "aud");
                Assert.Single(aud);
                var tokenAudience = aud.First().Value;
                Assert.Equal(Environment.GetEnvironmentVariable("TestEnvironmentUrl"), tokenAudience);
            }
        }
    }
}