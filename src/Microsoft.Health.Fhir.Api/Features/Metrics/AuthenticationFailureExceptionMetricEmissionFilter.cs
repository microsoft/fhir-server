// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace Microsoft.Health.Fhir.Api.Features.Metrics
{
    /// <summary>
    /// Suppresses <c>fhir/failures/exceptions</c> metric emission for authentication failures that resulted in
    /// an HTTP 401 (Unauthorized) response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// During an incident, a customer sent a sustained burst of requests with an expired bearer token. Every
    /// request produced a <see cref="SecurityTokenException"/> that was logged at error severity, which in turn
    /// caused the OpenTelemetry log enricher to emit a <c>fhir/failures/exceptions</c> metric event per request.
    /// The aggregate volume was high enough to cause Geneva to throttle the shared metric account, degrading
    /// monitoring for FHIR and DICOM.
    /// </para>
    /// <para>
    /// Filtering inbound at Geneva is not supported (see ADR-2605), so this filter suppresses the metric at the
    /// emission site. The suppression is intentionally narrow: only exceptions for which
    /// <see cref="IsAuthenticationException"/> returns <c>true</c> AND that produced a response status code of
    /// 401 are filtered out. 403 (Forbidden) responses, validation errors, and any other failure mode continue
    /// to produce metrics so that genuine failure signals remain visible.
    /// </para>
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
    ///       <see cref="IsAuthenticationException"/> to recognize additional exception types in addition to
    ///       <see cref="SecurityTokenException"/>. Then replace the registration:
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
            if (exception == null || httpContext == null)
            {
                return true;
            }

            if (httpContext.Response?.StatusCode != (int)HttpStatusCode.Unauthorized)
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
