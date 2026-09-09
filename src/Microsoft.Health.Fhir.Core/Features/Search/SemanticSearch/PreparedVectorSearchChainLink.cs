// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Describes one relationship traversed from a search root to the resource that owns a vector match.
    /// </summary>
    public sealed class PreparedVectorSearchChainLink
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PreparedVectorSearchChainLink"/> class.
        /// </summary>
        /// <param name="resourceTypes">The resource types that define the reference source.</param>
        /// <param name="referenceSearchParameter">The reference SearchParameter connecting the resources.</param>
        /// <param name="targetResourceTypes">The resource types targeted by the reference.</param>
        /// <param name="reversed">Whether the relationship is traversed using reverse chaining.</param>
        public PreparedVectorSearchChainLink(
            IReadOnlyCollection<string> resourceTypes,
            SearchParameterInfo referenceSearchParameter,
            IReadOnlyCollection<string> targetResourceTypes,
            bool reversed)
        {
            EnsureArg.IsNotNull(resourceTypes, nameof(resourceTypes));
            EnsureArg.IsNotNull(referenceSearchParameter, nameof(referenceSearchParameter));
            EnsureArg.IsNotNull(targetResourceTypes, nameof(targetResourceTypes));

            ResourceTypes = Array.AsReadOnly(resourceTypes.ToArray());
            ReferenceSearchParameter = referenceSearchParameter;
            TargetResourceTypes = Array.AsReadOnly(targetResourceTypes.ToArray());
            Reversed = reversed;
        }

        /// <summary>Gets the resource types that define the reference source.</summary>
        public IReadOnlyList<string> ResourceTypes { get; }

        /// <summary>Gets the reference SearchParameter connecting the resources.</summary>
        public SearchParameterInfo ReferenceSearchParameter { get; }

        /// <summary>Gets the resource types targeted by the reference.</summary>
        public IReadOnlyList<string> TargetResourceTypes { get; }

        /// <summary>Gets a value indicating whether the relationship is traversed using reverse chaining.</summary>
        public bool Reversed { get; }
    }
}
