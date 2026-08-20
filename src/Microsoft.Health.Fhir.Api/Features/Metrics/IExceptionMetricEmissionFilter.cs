// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Health.Fhir.Api.Features.Metrics
{
    /// <summary>
    /// Decides whether an exception observed by the metric emission pipeline (specifically the
    /// <c>fhir/failures/exceptions</c> metric emitted from <c>AzureMonitorOpenTelemetryLogEnricher</c>) should
    /// actually produce a metric event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both the exception and the current <see cref="HttpContext"/> are supplied because a decision often needs
    /// to look at both — e.g. "suppress this exception only when the response status code is 401" cannot be
    /// expressed by inspecting the exception alone, and "always suppress this benign exception type" cannot be
    /// expressed by inspecting only the response.
    /// </para>
    /// <para>
    /// Multiple implementations may be registered. All registered filters are consulted and a metric is emitted
    /// only when every filter returns <c>true</c> (logical AND). This allows independent, composable rules — e.g.
    /// one filter to suppress 401 authentication failures, another to suppress a noisy known-benign exception
    /// type — without any single component needing to know about every suppression policy.
    /// </para>
    /// <para>
    /// <b>Downstream consumers (e.g. fhir-paas) extend this pipeline by registering additional filters.</b>
    /// Example:
    /// </para>
    /// <code>
    /// // In fhir-paas Startup, after UseFhirServer/AddFhirServer has run:
    /// services.AddSingleton&lt;IExceptionMetricEmissionFilter, MyPaasBenignExceptionFilter&gt;();
    ///
    /// // To replace the default authentication filter with a customized one:
    /// services.RemoveAll&lt;IExceptionMetricEmissionFilter&gt;();
    /// services.AddSingleton&lt;IExceptionMetricEmissionFilter, MyExtendedAuthFilter&gt;();
    /// services.AddSingleton&lt;IExceptionMetricEmissionFilter, MyPaasBenignExceptionFilter&gt;();
    /// </code>
    /// </remarks>
    public interface IExceptionMetricEmissionFilter
    {
        /// <summary>
        /// Returns whether the given exception (which may be <c>null</c> when the metric originates from an
        /// error-level log entry rather than a thrown exception) should produce a <c>fhir/failures/exceptions</c>
        /// metric event for the supplied request context.
        /// </summary>
        /// <param name="exception">The exception associated with the log entry, or <c>null</c> if none.</param>
        /// <param name="httpContext">
        /// The current <see cref="HttpContext"/>, or <c>null</c> when the failure was not produced inside an
        /// HTTP request scope.
        /// </param>
        /// <returns><c>true</c> to allow the metric to be emitted; <c>false</c> to suppress it.</returns>
        bool ShouldEmit(Exception exception, HttpContext httpContext);
    }
}
