// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.SourceNodes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Ignixa;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    /// <summary>
    /// Extension methods for <see cref="ResourceElement"/> to support Ignixa integration.
    /// </summary>
    public static class ResourceElementIgnixaExtensions
    {
        /// <summary>
        /// Gets the underlying <see cref="ResourceJsonNode"/> if this element was created from Ignixa parsing.
        /// </summary>
        /// <param name="resource">The resource element.</param>
        /// <returns>
        /// The <see cref="ResourceJsonNode"/> if the resource was created from Ignixa parsing; otherwise null.
        /// </returns>
        /// <remarks>
        /// When a ResourceElement is created from an <see cref="IgnixaResourceElement"/>,
        /// the ResourceJsonNode is preserved for efficient serialization without POCO conversion.
        /// ResourceInstance may be either the ResourceJsonNode directly or an IgnixaResourceElement wrapper.
        /// </remarks>
        public static ResourceJsonNode GetIgnixaNode(this ResourceElement resource)
        {
            // Check for ResourceJsonNode directly
            if (resource.ResourceInstance is ResourceJsonNode node)
            {
                return node;
            }

            // Check for IgnixaResourceElement wrapper
            if (resource.ResourceInstance is IgnixaResourceElement ignixaElement)
            {
                return ignixaElement.ResourceNode;
            }

            return null;
        }
    }
}
