// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Stats
{
    /// <summary>
    /// Request for resource statistics.
    /// </summary>
    public class StatsRequest : IRequest<StatsResponse>
    {
        /// <summary>
        /// Start date filter (inclusive).
        /// </summary>
        public DateTime? StartDate { get; set; }

        /// <summary>
        /// End date filter (inclusive).
        /// </summary>
        public DateTime? EndDate { get; set; }
    }
}
