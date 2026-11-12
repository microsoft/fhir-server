// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Api.Configs
{
    /// <summary>
    /// Trusted forwarder settings applied when forwarded headers are enabled
    /// (ASPNETCORE_FORWARDEDHEADERS_ENABLED=true).
    /// </summary>
    public class ForwardedHeadersConfiguration
    {
        /// <summary>
        /// Gets the IP addresses of trusted proxies (e.g. "10.0.0.4"). When either trust list is nonempty,
        /// forwarded headers are only honored when the immediate caller matches an entry here or in
        /// <see cref="KnownNetworks"/>. If both lists are empty, every caller is trusted (legacy behavior).
        /// </summary>
        public IList<string> KnownProxies { get; } = new List<string>();

        /// <summary>
        /// Gets the CIDR ranges of trusted proxy networks (e.g. "10.244.0.0/16"). When either trust list is nonempty,
        /// forwarded headers are only honored when the immediate caller matches an entry here or in
        /// <see cref="KnownProxies"/>. If both lists are empty, every caller is trusted (legacy behavior).
        /// </summary>
        public IList<string> KnownNetworks { get; } = new List<string>();
    }
}
