// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// Represents the states of a Sort parameter
    /// </summary>
    public enum SortParameterStatus
    {
        /// <summary>
        /// The parameter is not sortable
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// The system will create the index for sort, but it not enabled for use by the api
        /// </summary>
        Supported = 1,

        /// <summary>
        /// Sort parameter is enabled
        /// </summary>
        Enabled = 2,
    }
}
