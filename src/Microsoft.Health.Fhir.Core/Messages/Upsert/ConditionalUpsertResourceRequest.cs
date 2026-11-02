// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Upsert
{
    /// <summary>
    /// Represents a conditional update request and its behavior when the search finds no match.
    /// </summary>
    public sealed class ConditionalUpsertResourceRequest : ConditionalResourceRequest<UpsertResourceResponse>
    {
        private static readonly string[] Capabilities = new string[1] { "conditionalUpdate = true" };

        /// <summary>
        /// Initializes a new instance of the <see cref="ConditionalUpsertResourceRequest"/> class.
        /// </summary>
        /// <param name="resource">The resource to upsert.</param>
        /// <param name="conditionalParameters">The conditional search parameters.</param>
        /// <param name="bundleResourceContext">The optional bundle resource context.</param>
        /// <param name="noMatchBehavior">How to save a resource with a client-supplied id when the conditional search finds no match.</param>
        /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="noMatchBehavior"/> is not a defined <see cref="ConditionalUpdateNoMatchBehavior"/> value.</exception>
        public ConditionalUpsertResourceRequest(
            ResourceElement resource,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            BundleResourceContext bundleResourceContext = null,
            ConditionalUpdateNoMatchBehavior noMatchBehavior = ConditionalUpdateNoMatchBehavior.Upsert)
            : base(resource.InstanceType, conditionalParameters, bundleResourceContext)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.EnumIsDefined(noMatchBehavior, nameof(noMatchBehavior));
            Resource = resource;
            NoMatchBehavior = noMatchBehavior;
        }

        /// <summary>
        /// Gets the resource to upsert.
        /// </summary>
        public ResourceElement Resource { get; }

        /// <summary>
        /// How the resource is saved when the search finds no match and the resource has a client-supplied id.
        /// </summary>
        public ConditionalUpdateNoMatchBehavior NoMatchBehavior { get; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;
    }
}
