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
using System.Text;
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

        private readonly Dictionary<string, HttpMessageHandler> _authenticationHandlers = new Dictionary<string, HttpMessageHandler>();

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

        public TestFhirClient GetTestFhirClient(ResourceFormat format, bool reusable = true, HttpMessageHandler authenticationHandler = null)
        {
            return GetTestFhirClient(format, TestApplications.GlobalAdminServicePrincipal, null, reusable, authenticationHandler);
        }

        public TestFhirClient GetTestFhirClient(ResourceFormat format, TestApplication clientApplication, TestUser user, bool reusable = true, HttpMessageHandler authenticationHandler = null)
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

        private TestFhirClient CreateFhirClient(ResourceFormat format, TestApplication clientApplication, TestUser user, HttpMessageHandler authenticationHandler = null)
        {
            HttpMessageHandler innerHandler;
            if (authenticationHandler != null)
            {
                innerHandler = authenticationHandler;
            }
            else if (SecurityEnabled)
            {
                // GetAuthenticationHandler returns null for InvalidClient (no auth token scenario)
                // Fall back to basic handler for testing unauthorized scenarios
                innerHandler = GetAuthenticationHandler(clientApplication, user) ?? CreateMessageHandler();
            }
            else
            {
                innerHandler = CreateMessageHandler();
            }

            var sessionMessageHandler = new SessionMessageHandler(innerHandler, _sessionTokenContainer);

            var httpClient = new HttpClient(sessionMessageHandler) { BaseAddress = BaseAddress };

            return new TestFhirClient(httpClient, this, format, clientApplication, user);
        }

        private HttpMessageHandler GetAuthenticationHandler(TestApplication clientApplication, TestUser user)
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

            ICredentialProvider innerCredentialProvider;
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

                innerCredentialProvider = new OAuth2ClientCredentialProvider(optionsMonitor, authHttpClient);
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

                innerCredentialProvider = new OAuth2UserPasswordCredentialProvider(optionsMonitor, authHttpClient);
            }

            // Wrap the credential provider with retry capability to handle transient 401 errors
            var retryableCredentialProvider = new RetryableCredentialProvider(innerCredentialProvider);

            var authenticationHandler = new AuthenticationHttpMessageHandler(retryableCredentialProvider)
            {
                InnerHandler = CreateMessageHandler(),
            };

            // Wrap with retry handler to automatically retry on 401 with token invalidation
            var retryHandler = new RetryAuthenticationHttpMessageHandler(
                retryableCredentialProvider,
                authenticationHandler,
                maxRetries: 3,
                baseDelay: TimeSpan.FromSeconds(2));

            _authenticationHandlers.Add(authDictionaryKey, retryHandler);

            return retryHandler;

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

            var httpClient = new HttpClient(CreateMessageHandler())
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(30),
            };

            // Retry policy for transient failures (500 errors, timeouts, etc.) during server startup
            // Total timeout is 5 minutes to ensure we fail fast if the server is not coming up
            var overallTimeout = TimeSpan.FromMinutes(5);
            var overallStopwatch = System.Diagnostics.Stopwatch.StartNew();
            const int baseDelaySeconds = 5;
            const int maxDelaySeconds = 30;
            HttpResponseMessage response = null;
            string content = null;
            int attempt = 0;

            while (overallStopwatch.Elapsed < overallTimeout)
            {
                attempt++;
                try
                {
                    using HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseAddress, "metadata"));
                    response = await httpClient.SendAsync(requestMessage, cancellationToken);
                    content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[ConfigureSecurityOptions] Metadata fetch successful on attempt {attempt} after {overallStopwatch.Elapsed.TotalSeconds:F1}s.");
                        break;
                    }

                    // Retry on 5xx errors (server not ready) or 401/503 (transient auth/availability issues) if within timeout
                    if (((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.ServiceUnavailable) &&
                        overallStopwatch.Elapsed < overallTimeout)
                    {
                        int delaySeconds = Math.Min(baseDelaySeconds * (int)Math.Pow(2, Math.Min(attempt - 1, 3)), maxDelaySeconds); // Cap growth at attempt 4
                        Console.WriteLine($"[ConfigureSecurityOptions] Metadata fetch returned {response.StatusCode} on attempt {attempt}. Elapsed: {overallStopwatch.Elapsed.TotalSeconds:F1}s. Retrying in {delaySeconds}s...");
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                        response.Dispose();
                        continue;
                    }

                    // Non-retryable error or timeout exhausted
                    response.EnsureSuccessStatusCode();
                }
                catch (Exception ex) when (ex is TaskCanceledException || ex is HttpRequestException || ex is IOException)
                {
                    if (overallStopwatch.Elapsed < overallTimeout)
                    {
                        int delaySeconds = Math.Min(baseDelaySeconds * (int)Math.Pow(2, Math.Min(attempt - 1, 3)), maxDelaySeconds);
                        Console.WriteLine($"[ConfigureSecurityOptions] Metadata fetch failed with {ex.GetType().Name} on attempt {attempt}. Elapsed: {overallStopwatch.Elapsed.TotalSeconds:F1}s. Retrying in {delaySeconds}s...");
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                        continue;
                    }

                    throw new HttpRequestException($"ConfigureSecurityOptions failed after {attempt} attempts over {overallStopwatch.Elapsed.TotalSeconds:F1}s. Last error: {ex.Message}", ex);
                }
            }

            // If we exited the loop due to timeout without success
            if (response == null || !response.IsSuccessStatusCode)
            {
                string errorMessage = response != null
                    ? $"ConfigureSecurityOptions failed after {attempt} attempts over {overallStopwatch.Elapsed.TotalSeconds:F1}s. Last status: {response.StatusCode}"
                    : $"ConfigureSecurityOptions failed after {attempt} attempts over {overallStopwatch.Elapsed.TotalSeconds:F1}s. No response received.";
                throw new HttpRequestException(errorMessage);
            }

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

                // Retry policy for transient errors including 429 (TooManyRequests) and 503 (ServiceUnavailable)
                // Uses longer delays and more retries to handle throttling scenarios in E2E tests
                _polly = Policy.Handle<HttpRequestException>(x =>
                    {
                        if (x.InnerException is IOException || x.InnerException is SocketException)
                        {
                            return true;
                        }

                        return false;
                    })
                    .Or<TaskCanceledException>(ex => !CancellationToken.None.IsCancellationRequested) // Timeout, not user cancellation
                    .OrResult<HttpResponseMessage>(message => message.StatusCode == HttpStatusCode.TooManyRequests ||
                                                              message.StatusCode == HttpStatusCode.ServiceUnavailable)
                    .WaitAndRetryAsync(
                        retryCount: 10,
                        sleepDurationProvider: (retryAttempt, result, context) =>
                        {
                            // Check for Retry-After header on 429 responses - honor the server's requested delay
                            if (result.Result?.Headers.TryGetValues("Retry-After", out var retryAfterValues) == true)
                            {
                                var retryAfterValue = retryAfterValues.FirstOrDefault();
                                if (int.TryParse(retryAfterValue, out int retryAfterSeconds))
                                {
                                    return TimeSpan.FromSeconds(retryAfterSeconds);
                                }
                            }

                            // Exponential backoff: 2s, 4s, 8s, 16s, 32s, 60s (capped)
                            var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt), 60));
                            return delay;
                        },
                        onRetryAsync: async (outcome, timespan, retryAttempt, context) =>
                        {
                            var statusCode = outcome.Result?.StatusCode.ToString() ?? outcome.Exception?.GetType().Name ?? "Unknown";
                            Console.WriteLine($"[SessionMessageHandler] Retry {retryAttempt}/10 after {statusCode}. Waiting {timespan.TotalSeconds:F1}s before next attempt...");
                            await Task.CompletedTask;
                        });
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string latestValue = _sessionTokenContainer.SessionToken;

                if (!string.IsNullOrEmpty(latestValue))
                {
                    request.Headers.TryAddWithoutValidation("x-ms-session-token", latestValue);
                }

                request.Headers.TryAddWithoutValidation("x-ms-consistency-level", "Session");

                // Buffer the content so we can clone the request for retries
                // HttpRequestMessage cannot be reused after sending, so we need to clone it for each retry
                byte[] contentBytes = null;
                string contentType = null;
                if (request.Content != null)
                {
                    await request.Content.LoadIntoBufferAsync();
                    contentBytes = await request.Content.ReadAsByteArrayAsync();
                    contentType = request.Content.Headers.ContentType?.ToString();
                }

                var diagnostics = new StringBuilder();
                var overallStopwatch = Stopwatch.StartNew();
                int attemptNumber = 0;

                try
                {
                    HttpResponseMessage response = await _polly.ExecuteAsync(async () =>
                    {
                        attemptNumber++;
                        var attemptStopwatch = Stopwatch.StartNew();

                        // Clone the request for each attempt (HttpRequestMessage cannot be reused after sending)
                        using var clonedRequest = CloneHttpRequestMessage(request, contentBytes, contentType);

                        // Re-add session token to cloned request (may have been updated from previous response)
                        string currentSessionToken = _sessionTokenContainer.SessionToken;
                        if (!string.IsNullOrEmpty(currentSessionToken))
                        {
                            clonedRequest.Headers.Remove("x-ms-session-token");
                            clonedRequest.Headers.TryAddWithoutValidation("x-ms-session-token", currentSessionToken);
                        }

                        try
                        {
                            var result = await base.SendAsync(clonedRequest, cancellationToken);
                            attemptStopwatch.Stop();
                            diagnostics.AppendLine($"  Attempt {attemptNumber}: {(int)result.StatusCode} {result.StatusCode} in {attemptStopwatch.ElapsedMilliseconds}ms");
                            return result;
                        }
                        catch (Exception ex)
                        {
                            attemptStopwatch.Stop();
                            diagnostics.AppendLine($"  Attempt {attemptNumber}: Exception ({ex.GetType().Name}: {ex.Message}) in {attemptStopwatch.ElapsedMilliseconds}ms");
                            throw;
                        }
                    });

                    if (response.Headers.TryGetValues("x-ms-session-token", out var tokens))
                    {
                        _sessionTokenContainer.SessionToken = tokens.SingleOrDefault();
                    }

                    return response;
                }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                {
                    // This is a timeout, not a user-initiated cancellation
                    overallStopwatch.Stop();
                    throw new HttpRequestException(
                        $"Request timed out after {overallStopwatch.ElapsedMilliseconds}ms.\n" +
                        $"Request: {request.Method} {request.RequestUri}\n" +
                        $"Attempts ({attemptNumber} total):\n{diagnostics}",
                        ex);
                }
                catch (TaskCanceledException)
                {
                    // User-initiated cancellation, just rethrow
                    throw;
                }
                catch (Exception ex)
                {
                    overallStopwatch.Stop();
                    throw new HttpRequestException(
                        $"Request failed after {overallStopwatch.ElapsedMilliseconds}ms.\n" +
                        $"Request: {request.Method} {request.RequestUri}\n" +
                        $"Attempts ({attemptNumber} total):\n{diagnostics}" +
                        $"Final exception: {ex.GetType().Name}: {ex.Message}",
                        ex);
                }
            }

            /// <summary>
            /// Clones an HttpRequestMessage for retry purposes.
            /// HttpRequestMessage cannot be reused after sending, so we need to create a new one for each retry attempt.
            /// </summary>
            private static HttpRequestMessage CloneHttpRequestMessage(HttpRequestMessage request, byte[] contentBytes, string contentType)
            {
                var clone = new HttpRequestMessage(request.Method, request.RequestUri);

                // Copy headers
                foreach (var header in request.Headers)
                {
                    clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }

                // Copy content if present
                if (contentBytes != null)
                {
                    clone.Content = new ByteArrayContent(contentBytes);
                    if (!string.IsNullOrEmpty(contentType))
                    {
                        clone.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
                    }
                }

                // Copy properties/options
                foreach (var prop in request.Options)
                {
                    clone.Options.TryAdd(prop.Key, prop.Value);
                }

                return clone;
            }
        }

        private class SessionTokenContainer
        {
            public string SessionToken { get; set; }
        }
    }
}
