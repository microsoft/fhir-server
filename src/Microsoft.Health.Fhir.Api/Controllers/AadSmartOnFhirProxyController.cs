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
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Api.Features.Exceptions;
using Microsoft.Health.Fhir.Api.Features.Filters;
using Microsoft.Health.Fhir.Api.Features.Routing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Api.Controllers
{
    /// <summary>
    /// Controller class enabling Azure Active Directory SMART on FHIR Proxy Capability
    /// </summary>
    [ServiceFilter(typeof(AadSmartOnFhirProxyAuditLoggingFilterAttribute))]
    [TypeFilter(typeof(AadSmartOnFhirProxyExceptionFilterAttribute))]
    [Route("AadSmartOnFhirProxy")]
    public class AadSmartOnFhirProxyController : Controller
    {
        private readonly bool _isAadV2;
        private readonly ILogger<SecurityConfiguration> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _aadAuthorizeEndpoint;
        private readonly string _aadTokenEndpoint;
        private readonly IUrlResolver _urlResolver;

        // TODO: _launchContextFields contain a list of fields that we will transmit as part of launch context, should be configurable
        private readonly string[] _launchContextFields = { "patient", "encounter", "practitioner", "need_patient_banner", "smart_style_url" };

        /// <summary>
        /// Initializes a new instance of the <see cref="AadSmartOnFhirProxyController" /> class.
        /// </summary>
        /// <param name="securityConfigurationOptions">Security configuration parameters.</param>
        /// <param name="httpClientFactory">HTTP Client Factory.</param>
        /// <param name="urlResolver">The URL resolver.</param>
        /// <param name="logger">The logger.</param>
        public AadSmartOnFhirProxyController(IOptions<SecurityConfiguration> securityConfigurationOptions, IHttpClientFactory httpClientFactory, IUrlResolver urlResolver, ILogger<SecurityConfiguration> logger)
        {
            EnsureArg.IsNotNull(securityConfigurationOptions?.Value, nameof(securityConfigurationOptions));
            EnsureArg.IsNotNull(httpClientFactory, nameof(httpClientFactory));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            SecurityConfiguration securityConfiguration = securityConfigurationOptions.Value;
            _isAadV2 = new Uri(securityConfiguration.Authentication.Authority).Segments.Contains("v2.0");
            _httpClientFactory = httpClientFactory;
            _urlResolver = urlResolver;
            _logger = logger;

            var openIdConfigurationUrl = $"{securityConfiguration.Authentication.Authority}/.well-known/openid-configuration";

            HttpResponseMessage openIdConfigurationResponse;
            var httpClient = httpClientFactory.CreateClient();

            try
            {
                openIdConfigurationResponse = httpClient.GetAsync(new Uri(openIdConfigurationUrl)).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                if (ex is HttpRequestException || ex is OperationCanceledException)
                {
                    logger.LogWarning(ex, $"There was an exception while attempting to read the OpenId Configuration from \"{openIdConfigurationUrl}\".");
                    throw new OpenIdConfigurationException();
                }

                throw;
            }

            openIdConfigurationResponse.EnsureSuccessStatusCode();

            var openIdConfiguration = JObject.Parse(openIdConfigurationResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            _aadTokenEndpoint = openIdConfiguration["token_endpoint"]?.Value<string>();
            _aadAuthorizeEndpoint = openIdConfiguration["authorization_endpoint"]?.Value<string>();

            if (_aadTokenEndpoint == null || _aadAuthorizeEndpoint == null)
            {
                logger.LogError($"There was an error attempting to read the endpoints from \"{openIdConfigurationUrl}\".");
                throw new OpenIdConfigurationException();
            }
        }

        /// <summary>
        /// Redirects request to the Azure AD authorize endpoint with adjusted parameters.
        /// </summary>
        /// <param name="responseType">response_type URL parameter.</param>
        /// <param name="clientId">client_id URL parameter.</param>
        /// <param name="redirectUri">redirect_uri URL parameter.</param>
        /// <param name="launch">launch (launch context)URL parameter.</param>
        /// <param name="scope">scope URL parameter.</param>
        /// <param name="state">state URL parameter.</param>
        /// <param name="aud">aud (audience) URL parameter.</param>
        [HttpGet]
        [AuditEventType(AuditEventSubType.SmartOnFhirAuthorize)]
        [Route("authorize", Name = RouteNames.AadSmartOnFhirProxyAuthorize)]
        public ActionResult Authorize(
            [FromQuery(Name = "response_type")] string responseType,
            [FromQuery(Name = "client_id")] string clientId,
            [FromQuery(Name = "redirect_uri")] Uri redirectUri,
            [FromQuery(Name = "launch")] string launch,
            [FromQuery(Name = "scope")] string scope,
            [FromQuery(Name = "state")] string state,
            [FromQuery(Name = "aud")] string aud)
        {
            if (string.IsNullOrEmpty(launch))
            {
                launch = Base64UrlEncoder.Encode("{}");
            }

            var newStateObj = new JObject
            {
                { "s", state },
                { "l", launch },
            };

            var queryBuilder = new QueryBuilder();

            if (!string.IsNullOrEmpty(responseType))
            {
                queryBuilder.Add("response_type", responseType);
            }

            if (!string.IsNullOrEmpty(clientId))
            {
                queryBuilder.Add("client_id", clientId);
            }

            try
            {
                var callbackUrl = _urlResolver.ResolveRouteNameUrl(RouteNames.AadSmartOnFhirProxyCallback, new RouteValueDictionary { { "encodedRedirect", Base64UrlEncoder.Encode(redirectUri.ToString()) } });
                queryBuilder.Add("redirect_uri", callbackUrl.AbsoluteUri);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Redirect URL passed to Authorize failed to resolve.");
            }

            if (!_isAadV2 && !string.IsNullOrEmpty(aud))
            {
                queryBuilder.Add("resource", aud);
            }
            else if (!string.IsNullOrEmpty(scope))
            {
                // Azure AD v2.0 uses fully qualified scopes and does not allow '/' (slash)
                // We add qualification to scopes and replace '/' -> '$'

                var scopes = scope.Split(' ');
                var scopesBuilder = new StringBuilder();
                string[] wellKnownScopes = { "profile", "openid", "email", "offline_access" };

                foreach (var s in scopes)
                {
                    if (wellKnownScopes.Contains(s))
                    {
                        scopesBuilder.Append($"{s} ");
                    }
                    else
                    {
                        scopesBuilder.Append($"{aud}/{s.Replace('/', '$')} ");
                    }
                }

                var newScopes = scopesBuilder.ToString().TrimEnd(' ');

                queryBuilder.Add("scope", Uri.EscapeDataString(newScopes));
            }

            if (!string.IsNullOrEmpty(state))
            {
                string newState = Base64UrlEncoder.Encode(newStateObj.ToString());
                queryBuilder.Add("state", newState);
            }

            return Redirect($"{_aadAuthorizeEndpoint}{queryBuilder}");
        }

        /// <summary>
        /// Callback function for receiving code from AAD
        /// </summary>
        /// <param name="encodedRedirect">Base64url encoded redirect URL on the app.</param>
        /// <param name="code">Authorization code.</param>
        /// <param name="state">state URL parameter.</param>
        /// <param name="sessionState">session_state URL parameter.</param>
        /// <param name="error">error URL parameter.</param>
        /// <param name="errorDescription">error_description URL parameter.</param>
        [HttpGet]
        [AuditEventType(AuditEventSubType.SmartOnFhirCallback)]
        [Route("callback/{encodedRedirect}", Name = RouteNames.AadSmartOnFhirProxyCallback)]
        public ActionResult Callback(
            string encodedRedirect,
            [FromQuery(Name = "code")] string code,
            [FromQuery(Name = "state")] string state,
            [FromQuery(Name = "session_state")] string sessionState,
            [FromQuery(Name = "error")] string error,
            [FromQuery(Name = "error_description")] string errorDescription)
        {
            Uri redirectUrl = null;

            try
            {
                redirectUrl = new Uri(Base64UrlEncoder.Decode(encodedRedirect));
            }
            catch (FormatException ex)
            {
                throw new AadSmartOnFhirProxyBadRequestException(string.Format(Resources.InvalidRedirectUri, redirectUrl), ex);
            }

            if (!string.IsNullOrEmpty(error))
            {
                var errorQueryBuilder = new QueryBuilder
                {
                    { "error", error },
                };

                if (!string.IsNullOrEmpty(errorDescription))
                {
                    errorQueryBuilder.Add("error_description", errorDescription);
                }

                return Redirect($"{redirectUrl}{errorQueryBuilder}");
            }

            string compoundCode;
            string newState;
            try
            {
                var launchStateParameters = JObject.Parse(Base64UrlEncoder.Decode(state));
                var launchParameters = JObject.Parse(Base64UrlEncoder.Decode(launchStateParameters["l"].ToString()));
                launchParameters.Add("code", code);
                newState = launchStateParameters["s"].ToString();
                compoundCode = Base64UrlEncoder.Encode(launchParameters.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error parsing launch parameters: {ex.Message}");
                throw new AadSmartOnFhirProxyBadRequestException(Resources.InvalidLaunchContext, ex);
            }

            var queryBuilder = new QueryBuilder
            {
                { "code", compoundCode },
                { "state", newState },
            };

            if (!string.IsNullOrEmpty(sessionState))
            {
                queryBuilder.Add("session_state", sessionState);
            }

            return Redirect($"{redirectUrl}{queryBuilder}");
        }

        /// <summary>
        /// Proxies a (POST) request to the AAD token endpoint
        /// </summary>
        /// <param name="grantType">grant_type request parameter.</param>
        /// <param name="compoundCode">The base64url encoded code and launch context</param>
        /// <param name="redirectUri">redirect_uri request parameter.</param>
        /// <param name="clientId">client_id request parameter.</param>
        /// <param name="clientSecret">client_secret request parameter.</param>
        [HttpPost]
        [AuditEventType(AuditEventSubType.SmartOnFhirToken)]
        [Route("token", Name = RouteNames.AadSmartOnFhirProxyToken)]
        public async Task<ActionResult> Token(
            [FromForm(Name = "grant_type")] string grantType,
            [FromForm(Name = "code")] string compoundCode,
            [FromForm(Name = "redirect_uri")] Uri redirectUri,
            [FromForm(Name = "client_id")] string clientId,
            [FromForm(Name = "client_secret")] string clientSecret)
        {
            try
            {
                EnsureArg.IsNotNull(grantType, nameof(grantType));
                EnsureArg.IsNotNull(clientId, nameof(clientId));
            }
            catch (ArgumentNullException ex)
            {
                throw new AadSmartOnFhirProxyBadRequestException(string.Format(Resources.ValueCannotBeNull, ex.ParamName), ex);
            }

            var client = _httpClientFactory.CreateClient();

            // Azure AD supports client_credentials, etc.
            // These are used in tests and may have value even when SMART proxy is used.
            // This prevents disabling those options.
            // TODO: This should probably removed.
            //       If somebody is accessing the IDP through this endpoint, all things not SMART on FHIR should be errors.
            //       For now, we will keep it to keep the E2E tests from failing.
            // TODO: Add handling of 'aud' -> 'resource', should that be an error or should translation be done?
            if (grantType != "authorization_code")
            {
                var fields = new List<KeyValuePair<string, string>>();
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

            try
            {
                EnsureArg.IsNotNull(compoundCode, nameof(compoundCode));
                EnsureArg.IsNotNull(redirectUri, nameof(redirectUri));
            }
            catch (ArgumentNullException ex)
            {
                throw new AadSmartOnFhirProxyBadRequestException(string.Format(Resources.ValueCannotBeNull, ex.ParamName), ex);
            }

            JObject decodedCompoundCode;
            string code;
            try
            {
                decodedCompoundCode = JObject.Parse(Base64UrlEncoder.Decode(compoundCode));
                code = decodedCompoundCode["code"].ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error decoding compound code: {ex.Message}");
                throw new AadSmartOnFhirProxyBadRequestException(Resources.InvalidCompoundCode, ex);
            }

            Uri callbackUrl = _urlResolver.ResolveRouteNameUrl(RouteNames.AadSmartOnFhirProxyCallback, new RouteValueDictionary { { "encodedRedirect", Base64UrlEncoder.Encode(redirectUri.ToString()) } });

            // TODO: Deal with client secret in basic auth header
            var content = new FormUrlEncodedContent(
                new[]
                {
                    new KeyValuePair<string, string>("grant_type", grantType),
                    new KeyValuePair<string, string>("code", code),
                    new KeyValuePair<string, string>("redirect_uri", callbackUrl.AbsoluteUri),
                    new KeyValuePair<string, string>("client_id", clientId),
                    new KeyValuePair<string, string>("client_secret", clientSecret),
                });

            HttpResponseMessage response = await client.PostAsync(new Uri(_aadTokenEndpoint), content);

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

            // Handle fields passed through launch context
            foreach (var launchField in _launchContextFields)
            {
                if (decodedCompoundCode[launchField] != null)
                {
                    tokenResponse[launchField] = decodedCompoundCode[launchField];
                }
            }

            tokenResponse["client_id"] = clientId;

            // Replace fully qualifies scopes with short scopes and replace $
            string[] scopes = tokenResponse["scope"].ToString().Split(' ');
            var scopesBuilder = new StringBuilder();

            foreach (var s in scopes)
            {
                if (IsAbsoluteUrl(s))
                {
                    var scopeUri = new Uri(s);
                    scopesBuilder.Append($"{scopeUri.Segments.Last().Replace('$', '/')} ");
                }
                else
                {
                    scopesBuilder.Append($"{s.Replace('$', '/')} ");
                }
            }

            tokenResponse["scope"] = scopesBuilder.ToString().TrimEnd(' ');

            return new ContentResult()
            {
                Content = tokenResponse.ToString(Newtonsoft.Json.Formatting.None),
                StatusCode = (int)response.StatusCode,
                ContentType = "application/json",
            };
        }

        private static bool IsAbsoluteUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out _);
        }
    }
}
