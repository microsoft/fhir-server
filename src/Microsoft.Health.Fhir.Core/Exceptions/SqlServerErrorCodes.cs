// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public static class SqlServerErrorCodes
    {
        /// <summary>
        /// Constraint Violation.
        /// </summary>
        public const int ConstraintViolation = 547;

        /// <summary>
        /// Number of concurrent calls to MergeResources is above optimal.
        /// </summary>
        public const int MergeResourcesConcurrentCallsIsAboveOptimal = 50410;

        /// <summary>
        /// Surrogate ID collision. Not enough resources to perform the operation: limit on surrogate IDs per second reached.
        /// This is a transient error and retrying the operation may succeed.
        /// </summary>
        public const int SurrogateIdCollision = 50424;

        /// <summary>
        /// Summary Key Vault Critical Error code.
        /// </summary>
        public const int KeyVaultCriticalError = 40981;

        /// <summary>
        /// Summary Key Vault Encountered Error code.
        /// </summary>
        public const int KeyVaultEncounteredError = 33183;

        /// <summary>
        /// Summary Key Vault Error Obtaining Info code.
        /// </summary>
        public const int KeyVaultErrorObtainingInfo = 33184;

        /// <summary>
        /// Summary Cannot Connect To DB In Current State code.
        /// </summary>
        public const int CannotConnectToDBInCurrentState = 40925;

        /// <summary>
        /// Set of Key Vault error codes.
        /// </summary>
        public static readonly int[] CustomerManagedKeyErrorCodes = new int[]
        {
            KeyVaultCriticalError,
            KeyVaultEncounteredError,
            KeyVaultErrorObtainingInfo,
            CannotConnectToDBInCurrentState,
        };
    }
}
