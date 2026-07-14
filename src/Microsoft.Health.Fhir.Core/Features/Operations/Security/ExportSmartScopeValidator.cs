// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Messages.Export;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Default <see cref="IExportSmartScopeValidator"/> implementation.
    /// </summary>
    public class ExportSmartScopeValidator : IExportSmartScopeValidator
    {
        private const string PatientScope = "patient";
        private const string UserScope = "user";
        private const string SystemScope = "system";
        private static readonly Regex FhirIdRegex = new Regex(
            "^[A-Za-z0-9\\-\\.]{1,64}$",
            RegexOptions.Compiled);

        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private readonly ISearchService _searchService;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportSmartScopeValidator"/> class.
        /// </summary>
        /// <param name="requestContextAccessor">The FHIR request context accessor.</param>
        /// <param name="searchService">The FHIR search service.</param>
        public ExportSmartScopeValidator(
            RequestContextAccessor<IFhirRequestContext> requestContextAccessor,
            ISearchService searchService)
        {
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
            _searchService = EnsureArg.IsNotNull(searchService, nameof(searchService));
        }

        /// <inheritdoc />
        public async Task<bool> ValidateCreateAccessAsync(CreateExportRequest request, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            AccessControlContext accessControlContext = GetSmartRestrictedAccessControlContextOrNull();
            if (accessControlContext == null)
            {
                return false;
            }

            string scopeContext = GetScopeContext(accessControlContext);
            IReadOnlyCollection<string> requestedResourceTypes = GetExplicitResourceTypes(request.ResourceType);
            bool bindToSmartCompartment = scopeContext == PatientScope || scopeContext == UserScope;

            if (bindToSmartCompartment)
            {
                ValidatePatientInstanceRequest(request, requestedResourceTypes);
                ValidateCompartmentContext(accessControlContext);

                if (scopeContext == PatientScope)
                {
                    if (!string.Equals(accessControlContext.CompartmentResourceType, KnownResourceTypes.Patient, StringComparison.Ordinal)
                        || !string.Equals(accessControlContext.CompartmentId, request.PatientId, StringComparison.Ordinal))
                    {
                        throw new UnauthorizedFhirActionException();
                    }
                }
                else
                {
                    await ValidatePatientInUserCompartmentAsync(accessControlContext, request.PatientId, cancellationToken);
                }
            }

            ValidateResourceTypeAccess(accessControlContext, requestedResourceTypes, scopeContext);
            return bindToSmartCompartment;
        }

        /// <inheritdoc />
        public void ValidateJobAccess(ExportJobRecord exportJobRecord)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            AccessControlContext accessControlContext = GetSmartRestrictedAccessControlContextOrNull();
            if (accessControlContext == null)
            {
                return;
            }

            string scopeContext = GetScopeContext(accessControlContext);
            IReadOnlyCollection<string> requiredResourceTypes = GetRequiredResourceTypes(exportJobRecord);

            if (scopeContext == PatientScope || scopeContext == UserScope)
            {
                ValidateCompartmentContext(accessControlContext);
                if (string.IsNullOrWhiteSpace(exportJobRecord.SmartCompartmentResourceType)
                    || string.IsNullOrWhiteSpace(exportJobRecord.SmartCompartmentId)
                    || !string.Equals(accessControlContext.CompartmentResourceType, exportJobRecord.SmartCompartmentResourceType, StringComparison.Ordinal)
                    || !string.Equals(accessControlContext.CompartmentId, exportJobRecord.SmartCompartmentId, StringComparison.Ordinal))
                {
                    throw new UnauthorizedFhirActionException();
                }
            }
            else if (!string.IsNullOrWhiteSpace(exportJobRecord.SmartCompartmentResourceType)
                || !string.IsNullOrWhiteSpace(exportJobRecord.SmartCompartmentId))
            {
                // System scopes are not bound to a clinical compartment, but they may read jobs
                // created in one when they cover every exported resource type.
            }

            ValidateResourceTypeAccess(accessControlContext, requiredResourceTypes, scopeContext);
        }

        private AccessControlContext GetSmartRestrictedAccessControlContextOrNull()
        {
            AccessControlContext accessControlContext = _requestContextAccessor.RequestContext?.AccessControlContext;
            if (accessControlContext?.ApplyFineGrainedAccessControl != true
                || accessControlContext.AllowedResourceActions == null
                || accessControlContext.AllowedResourceActions.Count == 0)
            {
                return null;
            }

            return accessControlContext;
        }

        private async Task ValidatePatientInUserCompartmentAsync(
            AccessControlContext accessControlContext,
            string patientId,
            CancellationToken cancellationToken)
        {
            SearchResult result = await _searchService.SearchCompartmentAsync(
                accessControlContext.CompartmentResourceType,
                accessControlContext.CompartmentId,
                KnownResourceTypes.Patient,
                new[]
                {
                    Tuple.Create(KnownQueryParameterNames.Id, patientId),
                    Tuple.Create(KnownQueryParameterNames.Count, "0"),
                },
                cancellationToken,
                isAsyncOperation: false,
                useSmartCompartmentDefinition: true);

            if (result.TotalCount.GetValueOrDefault() == 0)
            {
                throw new UnauthorizedFhirActionException();
            }
        }

        private static void ValidatePatientInstanceRequest(
            CreateExportRequest request,
            IReadOnlyCollection<string> requestedResourceTypes)
        {
            if (request.RequestType != ExportJobType.Patient
                || string.IsNullOrWhiteSpace(request.PatientId)
                || !FhirIdRegex.IsMatch(request.PatientId)
                || requestedResourceTypes.Count == 0)
            {
                throw new UnauthorizedFhirActionException();
            }
        }

        private static void ValidateCompartmentContext(AccessControlContext accessControlContext)
        {
            if (string.IsNullOrWhiteSpace(accessControlContext.CompartmentResourceType)
                || string.IsNullOrWhiteSpace(accessControlContext.CompartmentId))
            {
                throw new UnauthorizedFhirActionException();
            }
        }

        private static string GetScopeContext(AccessControlContext accessControlContext)
        {
            if (accessControlContext.AllowedResourceActions.Any(scope => IsScopeContext(scope, SystemScope)))
            {
                return SystemScope;
            }

            if (accessControlContext.AllowedResourceActions.Any(scope => IsScopeContext(scope, PatientScope)))
            {
                return PatientScope;
            }

            if (accessControlContext.AllowedResourceActions.Any(scope => IsScopeContext(scope, UserScope)))
            {
                return UserScope;
            }

            throw new UnauthorizedFhirActionException();
        }

        private static string[] GetExplicitResourceTypes(string resourceType)
        {
            if (string.IsNullOrWhiteSpace(resourceType))
            {
                return Array.Empty<string>();
            }

            return resourceType
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
        }

        private static HashSet<string> GetRequiredResourceTypes(ExportJobRecord exportJobRecord)
        {
            var resourceTypes = new HashSet<string>(GetExplicitResourceTypes(exportJobRecord.ResourceType), StringComparer.Ordinal);

            if (exportJobRecord.Output != null)
            {
                foreach (string resourceType in exportJobRecord.Output.Keys.Where(type => !string.IsNullOrWhiteSpace(type)))
                {
                    resourceTypes.Add(resourceType);
                }
            }

            if (resourceTypes.Count == 0)
            {
                resourceTypes.Add(KnownResourceTypes.All);
            }

            return resourceTypes;
        }

        private static void ValidateResourceTypeAccess(
            AccessControlContext accessControlContext,
            IReadOnlyCollection<string> requiredResourceTypes,
            string scopeContext)
        {
            if (requiredResourceTypes.Count == 0)
            {
                requiredResourceTypes = new[] { KnownResourceTypes.All };
            }

            foreach (string requiredResourceType in requiredResourceTypes)
            {
                DataActions allowedActions = DataActions.None;
                bool requiresAllResources = string.Equals(requiredResourceType, KnownResourceTypes.All, StringComparison.Ordinal);

                foreach (ScopeRestriction scope in accessControlContext.AllowedResourceActions)
                {
                    if (!IsScopeContext(scope, scopeContext)
                        || scope.SearchParameters?.Parameters?.Any() == true)
                    {
                        continue;
                    }

                    bool scopeMatches = string.Equals(scope.Resource, KnownResourceTypes.All, StringComparison.Ordinal)
                        || (!requiresAllResources && string.Equals(scope.Resource, requiredResourceType, StringComparison.Ordinal));
                    if (scopeMatches)
                    {
                        allowedActions |= scope.AllowedDataAction;
                    }
                }

                if (!IsExportReadScope(allowedActions))
                {
                    throw new UnauthorizedFhirActionException();
                }
            }
        }

        private static bool IsScopeContext(ScopeRestriction scopeRestriction, string scopeContext)
        {
            return string.Equals(scopeRestriction.User, scopeContext, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExportReadScope(DataActions allowedDataAction)
        {
            bool smartV1Read = allowedDataAction.HasFlag(DataActions.Read);
            bool smartV2ReadSearch = allowedDataAction.HasFlag(DataActions.ReadById)
                && allowedDataAction.HasFlag(DataActions.Search);

            return (smartV1Read || smartV2ReadSearch)
                && allowedDataAction.HasFlag(DataActions.Export);
        }
    }
}
