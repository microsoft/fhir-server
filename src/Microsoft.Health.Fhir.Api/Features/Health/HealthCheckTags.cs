// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Api.Features.Health
{
    /// <summary>
    /// Health-check tag constants used to select which checks each Kubernetes probe runs.
    /// A tag typo silently fails open (an empty selection resolves to Healthy => HTTP 200),
    /// so every registration and predicate MUST reference these constants rather than literals.
    /// </summary>
    public static class HealthCheckTags
    {
        /// <summary>Tag for the startup gate check (<see cref="StorageInitializedHealthCheck"/>).</summary>
        public const string ProbeStartup = "probe:startup";

        /// <summary>Tag for checks that participate in the readiness/routing decision.</summary>
        public const string ProbeReadiness = "probe:readiness";

        /// <summary>
        /// Mirrors the tag applied by the healthcare-shared-components SQL registration.
        /// A startup assertion fails loudly if the shared value ever drifts from this literal.
        /// </summary>
        public const string DataStoreSqlServer = "datastore:sqlServer";

        /// <summary>
        /// Registration name of the data-store health check. This is a check name (not a tag),
        /// shared between the in-repo Cosmos registration and the readiness startup assertion so
        /// that the load-bearing name filter cannot silently drift. The healthcare-shared-components
        /// SQL registration uses the same literal; the assertion fails loudly if that ever changes.
        /// </summary>
        public const string DataStoreHealthCheckName = "DataStoreHealthCheck";
    }
}
