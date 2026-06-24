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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;

namespace Microsoft.Health.Fhir.Core.Features.Security.Authorization
{
    /// <summary>
    /// Helper that enforces SMART on FHIR compartment ownership for by-id operations.
    /// When fine-grained access control is active, a by-id read/write/delete must be restricted
    /// to resources that belong to the caller's compartment. This mirrors the compartment-scoped
    /// search performed by <see cref="Microsoft.Health.Fhir.Core.Features.Resources.Get.GetResourceHandler"/>.
    /// </summary>
    public static class SmartCompartmentResourceValidator
    {
        /// <summary>
        /// Ensures that the target resource is within the caller's SMART compartment when fine-grained
        /// access control is active. If the resource exists outside the compartment, a
        /// <see cref="ResourceNotFoundException"/> is thrown so the caller cannot read, modify or delete
        /// another compartment's data.
        /// </summary>
        /// <param name="searchService">The search service used to perform the compartment-scoped lookup.</param>
        /// <param name="contextAccessor">The request context accessor.</param>
        /// <param name="resourceType">The resource type being targeted.</param>
        /// <param name="resourceId">The id of the resource being targeted.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="fhirDataStore">
        /// Optional data store. When provided, a resource that is absent from the caller's compartment but does
        /// not exist at all is allowed through (e.g. update-as-create via PUT). When omitted, any resource that
        /// is not found in the compartment is rejected.
        /// </param>
        public static async Task EnsureResourceIsInCompartmentAsync(
            ISearchService searchService,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            string resourceType,
            string resourceId,
            CancellationToken cancellationToken,
            IFhirDataStore fhirDataStore = null)
        {
            EnsureArg.IsNotNull(searchService, nameof(searchService));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            // Only enforce compartment ownership when SMART fine-grained access control is active.
            if (contextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl != true)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(resourceType) || string.IsNullOrWhiteSpace(resourceId))
            {
                return;
            }

            var query = new List<Tuple<string, string>>
            {
                new Tuple<string, string>(KnownQueryParameterNames.Id, resourceId),
            };

            // The search service injects the SMART compartment restriction, so a resource owned by a
            // different compartment will not be returned here.
            SearchResult results = await searchService.SearchAsync(resourceType, query, cancellationToken);

            if (results?.Results != null && results.Results.Any())
            {
                return;
            }

            // The resource is not in the caller's compartment. When a data store is supplied, allow the
            // operation to continue only if the resource does not exist at all (a legitimate create).
            if (fhirDataStore != null)
            {
                ResourceWrapper existing = await fhirDataStore.GetAsync(new ResourceKey(resourceType, resourceId), cancellationToken);
                if (existing == null || existing.IsDeleted)
                {
                    return;
                }
            }

            throw new ResourceNotFoundException(string.Format(Core.Resources.ResourceNotFoundById, resourceType, resourceId));
        }
    }
}
