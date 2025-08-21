// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Health
{
    /// <summary>
    /// Contains constants used by SqlStorageStatusReporter.
    /// </summary>
    public static class SqlStatusReporterConstants
    {
        /// <summary>
        /// Message for degraded health when customer managed key is unhealthy.
        /// </summary>
        public const string DegradedDescription = "The health of the store has degraded.";
    }
}
