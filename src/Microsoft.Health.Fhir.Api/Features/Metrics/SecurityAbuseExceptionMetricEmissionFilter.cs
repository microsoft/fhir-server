// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Health.Fhir.Api.Features.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Metrics
{
    /// <summary>
    /// Suppresses <c>fhir/failures/exceptions</c> metric emission for security-abuse exceptions — exceptions
    /// thrown when the server detects and rejects an abuse pattern such as a Server-Side Request Forgery
    /// (SSRF) attempt.
    /// </summary>
    /// <remarks>
    /// <para>
    /// These exception types are themselves the actionable signal — every occurrence indicates an attacker or
    /// misbehaving client probing the service. Per-event metric emission adds no monitoring value beyond what
    /// security/audit logs already provide, and under a sustained probing campaign the per-event metric volume
    /// can flood Geneva (see ADR-2605). The corresponding signals for operators come from audit logs,
    /// gateway-level rejection counters, and aggregated security telemetry — not from per-request FHIR
    /// failure metrics.
    /// </para>
    /// <para>
    /// The default implementation matches <see cref="ServerSideRequestForgeryException"/> in the
    /// inner-exception chain. Matching is exception-type-only — no status-code condition — because:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       Unlike <c>Microsoft.IdentityModel.Tokens.SecurityTokenException</c> (which can legitimately be
    ///       thrown by outbound clients in non-401 contexts), these exception types are constructed only
    ///       inside FHIR's SSRF-detection/security-rejection paths. Their presence is by itself a sufficient
    ///       signal to suppress.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       The mapped response is always a 4xx (typically 403 for SSRF), so a status-code condition would
    ///       add no precision while creating a code-coverage burden.
    ///     </description>
    ///   </item>
    /// </list>
    /// <para>
    /// <b>Extensibility for downstream consumers (e.g. fhir-paas).</b> Additional security-abuse exception
    /// types that live in downstream repositories — for example <c>AntiSSRFException</c> defined in fhir-paas —
    /// should be added by subclassing this type and overriding <see cref="IsSecurityAbuseException"/>:
    /// </para>
    /// <code>
    /// public sealed class PaasSecurityAbuseExceptionMetricEmissionFilter
    ///     : SecurityAbuseExceptionMetricEmissionFilter
    /// {
    ///     protected override bool IsSecurityAbuseException(Exception exception)
    ///     {
    ///         if (base.IsSecurityAbuseException(exception)) return true;
    ///         for (var current = exception; current != null; current = current.InnerException)
    ///         {
    ///             if (current is AntiSSRFException) return true;
    ///         }
    ///         return false;
    ///     }
    /// }
    ///
    /// // Then in fhir-paas Startup, after AddFhirServer has registered the default:
    /// services.RemoveAll&lt;IExceptionMetricEmissionFilter&gt;();
    /// services.AddSingleton&lt;IExceptionMetricEmissionFilter, AuthenticationFailureExceptionMetricEmissionFilter&gt;();
    /// services.AddSingleton&lt;IExceptionMetricEmissionFilter, PaasSecurityAbuseExceptionMetricEmissionFilter&gt;();
    /// </code>
    /// <para>
    /// Alternatively, fhir-paas can register a separate, independent filter for its own exception types — the
    /// enricher AND-combines all registered filters, so the rules are additive without needing to subclass.
    /// </para>
    /// </remarks>
    public class SecurityAbuseExceptionMetricEmissionFilter : IExceptionMetricEmissionFilter
    {
        /// <inheritdoc />
        public bool ShouldEmit(Exception exception, HttpContext httpContext)
        {
            if (exception == null)
            {
                return true;
            }

            return !IsSecurityAbuseException(exception);
        }

        /// <summary>
        /// Returns whether the given exception (or any exception in its inner-exception chain) represents a
        /// security-abuse signal that should not produce a per-event failure metric. Override in a subclass to
        /// recognize additional abuse-related exception types defined by a downstream consumer.
        /// </summary>
        /// <param name="exception">The exception under consideration. Never <c>null</c>.</param>
        /// <returns><c>true</c> if the exception is (or wraps) a security-abuse exception.</returns>
        protected virtual bool IsSecurityAbuseException(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is ServerSideRequestForgeryException)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
