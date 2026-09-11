// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch
{
    /// <summary>
    /// Resolves direct text selected by vector SearchParameters.
    /// </summary>
    public sealed class VectorTextSourceResolver : IVectorTextSourceResolver
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="VectorTextSourceResolver"/> class.
        /// </summary>
        public VectorTextSourceResolver()
        {
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<VectorTextSource>> ResolveAsync(
            ResourceWrapper owner,
            SearchParameterInfo searchParameter,
            IReadOnlyList<string> extractedValues,
            IReadOnlyCollection<ResourceWrapper> writeBatch,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(owner, nameof(owner));
            EnsureArg.IsNotNull(searchParameter, nameof(searchParameter));
            EnsureArg.IsNotNull(extractedValues, nameof(extractedValues));
            EnsureArg.IsNotNull(writeBatch, nameof(writeBatch));

            if (searchParameter.VectorConfig.SourceStrategy != VectorTextSourceStrategy.DirectText)
            {
                throw new InvalidOperationException($"Unsupported vector text source strategy '{searchParameter.VectorConfig.SourceStrategy}'.");
            }

            IReadOnlyList<VectorTextSource> sources = extractedValues
                .Select(value => new VectorTextSource(value, owner.ResourceTypeName, owner.ResourceId, owner.Version, searchParameter.Expression))
                .ToList();

            return Task.FromResult(sources);
        }
    }
}
