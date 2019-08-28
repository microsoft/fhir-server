// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net.Http;

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
        }

        protected override HttpMessageHandler CreateMessageHandler()
        {
            return new HttpClientHandler();
        }
    }
}
