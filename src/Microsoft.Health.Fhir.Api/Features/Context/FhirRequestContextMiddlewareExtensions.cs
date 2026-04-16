// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Net;
using EnsureThat;
using Microsoft.AspNetCore.Builder;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    public static class FhirRequestContextMiddlewareExtensions
    {
        public static IApplicationBuilder UseFhirRequestContext(
            this IApplicationBuilder builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            return builder.UseMiddleware<FhirRequestContextMiddleware>();
        }

        /// <summary>
        /// Determines if the request is from a loopback or local IP address.
        /// This is used to exclude health check requests from initializing the instance configuration.
        /// </summary>
        /// <param name="host">The host name or IP address from the request.</param>
        /// <returns>True if the host is a loopback or local IP address; otherwise, false.</returns>
        internal static bool IsLoopbackOrLocalRequest(string host)
        {
            if (string.IsNullOrEmpty(host))
            {
                return false;
            }

            // Remove brackets if present (IPv6 addresses may be wrapped in brackets in Host headers)
            var hostOnly = host.TrimStart('[').TrimEnd(']');

            // Remove port if present. IPv6 addresses have colons in the address itself,
            // so we need to be careful. Only remove port for IPv4:port format (single colon).
            if (hostOnly.Contains(':', StringComparison.Ordinal))
            {
                var colonCount = hostOnly.Count(c => c == ':');
                if (colonCount == 1)
                {
                    // Single colon - likely IPv4:port format, remove port
                    hostOnly = hostOnly.Split(':')[0];
                }

                // If multiple colons, this is IPv6 address - keep as is
            }

            // Check for common loopback/local identifiers
            if (hostOnly.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                hostOnly.Equals("127.0.0.1", StringComparison.Ordinal) ||
                hostOnly.Equals("::1", StringComparison.Ordinal) || // IPv6 loopback
                hostOnly.StartsWith("127.", StringComparison.Ordinal) || // 127.x.x.x range
                hostOnly.StartsWith("192.168.", StringComparison.Ordinal) || // Private network
                hostOnly.StartsWith("10.", StringComparison.Ordinal) || // Private network
                (hostOnly.StartsWith("172.", StringComparison.Ordinal) && IsIn172PrivateRange(hostOnly))) // Private 172.16.0.0/12
            {
                return true;
            }

            return false;
        }

        private static bool IsIn172PrivateRange(string host)
        {
            if (IPAddress.TryParse(host, out var ip))
            {
                var bytes = ip.GetAddressBytes();
                return bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31;
            }

            return false;
        }
    }
}
