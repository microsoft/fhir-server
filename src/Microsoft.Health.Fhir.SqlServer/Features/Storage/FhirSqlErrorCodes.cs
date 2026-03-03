// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public static class FhirSqlErrorCodes
    {
        /// <summary>
        /// Failed dependency. Not enough resources to perform the operation: limit on surrogate IDs per second reached.
        /// This is a transient error and retrying the operation may succeed.
        /// </summary>
        public const int FailedDependency = 50424;
    }
}
