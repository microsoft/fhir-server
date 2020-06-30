// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using Polly;
using Polly.Retry;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Represents a FHIR server for end-to-end testing.
    /// Creates and caches <see cref="TestFhirClient"/> instances that target the server.
    /// </summary>
    public abstract class TestFhirServer : IDisposable
    {
        private readonly ConcurrentDictionary<(ResourceFormat format, TestApplication clientApplication, TestUser user), Lazy<TestFhirClient>> _cache = new ConcurrentDictionary<(ResourceFormat format, TestApplication clientApplication, TestUser user), Lazy<TestFhirClient>>();
        private readonly AsyncLocal<SessionTokenContainer> _asyncLocalSessionTokenContainer = new AsyncLocal<SessionTokenContainer>();

        protected TestFhirServer(Uri baseAddress)
        {
            EnsureArg.IsNotNull(baseAddress, nameof(baseAddress));

            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }

        public TestFhirClient GetTestFhirClient(ResourceFormat format, bool reusable = true)
        {
            return GetTestFhirClient(format, TestApplications.GlobalAdminServicePrincipal, null, reusable);
        }

        public TestFhirClient GetTestFhirClient(ResourceFormat format, TestApplication clientApplication, TestUser user, bool reusable = true)
        {
            if (_asyncLocalSessionTokenContainer.Value == null)
            {
                // Ensure that we are able to preserve session tokens across requests in this execution context and its children.
                _asyncLocalSessionTokenContainer.Value = new SessionTokenContainer();
            }

            if (!reusable)
            {
                return CreateFhirClient(format, clientApplication, user);
            }

            return _cache.GetOrAdd(
                    (format, clientApplication, user),
                    (tuple, fhirServer) =>
                        new Lazy<TestFhirClient>(() => CreateFhirClient(tuple.format, tuple.clientApplication, tuple.user)),
                    this)
                .Value;
        }

        private TestFhirClient CreateFhirClient(ResourceFormat format, TestApplication clientApplication, TestUser user)
        {
            var httpClient = new HttpClient(new SessionMessageHandler(CreateMessageHandler(), _asyncLocalSessionTokenContainer)) { BaseAddress = BaseAddress };

            return new TestFhirClient(httpClient, this, format, clientApplication, user);
        }

        protected abstract HttpMessageHandler CreateMessageHandler();

        public virtual void Dispose()
        {
            foreach (Lazy<TestFhirClient> cacheValue in _cache.Values)
            {
                if (cacheValue.IsValueCreated)
                {
                    cacheValue.Value.HttpClient.Dispose();
                }
            }
        }

        /// <summary>
        /// An <see cref="HttpMessageHandler"/> that maintains Cosmos DB session consistency between requests.
        /// </summary>
        private class SessionMessageHandler : DelegatingHandler
        {
            private readonly AsyncLocal<SessionTokenContainer> _asyncLocalSessionTokenContainer;
            private readonly AsyncRetryPolicy _polly;

            public SessionMessageHandler(HttpMessageHandler innerHandler, AsyncLocal<SessionTokenContainer> asyncLocalSessionTokenContainer)
                : base(innerHandler)
            {
                _asyncLocalSessionTokenContainer = asyncLocalSessionTokenContainer;
                EnsureArg.IsNotNull(asyncLocalSessionTokenContainer, nameof(asyncLocalSessionTokenContainer));
                _polly = Policy.Handle<HttpRequestException>(x =>
                {
                    if (x.InnerException is IOException || x.InnerException is SocketException)
                    {
                        return true;
                    }

                    return false;
                }).WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                SessionTokenContainer sessionTokenContainer = _asyncLocalSessionTokenContainer.Value;
                if (sessionTokenContainer == null)
                {
                    throw new InvalidOperationException($"{nameof(SessionTokenContainer)} has not been set for the execution context");
                }

                string latestValue = sessionTokenContainer.SessionToken;

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
                    sessionTokenContainer.SessionToken = tokens.SingleOrDefault();
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
