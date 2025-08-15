// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Api.Features.Smart
{
    /// <summary>
    /// Middleware that runs after authentication middleware so the scopes field in the token can be examined for SMART on FHIR clinical scopes
    /// </summary>
    public class SmartClinicalScopesMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SmartClinicalScopesMiddleware> _logger;

        // Regex based on SMART on FHIR clinical scopes v1.0 and v2.0
        // v1: http://hl7.org/fhir/smart-app-launch/1.0.0/scopes-and-launch-context/index.html#clinical-scope-syntax
        // v2: http://hl7.org/fhir/smart-app-launch/scopes-and-launch-context/index.html#scopes-for-requesting-fhir-resources
        private static readonly Regex ClinicalScopeRegEx = new Regex(@"(^|\s+)(?<id>patient|user|system)(/|\$|\.)(?<resource>\*|([a-zA-Z]*)|all)\.(?<accessLevel>read|write|\*|all|[cruds]+)", RegexOptions.Compiled);

        public SmartClinicalScopesMiddleware(RequestDelegate next, ILogger<SmartClinicalScopesMiddleware> logger)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
            _next = next;
        }

        /// <summary>
        /// Parse SMART scope permissions supporting both v1 and v2 formats.
        /// v1: read, write, *, all
        /// v2: c (create), r (read), u (update), d (delete), s (search)
        /// </summary>
        /// <param name="accessLevel">The access level from the scope (e.g., "read", "rs", "cruds")</param>
        /// <returns>DataActions representing the permissions</returns>
        private static DataActions ParseScopePermissions(string accessLevel)
        {
            if (string.IsNullOrEmpty(accessLevel))
            {
                return DataActions.None;
            }

            // Handle v1 scope formats first for backward compatibility
            switch (accessLevel.ToLowerInvariant())
            {
                case "read":
                    // v1 read includes both read and search permissions
                    return DataActions.Read | DataActions.Export | DataActions.Search;
                case "write":
                    // v1 write includes create, update, delete, and legacy write permissions
                    return DataActions.Write | DataActions.Create | DataActions.Update | DataActions.Delete;
                case "*":
                case "all":
                    // Full access includes all permissions
                    return DataActions.Read | DataActions.Write | DataActions.Export | DataActions.Search |
                           DataActions.Create | DataActions.Update | DataActions.Delete;
            }

            // Handle v2 scope format (e.g., "rs", "cruds")
            var permissions = DataActions.None;
            foreach (char permission in accessLevel.ToLowerInvariant())
            {
                switch (permission)
                {
                    case 'c':
                        permissions |= DataActions.Create; // SMART v2 granular create permission
                        break;
                    case 'r':
                        permissions |= DataActions.ReadV2 | DataActions.Export; // SMART v2 read-only (no search)
                        break;
                    case 'u':
                        permissions |= DataActions.Update; // SMART v2 granular update permission
                        break;
                    case 'd':
                        permissions |= DataActions.Delete; // SMART v2 granular delete permission
                        break;
                    case 's':
                        permissions |= DataActions.Search; // Search is a separate permission in v2
                        break;
                    default:
                        // Unknown permission character - log warning but continue
                        break;
                }
            }

            return permissions;
        }

        public async Task Invoke(
            HttpContext context,
            RequestContextAccessor<IFhirRequestContext> fhirRequestContextAccessor,
            IOptions<SecurityConfiguration> securityConfigurationOptions,
            IAuthorizationService<DataActions> authorizationService)
        {
            EnsureArg.IsNotNull(fhirRequestContextAccessor, nameof(fhirRequestContextAccessor));
            EnsureArg.IsNotNull(securityConfigurationOptions, nameof(securityConfigurationOptions));

            var authorizationConfiguration = securityConfigurationOptions.Value.Authorization;

            if (fhirRequestContextAccessor.RequestContext.Principal != null
                && securityConfigurationOptions.Value.Enabled
                && (authorizationConfiguration.Enabled || authorizationConfiguration.EnableSmartWithoutAuth))
            {
                var fhirRequestContext = fhirRequestContextAccessor.RequestContext;
                var principal = fhirRequestContext.Principal;

                var dataActions = await authorizationService.CheckAccess(DataActions.Smart, context.RequestAborted);

                _logger.LogInformation("Smart Data Action is present {Smart}", dataActions.HasFlag(DataActions.Smart));

                var scopeRestrictions = new StringBuilder();
                scopeRestrictions.Append("Resource(s) allowed and permitted data actions on it are : ");

                // Only read and apply SMART clinical scopes if the user has the Smart Data action
                if (dataActions.HasFlag(DataActions.Smart))
                {
                    fhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = true;

                    bool includeFhirUserClaim = true;

                    // examine the scopes claim for any SMART on FHIR clinical scopes
                    DataActions permittedDataActions = 0;
                    string scopeClaims = string.Empty;

                    foreach (string singleScope in authorizationConfiguration.ScopesClaim)
                    {
                        foreach (Claim claim in principal.FindAll(singleScope))
                        {
                            scopeClaims += " " + string.Join(" ", claim.Value);
                        }
                    }

                    var matches = ClinicalScopeRegEx.Matches(scopeClaims);
                    foreach (Match match in matches)
                    {
                        fhirRequestContext.AccessControlContext.ClinicalScopes.Add(match.Value);

                        var id = match.Groups["id"]?.Value;
                        var resource = match.Groups["resource"]?.Value;
                        var accessLevel = match.Groups["accessLevel"]?.Value;

                        permittedDataActions = ParseScopePermissions(accessLevel);

                        if (!string.IsNullOrEmpty(resource)
                            && !string.IsNullOrEmpty(id))
                        {
                            if (resource.Equals("*", StringComparison.OrdinalIgnoreCase))
                            {
                                resource = KnownResourceTypes.All;
                            }

                            fhirRequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(resource, permittedDataActions, id));

                            scopeRestrictions.Append($" ( {resource}-{permittedDataActions} ) ");

                            if (string.Equals("system", id, StringComparison.OrdinalIgnoreCase))
                            {
                                includeFhirUserClaim = false; // we skip fhirUser claim for system scopes
                            }
                        }
                    }

                    _logger.LogInformation("Scope restrictions allowed are {ScopeRestriction}", scopeRestrictions);
                    _logger.LogInformation("FhirUserClaim is present {FhirUserClaim}", includeFhirUserClaim);

                    if (includeFhirUserClaim)
                    {
                        // Check if the "fhirUser" claim is present.
                        var fhirUser = principal.FindFirstValue(authorizationConfiguration.FhirUserClaim);
                        if (string.IsNullOrEmpty(fhirUser))
                        {
                            // The "fhirUser" claim is not present, check if the "extension_fhirUser" claim is present.
                            // Azure B2C will prefix the claim with "extension_" if the value is added to the user using a graph extension.
                            fhirUser = principal.FindFirstValue(authorizationConfiguration.ExtensionFhirUserClaim);
                        }

                        try
                        {
                            fhirRequestContext.AccessControlContext.FhirUserClaim = new System.Uri(fhirUser, UriKind.RelativeOrAbsolute);
                            FhirUserClaimParser.ParseFhirUserClaim(fhirRequestContext.AccessControlContext, authorizationConfiguration.ErrorOnMissingFhirUserClaim);
                        }
                        catch (UriFormatException)
                        {
                            if (authorizationConfiguration.ErrorOnMissingFhirUserClaim)
                            {
                                throw new BadHttpRequestException(string.Format(Api.Resources.FhirUserClaimMustBeURL, fhirUser));
                            }
                        }
                        catch (ArgumentNullException)
                        {
                            if (authorizationConfiguration.ErrorOnMissingFhirUserClaim)
                            {
                                throw new BadHttpRequestException(Api.Resources.FhirUserClaimCannotBeNull);
                            }
                        }
                    }
                }
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }
    }
}
