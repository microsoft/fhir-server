// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using Xunit;
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
        public async Task GivenPatientIdAndSmartAppUrl_WhenLaunchingApp_LaunchSequenceAndSignIn()
        {
            // There is no remote FHIR server. Skip test
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TestEnvironmentUrl")))
            {
                return;
            }

            var options = new ChromeOptions();

            options.AddArgument("--headless");
            options.AddArgument("--disable-gpu");
            options.AddArgument("--incognito");

            // TODO: We are accepting insecure certs to make it practical to run on build systems. A valid cert should be on the build system.
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
                // TODO: This parameter has been set (too) conservatively to ensure that content
                //       loads on build machines. Investigate if one could be less sensitive to that.
                driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(30);

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

                IWebElement loginElement = WaitForAndReturnElement(driver, "loginfmt", TimeSpan.FromSeconds(5), false);
                loginElement.SendKeys(testUserName);
                Advance();

                // We want to make sure the passwd element is available before we try to access it.
                IWebElement passwordElement = WaitForAndReturnElement(driver, "passwd", TimeSpan.FromSeconds(5), false);
                passwordElement.SendKeys(testUserPassword);
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

                IWebElement tokenResponseElement = WaitForAndReturnElement(driver, "tokenresponsefield", TimeSpan.FromSeconds(10), true);
                var tokenResponseText = tokenResponseElement.GetAttribute("value");

                // Check the token response, should have right audience
                var tokenResponse = JObject.Parse(tokenResponseElement.GetAttribute("value"));
                var jwtHandler = new JwtSecurityTokenHandler();
                Assert.True(jwtHandler.CanReadToken(tokenResponse["access_token"].ToString()));
                var token = jwtHandler.ReadJwtToken(tokenResponse["access_token"].ToString());
                var aud = token.Claims.Where(c => c.Type == "aud").ToList();
                Assert.Single(aud);
                var tokenAudience = aud.First().Value;
                Assert.Equal(Environment.GetEnvironmentVariable("TestEnvironmentUrl"), tokenAudience);

                // Check the patient
                var patientResponseElement = driver.FindElement(By.Id("patientfield"));
                var patientResource = JObject.Parse(patientResponseElement.GetAttribute("value"));
                Assert.Equal(patient.Id, patientResource["id"].ToString());
            }
        }

        private static IWebElement WaitForAndReturnElement(IWebDriver driver, string elementName, TimeSpan timeout, bool findElementById)
        {
            // We poll every 100ms to check whether the requested element is available or not.
            // If the element is still not available after "timeout" seconds, we throw a TimeoutException
            var driverWait = new WebDriverWait(driver, timeout)
            {
                PollingInterval = TimeSpan.FromMilliseconds(100),
            };

            // findElementById determines whether we search by Id or by Name.
            IWebElement element;
            if (findElementById)
            {
                element = driverWait.Until(d => d.FindElement(By.Id(elementName)));
            }
            else
            {
                element = driverWait.Until(d => d.FindElement(By.Name(elementName)));
            }

            return element;
        }
    }
}
