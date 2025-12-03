// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Health.Client.Authentication;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using NSubstitute;
using Polly;
using Polly.Retry;
using Task = System.Threading.Tasks.Task;
#if Stu3 || R4 || R4B
using RestfulCapabilityMode = Hl7.Fhir.Model.CapabilityStatement.RestfulCapabilityMode;
#endif

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Represents a FHIR server for end-to-end testing.
    /// Creates and caches <see cref="TestFhirClient"/> instances that target the server.
    /// </summary>
    public abstract class TestFhirServer : IAsyncDisposable
    {
        private readonly ConcurrentDictionary<(ResourceFormat format, TestApplication clientApplication, TestUser user), Lazy<TestFhirClient>> _cache = new ConcurrentDictionary<(ResourceFormat format, TestApplication clientApplication, TestUser user), Lazy<TestFhirClient>>();
        private readonly SessionTokenContainer _sessionTokenContainer = new SessionTokenContainer();

        private readonly Dictionary<string, AuthenticationHttpMessageHandler> _authenticationHandlers = new Dictionary<string, AuthenticationHttpMessageHandler>();

        protected TestFhirServer(Uri baseAddress)
        {
            EnsureArg.IsNotNull(baseAddress, nameof(baseAddress));

            BaseAddress = baseAddress;
        }

        protected internal bool SecurityEnabled { get; set; }

        protected internal Uri TokenUri { get; set; }

        protected internal Uri AuthorizeUri { get; set; }

        public Uri BaseAddress { get; }

        public ResourceElement Metadata { get; set; }

        public TestFhirClient GetTestFhirClient(ResourceFormat format, bool reusable = true, AuthenticationHttpMessageHandler authenticationHandler = null)
        {
            return GetTestFhirClient(format, TestApplications.GlobalAdminServicePrincipal, null, reusable, authenticationHandler);
        }

        public TestFhirClient GetTestFhirClient(ResourceFormat format, TestApplication clientApplication, TestUser user, bool reusable = true, AuthenticationHttpMessageHandler authenticationHandler = null)
        {
            if (!reusable)
            {
                return CreateFhirClient(format, clientApplication, user, authenticationHandler);
            }

            return _cache.GetOrAdd(
                    (format, clientApplication, user),
                    (tuple, fhirServer) =>
                        new Lazy<TestFhirClient>(() => CreateFhirClient(tuple.format, tuple.clientApplication, tuple.user, authenticationHandler)),
                    this)
                .Value;
        }

        private TestFhirClient CreateFhirClient(ResourceFormat format, TestApplication clientApplication, TestUser user, AuthenticationHttpMessageHandler authenticationHandler = null)
        {
            if (SecurityEnabled && authenticationHandler == null)
            {
                authenticationHandler = GetAuthenticationHandler(clientApplication, user);
            }

            HttpMessageHandler innerHandler = authenticationHandler ?? CreateMessageHandler();

            var sessionMessageHandler = new SessionMessageHandler(innerHandler, _sessionTokenContainer);

            var httpClient = new HttpClient(sessionMessageHandler) { BaseAddress = BaseAddress };

            return new TestFhirClient(httpClient, this, format, clientApplication, user);
        }

        private AuthenticationHttpMessageHandler GetAuthenticationHandler(TestApplication clientApplication, TestUser user)
        {
            if (clientApplication.Equals(TestApplications.InvalidClient))
            {
                return null;
            }

            string authDictionaryKey = GenerateDictionaryKey();

            if (_authenticationHandlers.ContainsKey(authDictionaryKey))
            {
                return _authenticationHandlers[authDictionaryKey];
            }

            var authHttpClient = new HttpClient(CreateMessageHandler())
            {
                BaseAddress = BaseAddress,
            };

            string scope = clientApplication.Equals(TestApplications.WrongAudienceClient) ? clientApplication.ClientId : AuthenticationSettings.Scope;
            string resource = clientApplication.Equals(TestApplications.WrongAudienceClient) ? clientApplication.ClientId : AuthenticationSettings.Resource;

            ICredentialProvider credentialProvider;
            if (user == null)
            {
                var credentialConfiguration = new OAuth2ClientCredentialOptions(
                    TokenUri,
                    resource,
                    scope,
                    clientApplication.ClientId,
                    clientApplication.ClientSecret);

                var optionsMonitor = Substitute.For<IOptionsMonitor<OAuth2ClientCredentialOptions>>();
                optionsMonitor.CurrentValue.Returns(credentialConfiguration);
                optionsMonitor.Get(default).ReturnsForAnyArgs(credentialConfiguration);

                credentialProvider = new OAuth2ClientCredentialProvider(optionsMonitor, authHttpClient);
            }
            else
            {
                var credentialConfiguration = new OAuth2UserPasswordCredentialOptions(
                    TokenUri,
                    resource,
                    scope,
                    clientApplication.ClientId,
                    clientApplication.ClientSecret,
                    user.UserId,
                    user.Password);

                var optionsMonitor = Substitute.For<IOptionsMonitor<OAuth2UserPasswordCredentialOptions>>();
                optionsMonitor.CurrentValue.Returns(credentialConfiguration);
                optionsMonitor.Get(default).ReturnsForAnyArgs(credentialConfiguration);

                credentialProvider = new OAuth2UserPasswordCredentialProvider(optionsMonitor, authHttpClient);
            }

            var authenticationHandler = new AuthenticationHttpMessageHandler(credentialProvider)
            {
                InnerHandler = CreateMessageHandler(),
            };
            _authenticationHandlers.Add(authDictionaryKey, authenticationHandler);

            return authenticationHandler;

            string GenerateDictionaryKey()
            {
                return $"{clientApplication.ClientId}:{user?.UserId}";
            }
        }

        internal abstract HttpMessageHandler CreateMessageHandler();

        /// <summary>
        /// Set the security options on the class.
        /// <remarks>Examines the metadata endpoint to determine if there's a token and authorize url exposed and sets the property <see cref="SecurityEnabled"/> to <value>true</value> or <value>false</value> based on this.
        /// Additionally, the <see cref="TokenUri"/> and <see cref="AuthorizeUri"/> is set if it they are found.</remarks>
        /// </summary>
        /// <param name="cancellationToken">The cancellation token</param>
        public async Task ConfigureSecurityOptions(CancellationToken cancellationToken = default)
        {
            bool localSecurityEnabled = false;

            using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, "metadata"));
            var httpClient = new HttpClient(CreateMessageHandler())
            {
                BaseAddress = BaseAddress,
            };
            HttpResponseMessage response = await httpClient.SendAsync(requestMessage, cancellationToken);

            string content = await response.Content.ReadAsStringAsync();
            response.EnsureSuccessStatusCode();

            CapabilityStatement metadata = new FhirJsonParser().Parse<CapabilityStatement>(content);
            Metadata = metadata.ToResourceElement();

#if Stu3 || R4 || R4B
            foreach (var rest in metadata.Rest.Where(r => r.Mode == RestfulCapabilityMode.Server))
#else
            foreach (var rest in metadata.Rest.Where(r => r.Mode == CapabilityStatement.RestfulCapabilityMode.Server))
#endif
            {
                var oauth = rest.Security?.GetExtension(Core.Features.Security.Constants.SmartOAuthUriExtension);
                if (oauth != null)
                {
                    var tokenUrl = oauth.GetExtensionValue<FhirUri>(Core.Features.Security.Constants.SmartOAuthUriExtensionToken).Value;
                    var authorizeUrl = oauth.GetExtensionValue<FhirUri>(Core.Features.Security.Constants.SmartOAuthUriExtensionAuthorize).Value;

                    localSecurityEnabled = true;
                    TokenUri = new Uri(tokenUrl);
                    AuthorizeUri = new Uri(authorizeUrl);

                    break;
                }
            }

            SecurityEnabled = localSecurityEnabled;
        }

        /// <summary>
        /// Clears the authentication handler cache and client cache.
        /// This is useful when retrying authentication after a failure.
        /// </summary>
        public void ClearAuthenticationCache()
        {
            _authenticationHandlers.Clear();
            _cache.Clear();
            Console.WriteLine($"[AuthValidation] Cleared authentication handler cache and client cache.");
        }

        /// <summary>
        /// Validates that authentication is working by making an authenticated request.
        /// Uses exponential backoff with a maximum wait time of approximately 2 minutes.
        /// Clears caches before each retry to avoid caching failed auth state.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>A task representing the async operation.</returns>
        public async Task ValidateAuthenticationAsync(CancellationToken cancellationToken = default)
        {
            if (!SecurityEnabled)
            {
                Console.WriteLine($"[AuthValidation] Security is not enabled, skipping authentication validation.");
                return;
            }

            const int maxRetries = 7;
            int[] backoffDelaysMs = { 1000, 2000, 4000, 8000, 16000, 32000, 64000 }; // ~127s total
            var stopwatch = Stopwatch.StartNew();

            Console.WriteLine($"[AuthValidation] Starting authentication validation with up to {maxRetries} attempts...");

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    Console.WriteLine($"[AuthValidation] Attempt {attempt}/{maxRetries} - Validating authentication...");

                    // Get a fresh client for validation
                    TestFhirClient client = GetTestFhirClient(ResourceFormat.Json, reusable: false);

                    // Make a simple authenticated request to validate auth is working
                    using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "Patient?_count=1");
                    using HttpResponseMessage response = await client.HttpClient.SendAsync(request, cancellationToken);

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        throw new HttpRequestException($"Authentication failed with status code {response.StatusCode}");
                    }

                    response.EnsureSuccessStatusCode();

                    Console.WriteLine($"[AuthValidation] Authentication validated successfully on attempt {attempt} after {stopwatch.ElapsedMilliseconds}ms.");
                    return;
                }
                catch (Exception ex) when (attempt < maxRetries && !cancellationToken.IsCancellationRequested)
                {
                    int delayMs = backoffDelaysMs[attempt - 1];
                    Console.WriteLine($"[AuthValidation] Attempt {attempt}/{maxRetries} failed: {ex.Message}");
                    Console.WriteLine($"[AuthValidation] Clearing caches and waiting {delayMs}ms before retry...");

                    // Clear caches to ensure we don't reuse failed auth state
                    ClearAuthenticationCache();

                    await Task.Delay(delayMs, cancellationToken);
                }
            }

            // If we get here, all retries failed
            stopwatch.Stop();
            string errorMessage = $"[AuthValidation] Authentication validation failed after {maxRetries} attempts over {stopwatch.ElapsedMilliseconds}ms. " +
                                  $"This may indicate AAD/RBAC propagation delays or configuration issues. " +
                                  $"Check that the test application has the required permissions.";
            Console.WriteLine(errorMessage);
            throw new InvalidOperationException(errorMessage);
        }

        public virtual ValueTask DisposeAsync()
        {
            foreach (Lazy<TestFhirClient> cacheValue in _cache.Values)
            {
                if (cacheValue.IsValueCreated)
                {
                    cacheValue.Value.HttpClient.Dispose();
                }
            }

            return default;
        }

        /// <summary>
        /// An <see cref="HttpMessageHandler"/> that maintains Cosmos DB session consistency between requests.
        /// </summary>
        private class SessionMessageHandler : DelegatingHandler
        {
            private readonly SessionTokenContainer _sessionTokenContainer;
            private readonly AsyncRetryPolicy<HttpResponseMessage> _polly;

            public SessionMessageHandler(HttpMessageHandler innerHandler, SessionTokenContainer sessionTokenContainer)
                : base(innerHandler)
            {
                EnsureArg.IsNotNull(sessionTokenContainer, nameof(sessionTokenContainer));
                _sessionTokenContainer = sessionTokenContainer;

                _polly = Policy.Handle<HttpRequestException>(x =>
                    {
                        if (x.InnerException is IOException || x.InnerException is SocketException)
                        {
                            return true;
                        }

                        return false;
                    })
                    .OrResult<HttpResponseMessage>(message => message.StatusCode == HttpStatusCode.TooManyRequests ||
                                                              message.StatusCode == HttpStatusCode.ServiceUnavailable)
                    .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string latestValue = _sessionTokenContainer.SessionToken;

                if (!string.IsNullOrEmpty(latestValue))
                {
                    request.Headers.TryAddWithoutValidation("x-ms-session-token", latestValue);
                }

                request.Headers.TryAddWithoutValidation("x-ms-consistency-level", "Session");

                if (request.Content != null)
                {
                    await request.Content.LoadIntoBufferAsync();
                }

                HttpResponseMessage response = await _polly.ExecuteAsync(async () => await base.SendAsync(request, cancellationToken));

                if (response.Headers.TryGetValues("x-ms-session-token", out var tokens))
                {
                    _sessionTokenContainer.SessionToken = tokens.SingleOrDefault();
                }

                return response;
            }
        }

        private class SessionTokenContainer
        {
            public string SessionToken { get; set; }
        }
    }
}
