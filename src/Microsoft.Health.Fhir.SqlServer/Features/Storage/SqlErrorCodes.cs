// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    /// <summary>
    /// Known SQL Server error codes
    /// </summary>
    public static class SqlErrorCodes
    {
        /// <summary>
        /// Custom error cores must be >= this
        /// </summary>
        private const int CustomErrorCodeBase = 50000;

        /// <summary>
        /// A resource was not found
        /// </summary>
        public const int NotFound = CustomErrorCodeBase + 404;

        /// <summary>
        /// An optimistic concurrency precondition failed
        /// </summary>
        public const int PreconditionFailed = CustomErrorCodeBase + 412;
    }
}
