// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public interface ICapabilityStatementBuilder
    {
        /// <summary>
        /// Adds to `rest.interaction` section of capability statement.
        /// </summary>
        /// <remarks>This type of interaction is applicable for all type of resources.</remarks>>
        /// <param name="interaction">Type of interaction to add.</param>
        ICapabilityStatementBuilder AddGlobalInteraction(string interaction);

        /// <summary>
        /// Adds to `rest.searchParam` section of capability statement.
        /// </summary>
        /// <remarks>Search parameters that are supported for searching all resources for implementations to support and/or make use of - either references to ones defined in the specification, or additional ones defined for/by the implementation.</remarks>
        ICapabilityStatementBuilder AddGlobalSearchParameters();

        /// <summary>
        /// Apply <paramref name="action"/> to specific to <paramref name="resourceType"/> `rest.resource` section of capability statement.
        /// </summary>
        /// <param name="resourceType">Type of resource to apply <paramref name="action"/> to.</param>
        /// <param name="action">Action to apply to `rest.resource` section of capability statement.</param>
        ICapabilityStatementBuilder ApplyToResource(string resourceType, Action<ListedResourceComponent> action);

        /// <summary>
        /// Apply <paramref name="action"/> to capability statement.
        /// </summary>
        /// <param name="action">Action to apply to capability statement.</param>
        ICapabilityStatementBuilder Apply(Action<ListedCapabilityStatement> action);

        /// <summary>
        /// Populates capability statement with predefined default values.
        /// </summary>
        ICapabilityStatementBuilder PopulateDefaultResourceInteractions();

        /// <summary>
        /// Updates capability statement to latest supported search paramaters by checkin in memory storage for search parameters.
        /// </summary>
        ICapabilityStatementBuilder SyncSearchParametersAsync();

        /// <summary>
        /// Updates capability statement to lastest supported profiles by pulling them from database.
        /// </summary>
        /// <param name="disableCacheRefresh">Disable pull from database and check cached version in memory. Needed to prevent circular calls to sync profiles.</param>
        ICapabilityStatementBuilder SyncProfiles(bool disableCacheRefresh = false);

        /// <summary>
        /// Create json representation of capability statement.
        /// </summary>
        ITypedElement Build();
    }
}
