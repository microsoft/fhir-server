// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Core.Features.Search.SearchValues
{
    /// <summary>
    /// Represents a compartment search value.
    /// </summary>
    public class CompartmentSearchValue : ISearchValue
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CompartmentSearchValue"/> class.
        /// </summary>
        /// <param name="compartmentType">The compartment type.</param>
        /// <param name="resourceIds">The ResourceIds for the compartment</param>
        public CompartmentSearchValue(CompartmentType compartmentType, HashSet<string> resourceIds)
        {
            EnsureArg.IsNotNull<CompartmentType>(compartmentType, nameof(compartmentType));
            CompartmentType = compartmentType;
            ResourceIds = resourceIds;
        }

        public CompartmentType CompartmentType { get; private set; }

        /// <summary>
        /// Gets the resource ids.
        /// </summary>
        public HashSet<string> ResourceIds { get; }

        /// <inheritdoc />
        public bool IsValidAsCompositeComponent => true;

        /// <inheritdoc />
        public void AcceptVisitor(ISearchValueVisitor visitor)
        {
            EnsureArg.IsNotNull(visitor, nameof(visitor));

            visitor.Visit(this);
        }
    }
}
