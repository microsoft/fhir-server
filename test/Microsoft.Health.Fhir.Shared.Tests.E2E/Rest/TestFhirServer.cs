// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
using Microsoft.Health.Fhir.Tests.E2E.Common;
using FhirClient = Microsoft.Health.Fhir.Tests.E2E.Common.FhirClient;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Represents a FHIR server for end-to-end testing.
    /// Creates and caches <see cref="FhirClient"/> instances that target the server.
    /// </summary>
    public abstract class TestFhirServer : IDisposable
    {
        private readonly ConcurrentDictionary<(ResourceFormat format, TestApplication clientApplication, TestUser user), Lazy<FhirClient>> _cache = new ConcurrentDictionary<(ResourceFormat format, TestApplication clientApplication, TestUser user), Lazy<FhirClient>>();

        protected TestFhirServer(Uri baseAddress)
        {
            EnsureArg.IsNotNull(baseAddress, nameof(baseAddress));

            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }

        public FhirClient GetFhirClient(ResourceFormat format, bool reusable = true)
        {
            return GetFhirClient(format, TestApplications.ServiceClient, null, reusable);
        }

        public FhirClient GetFhirClient(ResourceFormat format, TestApplication clientApplication, TestUser user, bool cacheable = true)
        {
            HttpClient CreateHttpClient()
            {
                return new HttpClient(new SessionMessageHandler(CreateMessageHandler())) { BaseAddress = BaseAddress };
            }

            if (!cacheable)
            {
                return new FhirClient(CreateHttpClient(), this, format, clientApplication, user);
            }

            return _cache.GetOrAdd(
                    (format, clientApplication, user),
                    (tuple, fhirServer) =>
                        new Lazy<FhirClient>(() =>
                            new FhirClient(CreateHttpClient(), fhirServer, tuple.format, tuple.clientApplication, tuple.user)),
                    this)
                .Value;
        }

        protected abstract HttpMessageHandler CreateMessageHandler();

        public virtual void Dispose()
        {
        }

        /// <summary>
        /// An <see cref="HttpMessageHandler"/> that maintains Cosmos DB session consistency between requests.
        /// </summary>
        private class SessionMessageHandler : DelegatingHandler
        {
            private readonly AsyncLocal<string> _sessionToken = new AsyncLocal<string>();

            public SessionMessageHandler(HttpMessageHandler innerHandler)
                : base(innerHandler)
            {
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string latestValue = _sessionToken.Value;

                if (!string.IsNullOrEmpty(latestValue))
                {
                    request.Headers.TryAddWithoutValidation("x-ms-session-token", latestValue);
                }

                request.Headers.TryAddWithoutValidation("x-ms-consistency-level", "Session");

                var response = await base.SendAsync(request, cancellationToken);

                if (response.Headers.TryGetValues("x-ms-session-token", out var tokens))
                {
                    _sessionToken.Value = tokens.SingleOrDefault();
                }

                return response;
            }
        }
    }
}
