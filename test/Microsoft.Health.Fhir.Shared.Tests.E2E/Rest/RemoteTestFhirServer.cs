// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using System.Net.Http;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.E2E.Common;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    /// <summary>
    /// Represents a <see cref="TestFhirServer"/> that resides out of process that we will
    /// communicate with over TCP/IP.
    /// </summary>
    public class RemoteTestFhirServer : TestFhirServer
    {
        public RemoteTestFhirServer(string environmentUrl)
            : base(new Uri(environmentUrl))
        {
            var value = EnvironmentVariables.GetEnvironmentVariable(KnownEnvironmentVariableNames.TestSearchParameterCacheRefreshIntervalSeconds);
            if (value == null)
            {
                SearchParameterCacheRefreshInterval = base.SearchParameterCacheRefreshInterval;
                return;
            }

            // Five refresh cycles must fit the cancellation timer used by status polling.
            const int maximumSeconds = int.MaxValue / 5000;
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0 || seconds > maximumSeconds)
            {
                throw new InvalidOperationException(
                    $"{KnownEnvironmentVariableNames.TestSearchParameterCacheRefreshIntervalSeconds} must be an integer between 1 and {maximumSeconds} seconds. Received '{value}'.");
            }

            SearchParameterCacheRefreshInterval = TimeSpan.FromSeconds(seconds);
        }

        /// <inheritdoc />
        public override TimeSpan SearchParameterCacheRefreshInterval { get; }

        internal override HttpMessageHandler CreateMessageHandler()
        {
            return new HttpClientHandler();
        }

        protected override Uri GetClientCredentialTokenEndpoint() => AuthenticationSettings.TestTokenEndpoint;
    }
}
