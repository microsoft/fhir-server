// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
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
        private readonly CoreFeatureConfiguration _coreFeatureConfiguration;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportSmartScopeAuthorizer"/> class.
        /// </summary>
        /// <param name="requestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="coreFeatureConfiguration">The core feature configuration.</param>
        public ExportSmartScopeAuthorizer(
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            IOptions<CoreFeatureConfiguration> coreFeatureConfiguration)
        {
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _coreFeatureConfiguration = EnsureArg.IsNotNull(coreFeatureConfiguration?.Value, nameof(coreFeatureConfiguration));
        }

        /// <inheritdoc />
        public string AuthorizeCreateAndResolveResourceType(CreateExportRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            if (!ShouldRunAuthorizeSmartExportLogic())
            {
                return request.ResourceType;
            }

            ScopeRestriction[] systemScopes = GetUnconstrainedSystemScopes(_requestContextAccessor.RequestContext?.AccessControlContext);
            EnsureCompleteExportReadAccess(systemScopes, GetRouteRequiredResourceTypes(request.RequestType));

            List<string> explicitResourceTypes = ParseExplicitResourceTypes(request.ResourceType);

            if (explicitResourceTypes.Count > 0)
            {
                EnsureCompleteExportReadAccess(systemScopes, explicitResourceTypes);

                return string.Join(",", explicitResourceTypes);
            }

            // No explicit (or effectively empty) _type. A complete system wildcard leaves the export unconstrained.
            if (HasCompleteExportReadAccess(systemScopes, KnownResourceTypes.All))
            {
                return request.ResourceType;
            }

            // Otherwise, infer the narrowed effective _type from every unconstrained, resource-specific system
            // scope that independently provides complete export-read access. Fail closed if nothing is eligible.
            List<string> inferredResourceTypes = InferEligibleOutputResourceTypes(systemScopes);
            if (inferredResourceTypes.Count == 0)
            {
                throw new UnauthorizedFhirActionException();
            }

            return string.Join(",", inferredResourceTypes);
        }

        /// <inheritdoc />
        public void AuthorizeJobAccess(ExportJobRecord exportJobRecord)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            if (!ShouldRunAuthorizeSmartExportLogic())
            {
                return;
            }

            ScopeRestriction[] systemScopes = GetUnconstrainedSystemScopes(_requestContextAccessor.RequestContext?.AccessControlContext);

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

        /// <summary>
        /// Determines whether SMART export scope authorization applies to the current request.
        /// </summary>
        private bool ShouldRunAuthorizeSmartExportLogic()
        {
            return _coreFeatureConfiguration.EnableSmartExportScopeAuthorization
                && _requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true;
        }

        /// <summary>
        /// Returns system scopes without search-parameter constraints, since constrained scopes cannot authorize export.
        /// </summary>
        private static ScopeRestriction[] GetUnconstrainedSystemScopes(AccessControlContext accessControlContext)
        {
            return accessControlContext?.AllowedResourceActions?
                .Where(scope => string.Equals(scope.User, SystemScope, StringComparison.Ordinal)
                    && scope.SearchParameters?.Parameters?.Any() != true)
                .ToArray()
                ?? Array.Empty<ScopeRestriction>();
        }

        /// <summary>
        /// Returns the resource types that must be authorized for the export route itself, independent of any
        /// explicit or inferred output _type, per <see cref="ExportJobType"/>.
        /// </summary>
        private static string[] GetRouteRequiredResourceTypes(ExportJobType exportType)
        {
            return exportType switch
            {
                ExportJobType.Patient => new[] { KnownResourceTypes.Patient },
                ExportJobType.Group => new[] { KnownResourceTypes.Group, KnownResourceTypes.Patient },
                _ => Array.Empty<string>(),
            };
        }

        /// <summary>
        /// Parses an explicit, nonempty _type parameter into a deterministic, order-preserving, deduplicated list.
        /// </summary>
        private static List<string> ParseExplicitResourceTypes(string resourceType)
        {
            if (string.IsNullOrWhiteSpace(resourceType))
            {
                return new List<string>();
            }

            return resourceType
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
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
                .Where(resource => !string.Equals(resource, KnownResourceTypes.All, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Where(resourceType => HasCompleteExportReadAccess(systemScopes, resourceType))
                .OrderBy(resourceType => resourceType, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Parses the persisted output types, treating a missing value as an unconstrained all-resource export.
        /// </summary>
        private static HashSet<string> GetPersistedOutputResourceTypes(string resourceType)
        {
            var resourceTypes = new HashSet<string>(
                string.IsNullOrWhiteSpace(resourceType)
                    ? Array.Empty<string>()
                    : resourceType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                StringComparer.Ordinal);

            if (resourceTypes.Count == 0)
            {
                resourceTypes.Add(KnownResourceTypes.All);
            }

            return resourceTypes;
        }

        /// <summary>
        /// Throws when any required resource type lacks complete export-read access.
        /// </summary>
        private static void EnsureCompleteExportReadAccess(ScopeRestriction[] systemScopes, IReadOnlyCollection<string> requiredResourceTypes)
        {
            if (requiredResourceTypes.Any(requiredResourceType => !HasCompleteExportReadAccess(systemScopes, requiredResourceType)))
            {
                throw new UnauthorizedFhirActionException();
            }
        }

        /// <summary>
        /// Determines whether matching system scopes collectively grant SMART v1 or v2 read access plus export.
        /// </summary>
        private static bool HasCompleteExportReadAccess(ScopeRestriction[] systemScopes, string requiredResourceType)
        {
            bool requiresAllResources = string.Equals(
                requiredResourceType,
                KnownResourceTypes.All,
                StringComparison.Ordinal);
            DataActions allowedActions = systemScopes
                .Where(scope => string.Equals(scope.Resource, KnownResourceTypes.All, StringComparison.Ordinal)
                    || (!requiresAllResources
                        && string.Equals(scope.Resource, requiredResourceType, StringComparison.Ordinal)))
                .Aggregate(DataActions.None, (actions, scope) => actions | scope.AllowedDataAction);

            // SMART v1 uses read; SMART v2 requires the read-by-id and search pair.
            bool hasReadAccess = allowedActions.HasFlag(DataActions.Read)
                || (allowedActions.HasFlag(DataActions.ReadById)
                    && allowedActions.HasFlag(DataActions.Search));
            return hasReadAccess && allowedActions.HasFlag(DataActions.Export);
        }
    }
}
