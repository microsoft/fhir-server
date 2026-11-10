// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    /// <summary>
    /// The FHIR temporal leaf type proven for a search parameter expression.
    /// </summary>
    public enum TemporalType
    {
        /// <summary>No complete classification is available.</summary>
        Unknown,

        /// <summary>Every branch extracts a FHIR date.</summary>
        Date,

        /// <summary>Every branch extracts a FHIR dateTime.</summary>
        DateTime,

        /// <summary>Every branch extracts a FHIR instant.</summary>
        Instant,

        /// <summary>Every branch extracts a FHIR Period.</summary>
        Period,

        /// <summary>Every branch extracts a FHIR Timing.</summary>
        Timing,

        /// <summary>All branches are understood, but extract different temporal types.</summary>
        Mixed,
    }
}
