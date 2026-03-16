// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Known FHIR specific SQL Server error codes
    /// </summary>
    internal class SqlStoreErrorCodes
    {
        /// <summary>
        /// Number of concurrent calls to MergeResources is above optimal.
        /// </summary>
        public const int MergeResourcesConcurrentCallsIsAboveOptimal = 50410;

        /// <summary>
        /// Constraint Violation.
        /// </summary>
        public const int ConstraintViolation = 547;
    }
}
