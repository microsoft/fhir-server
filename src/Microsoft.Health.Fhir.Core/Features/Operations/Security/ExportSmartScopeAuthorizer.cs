// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Default <see cref="IExportSmartScopeAuthorizer"/> implementation.
    /// </summary>
    public class ExportSmartScopeAuthorizer : IExportSmartScopeAuthorizer
    {
        private const string SystemScope = "system";

        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportSmartScopeAuthorizer"/> class.
        /// </summary>
        /// <param name="requestContextAccessor">The FHIR request context accessor.</param>
        public ExportSmartScopeAuthorizer(RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
        }

        /// <inheritdoc />
        public string AuthorizeCreateAndResolveResourceType(CreateExportRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            AccessControlContext accessControlContext = _requestContextAccessor.RequestContext?.AccessControlContext;
            ScopeRestriction[] systemScopes = GetUnconstrainedSystemScopes(accessControlContext);
            HashSet<string> routeResourceTypes = GetRouteRequiredResourceTypes(request.RequestType);
            List<string> explicitResourceTypes = ParseExplicitResourceTypes(request.ResourceType);

            if (explicitResourceTypes.Count > 0)
            {
                // Route selection requirements (e.g. Group needing Group+Patient access) apply in addition to,
                // and independently of, the explicit output types requested via _type.
                var requiredResourceTypes = new HashSet<string>(routeResourceTypes, StringComparer.OrdinalIgnoreCase);
                requiredResourceTypes.UnionWith(explicitResourceTypes);

                EnsureCompleteExportReadAccess(systemScopes, requiredResourceTypes);

                return string.Join(",", explicitResourceTypes);
            }

            // No explicit (or effectively empty) _type. A complete system wildcard leaves the export unconstrained.
            if (HasCompleteExportReadAccess(systemScopes, KnownResourceTypes.All))
            {
                EnsureCompleteExportReadAccess(systemScopes, routeResourceTypes);
                return request.ResourceType;
            }

            // Otherwise, infer the narrowed effective _type from every unconstrained, resource-specific system
            // scope that independently provides complete export-read access. Fail closed if nothing is eligible.
            List<string> inferredResourceTypes = InferEligibleOutputResourceTypes(systemScopes);
            if (inferredResourceTypes.Count == 0)
            {
                throw new UnauthorizedFhirActionException();
            }

            EnsureCompleteExportReadAccess(systemScopes, routeResourceTypes);

            return string.Join(",", inferredResourceTypes);
        }

        /// <inheritdoc />
        public void AuthorizeJobAccess(ExportJobRecord exportJobRecord)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            AccessControlContext accessControlContext = _requestContextAccessor.RequestContext?.AccessControlContext;
            ScopeRestriction[] systemScopes = GetUnconstrainedSystemScopes(accessControlContext);

            HashSet<string> requiredResourceTypes = GetPersistedOutputResourceTypes(exportJobRecord.ResourceType);

            if (exportJobRecord.Output != null)
            {
                requiredResourceTypes.UnionWith(exportJobRecord.Output.Keys.Where(type => !string.IsNullOrWhiteSpace(type)));
            }

            // Route selection requirements are derived from the persisted ExportType, independently of the
            // persisted _type / completed output resource types.
            requiredResourceTypes.UnionWith(GetRouteRequiredResourceTypes(exportJobRecord.ExportType));

            EnsureCompleteExportReadAccess(systemScopes, requiredResourceTypes);
        }

        private static ScopeRestriction[] GetUnconstrainedSystemScopes(AccessControlContext accessControlContext)
        {
            return accessControlContext.AllowedResourceActions?
                .Where(scope => string.Equals(scope.User, SystemScope, StringComparison.OrdinalIgnoreCase)
                    && scope.SearchParameters?.Parameters?.Any() != true)
                .ToArray()
                ?? Array.Empty<ScopeRestriction>();
        }

        /// <summary>
        /// Returns the resource types that must be authorized for the export route itself, independent of any
        /// explicit or inferred output _type, per <see cref="ExportJobType"/>.
        /// </summary>
        private static HashSet<string> GetRouteRequiredResourceTypes(ExportJobType exportType)
        {
            switch (exportType)
            {
                case ExportJobType.Patient:
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { KnownResourceTypes.Patient };
                case ExportJobType.Group:
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase) { KnownResourceTypes.Group, KnownResourceTypes.Patient };
                default:
                    return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Parses an explicit, nonempty _type parameter into a deterministic, order-preserving, deduplicated list.
        /// </summary>
        private static List<string> ParseExplicitResourceTypes(string resourceType)
        {
            var explicitResourceTypes = new List<string>();
            if (string.IsNullOrWhiteSpace(resourceType))
            {
                return explicitResourceTypes;
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string type in resourceType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (seen.Add(type))
                {
                    explicitResourceTypes.Add(type);
                }
            }

            return explicitResourceTypes;
        }

        /// <summary>
        /// Infers the eligible, deterministic output resource types from every resource-specific
        /// (non-wildcard) unconstrained system scope whose aggregated matching actions provide complete
        /// export-read access.
        /// </summary>
        private static List<string> InferEligibleOutputResourceTypes(ScopeRestriction[] systemScopes)
        {
            return systemScopes
                .Select(scope => scope.Resource)
                .Where(resource => !string.Equals(resource, KnownResourceTypes.All, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(resourceType => HasCompleteExportReadAccess(systemScopes, resourceType))
                .OrderBy(resourceType => resourceType, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static HashSet<string> GetPersistedOutputResourceTypes(string resourceType)
        {
            var resourceTypes = new HashSet<string>(
                string.IsNullOrWhiteSpace(resourceType)
                    ? Array.Empty<string>()
                    : resourceType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.OrdinalIgnoreCase);

            if (resourceTypes.Count == 0)
            {
                resourceTypes.Add(KnownResourceTypes.All);
            }

            return resourceTypes;
        }

        private static void EnsureCompleteExportReadAccess(ScopeRestriction[] systemScopes, IReadOnlyCollection<string> requiredResourceTypes)
        {
            foreach (string requiredResourceType in requiredResourceTypes)
            {
                if (!HasCompleteExportReadAccess(systemScopes, requiredResourceType))
                {
                    throw new UnauthorizedFhirActionException();
                }
            }
        }

        private static bool HasCompleteExportReadAccess(ScopeRestriction[] systemScopes, string requiredResourceType)
        {
            bool requiresAllResources = string.Equals(
                requiredResourceType,
                KnownResourceTypes.All,
                StringComparison.OrdinalIgnoreCase);
            DataActions allowedActions = systemScopes
                .Where(scope => string.Equals(scope.Resource, KnownResourceTypes.All, StringComparison.OrdinalIgnoreCase)
                    || (!requiresAllResources
                        && string.Equals(scope.Resource, requiredResourceType, StringComparison.OrdinalIgnoreCase)))
                .Aggregate(DataActions.None, (actions, scope) => actions | scope.AllowedDataAction);

            // SMART v1 uses read; SMART v2 requires the read-by-id and search pair.
            bool hasReadAccess = allowedActions.HasFlag(DataActions.Read)
                || (allowedActions.HasFlag(DataActions.ReadById)
                    && allowedActions.HasFlag(DataActions.Search));
            return hasReadAccess && allowedActions.HasFlag(DataActions.Export);
        }
    }
}
