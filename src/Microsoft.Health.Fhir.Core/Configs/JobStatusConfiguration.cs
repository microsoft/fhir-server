// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    /// <summary>
    /// Configuration for listing asynchronous job statuses.
    /// </summary>
    public class JobStatusConfiguration
    {
        /// <summary>
        /// Gets or sets the number of job groups returned per page.
        /// </summary>
        public int PageSize { get; set; } = 10;
    }
}
