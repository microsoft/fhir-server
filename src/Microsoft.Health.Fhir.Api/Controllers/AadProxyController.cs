// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    [Route("/AadProxy")]
    public class AadProxyController : Controller
    {
        private readonly SecurityConfiguration _securityConfiguration;
        private readonly bool _isAadV2;
        private readonly ILogger<SecurityConfiguration> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _aadAuthorizeEndpoint;
        private readonly string _aadTokenEndpoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="AadProxyController" /> class.
        /// </summary>
        /// <param name="securityConfiguration">Security configuration parameters.</param>
        /// <param name="httpClientFactory">HTTP Client Factory.</param>
        /// <param name="logger">The logger.</param>
        public AadProxyController(IOptions<SecurityConfiguration> securityConfiguration, IHttpClientFactory httpClientFactory, ILogger<SecurityConfiguration> logger)
        {
            EnsureArg.IsNotNull(securityConfiguration, nameof(securityConfiguration));

            _securityConfiguration = securityConfiguration.Value;
            _isAadV2 = new Uri(_securityConfiguration.Authentication.Authority).Segments.Contains("v2.0");
            _logger = logger;
            _httpClientFactory = httpClientFactory;

            var openIdConfigurationUrl = $"{_securityConfiguration.Authentication.Authority}/.well-known/openid-configuration";

            HttpResponseMessage openIdConfigurationResponse;
            using (var httpClient = httpClientFactory.CreateClient())
            {
                try
                {
                    openIdConfigurationResponse = httpClient.GetAsync(new Uri(openIdConfigurationUrl)).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, $"There was an exception while attempting to read the OpenId Configuration from \"{openIdConfigurationUrl}\".");
                    throw new OpenIdConfigurationException();
                }
            }

            if (openIdConfigurationResponse.IsSuccessStatusCode)
            {
                var openIdConfiguration = JObject.Parse(openIdConfigurationResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                try
                {
                    _aadTokenEndpoint = openIdConfiguration["token_endpoint"].Value<string>();
                    _aadAuthorizeEndpoint = openIdConfiguration["authorization_endpoint"].Value<string>();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, $"There was an exception while attempting to read the endpoints from \"{openIdConfigurationUrl}\".");
                    throw new OpenIdConfigurationException();
                }
            }
        }

        /// <summary>
        /// Proxies a request to the Azure AD authorize endpoint.
        /// </summary>
        /// <param name="responseType">response_type URL parameter.</param>
        /// <param name="clientId">client_id URL parameter.</param>
        /// <param name="redirectUri">redirect_uri URL parameter.</param>
        /// <param name="launch">launch (launch context)URL parameter.</param>
        /// <param name="scope">scope URL parameter.</param>
        /// <param name="state">state URL parameter.</param>
        /// <param name="aud">aud (audience) URL parameter.</param>
        [HttpGet("authorize")]
        public ActionResult Authorize(
            [FromQuery(Name = "response_type")] string responseType,
            [FromQuery(Name = "client_id")] string clientId,
            [FromQuery(Name = "redirect_uri")] Uri redirectUri,
            [FromQuery(Name = "launch")] string launch,
            [FromQuery(Name = "scope")] string scope,
            [FromQuery(Name = "state")] string state,
            [FromQuery(Name = "aud")] string aud)
        {
            EnsureArg.IsNotNull(responseType, nameof(responseType));
            EnsureArg.IsNotNull(clientId, nameof(clientId));
            EnsureArg.IsNotNull(redirectUri, nameof(redirectUri));
            EnsureArg.IsNotNull(aud, nameof(aud));

            if (string.IsNullOrEmpty(launch))
            {
                launch = Base64Encode("{}");
            }

            Uri callbackUrl = new Uri(
                Request.Scheme + "://" + Request.Host + "/AadProxy/callback/" +
                Uri.EscapeDataString(Base64Encode(redirectUri.ToString())) + "/" + Uri.EscapeDataString(launch));

            string newQueryString = $"response_type={responseType}&redirect_uri={callbackUrl.ToString()}&client_id={clientId}";
            if (!_isAadV2)
            {
                newQueryString += $"&resource={aud}";
            }
            else
            {
                // Azure AD v2.0 uses fully qualified scopes and does not allow '/' (slash)
                // We add qualification to scopes and replace '/' -> '$'

                EnsureArg.IsNotNull(scope, nameof(scope));
                var scopes = scope.Split(' ');
                string newScopes = string.Empty;
                string[] wellKnownScopes = { "profile", "openid", "email", "offline_access" };

                foreach (var s in scopes)
                {
                    if (wellKnownScopes.Contains(s))
                    {
                        newScopes += $"{s} ";
                    }
                    else
                    {
                        newScopes += $"{aud}/{s.Replace('/', '$')} ";
                    }
                }

                newScopes = newScopes.TrimEnd(' ');
                newQueryString += $"&scope={Uri.EscapeDataString(newScopes)}";
            }

            if (!string.IsNullOrEmpty(state))
            {
                newQueryString += $"&state={state}";
            }

            return RedirectPermanent($"{_aadAuthorizeEndpoint}?{newQueryString}");
        }

        /// <summary>
        /// Callback function for receiving code from AAD
        /// </summary>
        /// <param name="encodedRedirect">Base64 encoded redirect URL on the app.</param>
        /// <param name="launchContext">The base64 encoded launch context</param>
        /// <param name="code">Authorization code.</param>
        /// <param name="state">state URL parameter.</param>
        /// <param name="sessionState">session_state URL parameter.</param>
        /// <param name="error">error URL parameter.</param>
        /// <param name="errorDescription">error_description URL parameter.</param>
        [HttpGet("callback/{encodedRedirect}/{launchContext}")]
        public ActionResult Callback(
            string encodedRedirect,
            string launchContext,
            [FromQuery(Name = "code")] string code,
            [FromQuery(Name = "state")] string state,
            [FromQuery(Name = "session_state")] string sessionState,
            [FromQuery(Name = "error")] string error,
            [FromQuery(Name = "error_description")] string errorDescription)
        {
            Uri redirectUrl = new Uri(Base64Decode(encodedRedirect));

            if (!string.IsNullOrEmpty(error))
            {
                return RedirectPermanent($"{redirectUrl.ToString()}?error={error}&error_description={errorDescription}");
            }

            string compoundCode;
            try
            {
                JObject launchParameters = JObject.Parse(Base64Decode(launchContext));
                launchParameters.Add("code", code);
                compoundCode = Uri.EscapeDataString(Base64Encode(launchParameters.ToString(Newtonsoft.Json.Formatting.None)));
            }
            catch
            {
                _logger.LogError("Error parsing launch parameters.");
                throw;
            }

            return RedirectPermanent($"{redirectUrl.ToString()}?code={compoundCode}&state={state}&session_state={sessionState}");
        }

        /// <summary>
        /// Proxies a (POST) request to the AAD token endpoint
        /// </summary>
        /// <param name="grantType">grant_type request parameter.</param>
        /// <param name="compoundCode">The base64 encoded code and launch context</param>
        /// <param name="redirectUri">redirect_uri request parameter.</param>
        /// <param name="clientId">client_id request parameter.</param>
        /// <param name="clientSecret">client_secret request parameter.</param>
        [HttpPost("token")]
        public async Task<ActionResult> Token(
            [FromForm(Name = "grant_type")] string grantType,
            [FromForm(Name = "code")] string compoundCode,
            [FromForm(Name = "redirect_uri")] Uri redirectUri,
            [FromForm(Name = "client_id")] string clientId,
            [FromForm(Name = "client_secret")] string clientSecret)
        {
            EnsureArg.IsNotNull(grantType, nameof(grantType));
            EnsureArg.IsNotNull(clientId, nameof(clientId));

            var client = _httpClientFactory.CreateClient();

            // TODO: This hack should be handled more generically
            if (grantType != "authorization_code")
            {
                List<KeyValuePair<string, string>> fields = new List<KeyValuePair<string, string>>();
                foreach (var f in Request.Form)
                {
                    fields.Add(new KeyValuePair<string, string>(f.Key, f.Value));
                }

                var passThroughContent = new FormUrlEncodedContent(fields);

                var passThroughResponse = await client.PostAsync(new Uri(_aadTokenEndpoint), passThroughContent);

                return new ContentResult()
                {
                    Content = await passThroughResponse.Content.ReadAsStringAsync(),
                    StatusCode = (int)passThroughResponse.StatusCode,
                    ContentType = "application/json",
                };
            }

            EnsureArg.IsNotNull(compoundCode, nameof(compoundCode));
            EnsureArg.IsNotNull(redirectUri, nameof(redirectUri));

            JObject decodedCompoundCode;
            string code;
            string launch;

            try
            {
                decodedCompoundCode = JObject.Parse(Base64Decode(compoundCode));
                code = decodedCompoundCode["code"].ToString();
                decodedCompoundCode.Remove("code");
                launch = Base64Encode(decodedCompoundCode.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch
            {
                _logger.LogError("Error decoding compound code");
                throw;
            }

            Uri callbackUrl = new Uri(
                Request.Scheme + "://" + Request.Host + "/AadProxy/callback/" +
                Base64Encode(redirectUri.ToString()) + "/" + launch);

            // TODO: Deal with client secret in basic auth header

            var content = new FormUrlEncodedContent(
                new[]
                {
                    new KeyValuePair<string, string>("grant_type", grantType),
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("redirect_uri", callbackUrl.ToString()),
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                });

            var response = await client.PostAsync(new Uri(_aadTokenEndpoint), content);

            if (!response.IsSuccessStatusCode)
            {
                return new ContentResult()
                {
                    Content = await response.Content.ReadAsStringAsync(),
                    StatusCode = (int)response.StatusCode,
                    ContentType = "application/json",
                };
            }

            var tokenResponse = JObject.Parse(await response.Content.ReadAsStringAsync());

            if (decodedCompoundCode["patient"] != null)
            {
                tokenResponse["patient"] = decodedCompoundCode["patient"];
            }

            if (decodedCompoundCode["encounter"] != null)
            {
                tokenResponse["encounter"] = decodedCompoundCode["encounter"];
            }

            if (decodedCompoundCode["practitioner"] != null)
            {
                tokenResponse["practitoner"] = decodedCompoundCode["practitioner"];
            }

            if (decodedCompoundCode["need_patient_banner"] != null)
            {
                tokenResponse["need_patient_banner"] = decodedCompoundCode["need_patient_banner"];
            }

            if (decodedCompoundCode["smart_style_url"] != null)
            {
                tokenResponse["smart_style_url"] = decodedCompoundCode["smart_style_url"];
            }

            tokenResponse["client_id"] = clientId;

            // Replace fully qualifies scopes with short scopes and replace $
            string[] scopes = tokenResponse["scope"].ToString().Split(' ');
            string newScopes = string.Empty;

            foreach (var s in scopes)
            {
                if (IsAbsoluteUrl(s))
                {
                    Uri scopeUri = new Uri(s);
                    newScopes += $"{scopeUri.Segments.Last().Replace('$', '/')} ";
                }
                else
                {
                    newScopes += $"{s.Replace('$', '/')} ";
                }
            }

            newScopes = newScopes.TrimEnd(' ');
            tokenResponse["scope"] = newScopes;

            return new ContentResult()
            {
                Content = tokenResponse.ToString(Newtonsoft.Json.Formatting.None),
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json",
            };
        }

        private static string Base64Encode(string plainText)
        {
            var plainTextBytes = System.Text.Encoding.UTF8.GetBytes(plainText);
            return System.Convert.ToBase64String(plainTextBytes);
        }

        private static string Base64Decode(string base64EncodedData)
        {
            var base64EncodedBytes = System.Convert.FromBase64String(base64EncodedData);
            return System.Text.Encoding.UTF8.GetString(base64EncodedBytes);
        }

        private static bool IsAbsoluteUrl(string url)
        {
            Uri result;
            return Uri.TryCreate(url, UriKind.Absolute, out result);
        }
    }
}