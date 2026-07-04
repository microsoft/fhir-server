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
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Default <see cref="IAsyncOperationSmartScopeValidator"/> implementation. It inspects the
    /// SMART fine-grained access control context on the current request to decide whether the
    /// caller's system scopes are sufficient to read/cancel an asynchronous operation.
    /// </summary>
    public class AsyncOperationSmartScopeValidator : IAsyncOperationSmartScopeValidator
    {
        // SMART scopes are prefixed with the access level of the granted context: "patient", "user" or "system".
        // Only "system" scopes are honored for asynchronous operation authorization.
        private const string SystemScopeUser = "system";

        private readonly RequestContextAccessor<IFhirRequestContext> _fhirRequestContextAccessor;

        public AsyncOperationSmartScopeValidator(RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor)
        {
            _fhirRequestContextAccessor = EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
        }

        /// <inheritdoc />
        public bool ValidateExportStatusAccess(ExportJobRecord exportJobRecord)
        {
            EnsureArg.IsNotNull(exportJobRecord, nameof(exportJobRecord));

            AccessControlContext accessControlContext = GetSmartRestrictedAccessControlContextOrNull();
            if (accessControlContext == null)
            {
                // Not a SMART fine-grained restricted request. Preserve existing non-SMART/admin behavior.
                return false;
            }

            IReadOnlyCollection<string> requiredResourceTypes = DetermineExportRequiredResourceTypes(exportJobRecord);

            if (requiredResourceTypes.Count == 0)
            {
                // No specific resource type could be determined. Any system export read scope is sufficient.
                if (accessControlContext.AllowedResourceActions.Any(scope => IsSystemScope(scope) && !IsSearchConstrainedScope(scope) && IsExportReadScope(scope.AllowedDataAction)))
                {
                    return true;
                }

                throw new UnauthorizedFhirActionException();
            }

            foreach (string requiredResourceType in requiredResourceTypes)
            {
                if (!IsExportReadScopeSatisfied(accessControlContext, requiredResourceType))
                {
                    throw new UnauthorizedFhirActionException();
                }
            }

            return true;
        }

        /// <inheritdoc />
        public bool ValidateAllResourceReadWriteAccess()
        {
            AccessControlContext accessControlContext = GetSmartRestrictedAccessControlContextOrNull();
            if (accessControlContext == null)
            {
                // Not a SMART fine-grained restricted request. Preserve existing non-SMART/admin behavior.
                return false;
            }

            // The read and write grants may be expressed as separate scope entries (e.g. system/*.read and system/*.write).
            DataActions allResourceActions = GetAllowedSystemActionsForResource(accessControlContext, KnownResourceTypes.All);
            bool hasAllResourceRead = IsReadScope(allResourceActions);
            bool hasAllResourceWrite = IsWriteScope(allResourceActions);

            if (!hasAllResourceRead || !hasAllResourceWrite)
            {
                throw new UnauthorizedFhirActionException();
            }

            return true;
        }

        private AccessControlContext GetSmartRestrictedAccessControlContextOrNull()
        {
            AccessControlContext accessControlContext = _fhirRequestContextAccessor.RequestContext?.AccessControlContext;

            if (accessControlContext == null
                || !accessControlContext.ApplyFineGrainedAccessControl
                || accessControlContext.AllowedResourceActions == null
                || accessControlContext.AllowedResourceActions.Count == 0)
            {
                return null;
            }

            return accessControlContext;
        }

        private static IReadOnlyCollection<string> DetermineExportRequiredResourceTypes(ExportJobRecord exportJobRecord)
        {
            var requiredResourceTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hasExplicitTypeMetadata = false;

            // 1. Completed job output keys when present.
            if (exportJobRecord.Output != null && exportJobRecord.Output.Count > 0)
            {
                foreach (string outputResourceType in exportJobRecord.Output.Keys.Where(key => !string.IsNullOrWhiteSpace(key)))
                {
                    requiredResourceTypes.Add(outputResourceType);
                }
            }

            // 2. The explicit resource type filter (_type) when present. This can be a comma-delimited list.
            if (!string.IsNullOrWhiteSpace(exportJobRecord.ResourceType))
            {
                foreach (string resourceType in exportJobRecord.ResourceType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    requiredResourceTypes.Add(resourceType);
                    hasExplicitTypeMetadata = true;
                }
            }

            // 3. Type filters (_typeFilter) when present.
            if (exportJobRecord.Filters != null && exportJobRecord.Filters.Count > 0)
            {
                foreach (string filterResourceType in exportJobRecord.Filters.Select(filter => filter.ResourceType).Where(resourceType => !string.IsNullOrWhiteSpace(resourceType)))
                {
                    requiredResourceTypes.Add(filterResourceType);
                    hasExplicitTypeMetadata = true;
                }
            }

            if (hasExplicitTypeMetadata || (requiredResourceTypes.Count > 0 && exportJobRecord.Status == OperationStatus.Completed))
            {
                return requiredResourceTypes.ToList();
            }

            // 4. When no explicit type metadata is present, incomplete broad exports must use the export type's implicit scope.
            switch (exportJobRecord.ExportType)
            {
                case ExportJobType.All:
                    // A system-level export requires access to all resources.
                    return new[] { KnownResourceTypes.All };
                case ExportJobType.Patient:
                case ExportJobType.Group:
                    // Without output or explicit type narrowing, patient/group exports may include compartment resources.
                    return new[] { KnownResourceTypes.All };
                default:
                    // Unknown export type - nothing specific to constrain against.
                    return Array.Empty<string>();
            }
        }

        private static bool IsExportReadScopeSatisfied(AccessControlContext accessControlContext, string requiredResourceType)
        {
            DataActions allowedDataActions = GetAllowedSystemActionsForResource(accessControlContext, requiredResourceType);

            return IsExportReadScope(allowedDataActions);
        }

        private static bool IsSystemScope(ScopeRestriction scope)
        {
            return string.Equals(scope.User, SystemScopeUser, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAllResourceScope(ScopeRestriction scope)
        {
            // An all-resource scope must be a SMART system scope (e.g. system/*.read), not patient/user scoped.
            return string.Equals(scope.Resource, KnownResourceTypes.All, StringComparison.OrdinalIgnoreCase)
                && IsSystemScope(scope)
                && !IsSearchConstrainedScope(scope);
        }

        private static DataActions GetAllowedSystemActionsForResource(AccessControlContext accessControlContext, string requiredResourceType)
        {
            bool requiresAllResources = string.Equals(requiredResourceType, KnownResourceTypes.All, StringComparison.OrdinalIgnoreCase);
            DataActions allowedDataActions = DataActions.None;

            foreach (ScopeRestriction scope in accessControlContext.AllowedResourceActions)
            {
                // Only unconstrained SMART system scopes are honored for asynchronous operation authorization.
                if (!IsSystemScope(scope) || IsSearchConstrainedScope(scope))
                {
                    continue;
                }

                if (IsAllResourceScope(scope)
                    || (!requiresAllResources && string.Equals(scope.Resource, requiredResourceType, StringComparison.OrdinalIgnoreCase)))
                {
                    allowedDataActions |= scope.AllowedDataAction;
                }
            }

            return allowedDataActions;
        }

        private static bool IsSearchConstrainedScope(ScopeRestriction scope)
        {
            return scope.SearchParameters?.Parameters?.Any() == true;
        }

        private static bool IsReadScope(DataActions allowedDataAction)
        {
            // SMART v1 read grants the legacy Read action.
            bool v1Read = allowedDataAction.HasFlag(DataActions.Read);

            // SMART v2 read grants require both 'r' (ReadById) and 's' (Search).
            bool v2Read = allowedDataAction.HasFlag(DataActions.ReadById) && allowedDataAction.HasFlag(DataActions.Search);

            return v1Read || v2Read;
        }

        private static bool IsExportReadScope(DataActions allowedDataAction)
        {
            return IsReadScope(allowedDataAction) && allowedDataAction.HasFlag(DataActions.Export);
        }

        private static bool IsWriteScope(DataActions allowedDataAction)
        {
            // SMART v1 write grants the legacy Write action.
            bool v1Write = allowedDataAction.HasFlag(DataActions.Write);

            // SMART v2 write grants require 'c' (Create), 'u' (Update) and 'd' (Delete).
            bool v2Write = allowedDataAction.HasFlag(DataActions.Create)
                && allowedDataAction.HasFlag(DataActions.Update)
                && allowedDataAction.HasFlag(DataActions.Delete);

            return v1Write || v2Write;
        }
    }
}
