// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Partitions;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    public interface IFhirRequestContext : IRequestContext, ICloneable
    {
        /// <summary>
        /// Gets or sets the FHIR base URI, finalized after endpoint routing.
        /// </summary>
        new Uri BaseUri { get; set; }

        string ResourceType { get; set; }

        /// <summary>
        /// Gets or sets the configured partition identifier, or null for a partition-independent request.
        /// </summary>
        IPartitionInfo Partition { get; set; }

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
    }
}
