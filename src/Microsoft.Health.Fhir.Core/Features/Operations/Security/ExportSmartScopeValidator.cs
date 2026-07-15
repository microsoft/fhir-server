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
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
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
        private const string SystemScope = "system";

        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExportSmartScopeValidator"/> class.
        /// </summary>
        /// <param name="requestContextAccessor">The FHIR request context accessor.</param>
        public ExportSmartScopeValidator(RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            _requestContextAccessor = EnsureArg.IsNotNull(requestContextAccessor, nameof(requestContextAccessor));
        }

        /// <inheritdoc />
        public void ValidateCreateAccess(CreateExportRequest request)
        {
            EnsureArg.IsNotNull(request, nameof(request));

            ValidateResourceTypeAccess(GetRequiredResourceTypes(request.ResourceType));
        }

        /// <inheritdoc />
        public void ValidateJobAccess(ExportJobRecord exportJobRecord)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            HashSet<string> resourceTypes = GetRequiredResourceTypes(exportJobRecord.ResourceType);

            if (exportJobRecord.Output != null)
            {
                resourceTypes.UnionWith(exportJobRecord.Output.Keys.Where(type => !string.IsNullOrWhiteSpace(type)));
            }

            ValidateResourceTypeAccess(resourceTypes);
        }

        private static HashSet<string> GetRequiredResourceTypes(string resourceType)
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

        private void ValidateResourceTypeAccess(IReadOnlyCollection<string> requiredResourceTypes)
        {
            AccessControlContext accessControlContext = _requestContextAccessor.RequestContext?.AccessControlContext;
            if (accessControlContext?.ApplyFineGrainedAccessControl != true)
            {
                return;
            }

            ScopeRestriction[] systemScopes = accessControlContext.AllowedResourceActions?
                .Where(scope => string.Equals(scope.User, SystemScope, StringComparison.OrdinalIgnoreCase)
                    && scope.SearchParameters?.Parameters?.Any() != true)
                .ToArray()
                ?? Array.Empty<ScopeRestriction>();

            foreach (string requiredResourceType in requiredResourceTypes)
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
                if (!hasReadAccess || !allowedActions.HasFlag(DataActions.Export))
                {
                    throw new UnauthorizedFhirActionException();
                }
            }
        }
    }
}
