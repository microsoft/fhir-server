// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public interface IFhirRequestContext : IRequestContext, ICloneable
    {
        string ResourceType { get; set; }

        /// <summary>
        /// A list of issues that will be returned inside a resulting search bundle
        /// </summary>
        IList<OperationOutcomeIssue> BundleIssues { get; }

        bool IncludePartiallyIndexedSearchParams { get; set; }

        /// <summary>
        /// Indicates whether this request is part of the execution of a batch or transaction request.
        /// </summary>
        bool ExecutingBatchOrTransaction { get; set; }

        /// <summary>
        /// Indicates whether this running as part of a background task instead of an HTTP request
        /// </summary>
        bool IsBackgroundTask { get; set; }

        /// <summary>
        /// A weakly-typed property bag that can be used for communication between components in the context of a request.
        /// </summary>
        IDictionary<string, object> Properties { get; }

        /// <summary>
        /// Data informing the service how to apply fine grained access control
        /// </summary>
        AccessControlContext AccessControlContext { get; }

        /// <summary>
        /// The logical partition ID for data partitioning. Null if partitioning is disabled.
        /// </summary>
        int? LogicalPartitionId { get; set; }

        /// <summary>
        /// The partition name extracted from the URL. Null if no partition specified.
        /// </summary>
        string PartitionName { get; set; }

        /// <summary>
        /// Gets the effective partition ID for the specified resource type.
        /// System resources always use the system partition (ID=1), while regular resources use the current partition context.
        /// </summary>
        /// <param name="resourceType">The resource type to get the partition ID for.</param>
        /// <returns>The effective partition ID for the resource type.</returns>
        int GetEffectivePartitionId(string resourceType = null);
    }
}
