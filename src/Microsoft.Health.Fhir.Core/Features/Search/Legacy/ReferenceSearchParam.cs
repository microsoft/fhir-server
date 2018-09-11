// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.Legacy
{
    /// <summary>
    /// Provides information about a reference search parameter.
    /// </summary>
    internal class ReferenceSearchParam : SearchParam
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ReferenceSearchParam"/> class.
        /// </summary>
        /// <param name="resourceType">The resource type.</param>
        /// <param name="paramName">The parameter name.</param>
        /// <param name="parser">The parser used to parse the string representation of the search parameter.</param>
        /// <param name="targetResourceTypes">The target resource types this reference search param can be pointing to.</param>
        internal ReferenceSearchParam(
            Type resourceType,
            string paramName,
            SearchParamValueParser parser,
            IReadOnlyCollection<Type> targetResourceTypes)
            : base(resourceType, paramName, SearchParamType.Reference, parser)
        {
            EnsureArg.IsNotNull(targetResourceTypes, nameof(targetResourceTypes));
            Debug.Assert(targetResourceTypes.Any(), "The target resource type should not be empty.");
            Debug.Assert(
                targetResourceTypes.All(t => typeof(Resource).IsAssignableFrom(t)),
                "The target resource type should all be type of Resource.");

            TargetReferenceTypes = targetResourceTypes;
        }

        internal IReadOnlyCollection<Type> TargetReferenceTypes { get; }
    }
}
