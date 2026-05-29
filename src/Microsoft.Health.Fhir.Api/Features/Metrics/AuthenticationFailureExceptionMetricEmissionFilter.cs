// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Health.Fhir.Api.Features.Metrics
{
    /// <summary>
    /// Suppresses <c>fhir/failures/exceptions</c> metric emission for token-validation failures
    /// (<see cref="SecurityTokenException"/> and its derivatives).
    /// </summary>
    /// <remarks>
    /// <para>
    /// During an incident, a customer sent a sustained burst of requests with an expired bearer token. Every
    /// request produced a <see cref="SecurityTokenException"/> that the token-introspection path logged with
    /// the exception object attached, which in turn caused the OpenTelemetry log enricher to emit a
    /// <c>fhir/failures/exceptions</c> metric event per request. The aggregate volume was high enough to
    /// cause Geneva to throttle the shared metric account, degrading monitoring for FHIR and DICOM.
    /// </para>
    /// <para>
    /// Filtering inbound at Geneva is not supported (see ADR-2605), so this filter suppresses the metric at
    /// the emission site, keyed on the exception type alone.
    /// </para>
    /// <para>
    /// <b>Why not also gate on a 401 response status?</b> The flood log is written from inside the
    /// authentication / token-introspection path — i.e. <i>before</i> the challenge that writes the 401 to
    /// the response. At the moment the enricher observes the log record, <c>HttpContext.Response.StatusCode</c>
    /// is still the default 200, so a 401 condition would never match the very flood the filter is intended
    /// to suppress. <see cref="SecurityTokenException"/> is defined in <c>Microsoft.IdentityModel.Tokens</c>
    /// and in this server is produced only by token-validation code, so suppression by exception type alone
    /// does not hide any non-authentication failure.
    /// </para>
    /// <para>
    /// <b>Covered exception types.</b> The default implementation of <see cref="IsAuthenticationException"/>
    /// matches <see cref="SecurityTokenException"/> anywhere in the inner-exception chain. Because the
    /// following types derive from <see cref="SecurityTokenException"/>, they are also matched without any
    /// additional code:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><see cref="SecurityTokenExpiredException"/></description></item>
    ///   <item><description><see cref="SecurityTokenInvalidAudienceException"/></description></item>
    ///   <item><description><see cref="SecurityTokenInvalidIssuerException"/></description></item>
    ///   <item><description>Any other type derived from <see cref="SecurityTokenException"/>.</description></item>
    /// </list>
    /// <para>
    /// <b>Extensibility for downstream consumers (e.g. fhir-paas).</b> Three extension patterns are supported:
    /// </para>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <b>Add a new, independent rule.</b> Register an additional
    ///       <see cref="IExceptionMetricEmissionFilter"/> via
    ///       <c>services.AddSingleton&lt;IExceptionMetricEmissionFilter, MyFilter&gt;()</c>. All registered
    ///       filters are consulted; a metric is emitted only when every filter returns <c>true</c>. No change
    ///       to this class is required.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Extend what counts as an authentication exception.</b> Subclass this type and override
    ///       <see cref="IsAuthenticationException"/> to recognize additional authentication-related exception
    ///       types (for example, an fhir-paas <c>S2SAuthenticationException</c>) in addition to the default
    ///       <see cref="SecurityTokenException"/> family. Then replace the registration:
    ///       <c>services.RemoveAll&lt;IExceptionMetricEmissionFilter&gt;()</c> followed by
    ///       <c>services.AddSingleton&lt;IExceptionMetricEmissionFilter, MyAuthFilter&gt;()</c> (plus any other
    ///       filters you want to keep).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b>Disable this filter entirely.</b> Call
    ///       <c>services.RemoveAll&lt;IExceptionMetricEmissionFilter&gt;()</c> in startup after the default
    ///       registration runs, then add only the filters you want.
    ///     </description>
    ///   </item>
    /// </list>
    /// </remarks>
    public class AuthenticationFailureExceptionMetricEmissionFilter : IExceptionMetricEmissionFilter
    {
        /// <inheritdoc />
        public bool ShouldEmit(Exception exception, HttpContext httpContext)
        {
            if (exception == null)
            {
                return true;
            }

            return !IsAuthenticationException(exception);
        }

        /// <summary>
        /// Returns whether the given exception (or any exception in its inner-exception chain) represents an
        /// authentication failure. Override in a subclass to recognize additional authentication-related
        /// exception types defined by a downstream consumer.
        /// </summary>
        /// <param name="exception">The exception under consideration. Never <c>null</c>.</param>
        /// <returns><c>true</c> if the exception is (or wraps) an authentication failure.</returns>
        protected virtual bool IsAuthenticationException(Exception exception)
        {
            for (Exception current = exception; current != null; current = current.InnerException)
            {
                if (current is SecurityTokenException)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
