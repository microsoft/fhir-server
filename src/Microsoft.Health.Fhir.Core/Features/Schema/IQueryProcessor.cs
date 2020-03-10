// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Schema
{
    public interface IQueryProcessor
    {
        /// <summary>
        /// Get compatible version.
        /// </summary>
        /// <param name="maxVersion">The maximum schema version specified by code</param>
        /// <returns>The latest supported schema version from server.</returns>
        int GetLatestCompatibleVersion(int maxVersion);
    }
}
