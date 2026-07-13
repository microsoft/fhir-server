// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// The single source of truth for the health-check selection predicates behind each Kubernetes
    /// probe endpoint. <c>UseFhirServer</c> maps these onto <c>/health/check</c>, <c>/health/startup</c>,
    /// <c>/health/ready</c> and <c>/health/live</c>, and the startup registration assertion reuses
    /// <see cref="Readiness"/> and <see cref="Startup"/>. Keeping the predicates here (rather than as
    /// inline lambdas) lets unit tests exercise the exact production logic, so a change to probe
    /// routing cannot silently drift away from its regression coverage.
    /// </summary>
    internal static class HealthProbePredicates
    {
        /// <summary>
        /// Diagnostic endpoint (<c>/health/check</c>): honors the caller-supplied predicate but always
        /// excludes the startup gate, so a still-initializing pod is not reported as failed here.
        /// </summary>
        /// <param name="callerPredicate">Optional additional filter supplied by the host. When null, all non-startup checks run.</param>
        internal static Func<HealthCheckRegistration, bool> Diagnostic(Func<HealthCheckRegistration, bool> callerPredicate) =>
            reg => (callerPredicate?.Invoke(reg) ?? true) && !reg.Tags.Contains(HealthCheckTags.ProbeStartup);

        /// <summary>Startup gate (<c>/health/startup</c>): only the storage-init check runs.</summary>
        internal static bool Startup(HealthCheckRegistration reg) =>
            reg.Tags.Contains(HealthCheckTags.ProbeStartup);

        /// <summary>
        /// Readiness/routing (<c>/health/ready</c>): the data-store check plus any readiness-tagged
        /// checks. Degraded (e.g. a CMK failure) maps to HTTP 200 so the pod stays routable.
        /// </summary>
        internal static bool Readiness(HealthCheckRegistration reg) =>
            reg.Tags.Contains(HealthCheckTags.DataStoreSqlServer) || reg.Tags.Contains(HealthCheckTags.ProbeReadiness);

        /// <summary>Dependency-free HTTP liveness (<c>/health/live</c>): run no checks so an empty report is Healthy => 200.</summary>
        internal static bool Live(HealthCheckRegistration reg) => false;
    }
}
