// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Rest;
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
        private static readonly Regex ClinicalScopeRegEx = new Regex(
            @"(?:^|\s+)(?<id>patient|user|system)(?>/|\$|\.)(?<resource>\*|(?>[a-zA-Z]+)|all)\.(?<accessLevel>read|write|\*|all|(?>[cruds]+))(?:\?(?<searchParams>(?>[a-zA-Z0-9_\-]+=[^&\s]+)(?>&[a-zA-Z0-9_\-]+=[^&\s]+)*))?",
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

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
                        permissions |= DataActions.ReadById; // SMART v2 read-only (no search)
                        break;
                    case 'u':
                        permissions |= DataActions.Update; // SMART v2 granular update permission
                        break;
                    case 'd':
                        permissions |= DataActions.Delete; // SMART v2 granular delete permission
                        break;
                    case 's':
                        permissions |= DataActions.Search | DataActions.Export; // Search is a separate permission in v2
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
                    var scopeClaimsBuilder = new StringBuilder();
                    string scopeClaims = string.Empty;

                    foreach (string singleScope in authorizationConfiguration.ScopesClaim)
                    {
                        // To support SMART V2 Finer-grained resource constraints using search parameters in OpenIdDict we are replacing the search parameters with wild card *
                        // For example Patient/Observation.rd?category=blah will be Patient/Observation.rd?*
                        // We are storing the original scopes in raw_Scope
                        // If the raw_Scope is non empty then use that as a scopeClaims
                        // In all the other cases (including anything other than OpenIdDict) keep reading from all the possible scopes like scp, scope, roles
                        if (!string.IsNullOrEmpty(principal.FindFirstValue("raw_scope")))
                        {
                            scopeClaims = principal.FindFirstValue("raw_scope");
                            break;
                        }
                        else
                        {
                            foreach (Claim claim in principal.FindAll(singleScope))
                            {
                                scopeClaimsBuilder.Append(' ').Append(claim.Value);
                            }
                        }
                    }

                    if (string.IsNullOrEmpty(scopeClaims))
                    {
                        scopeClaims = scopeClaimsBuilder.ToString();
                    }

                    var matches = ClinicalScopeRegEx.Matches(scopeClaims);
                    bool smartV1AccessLevelUsed = false;
                    bool smartV2AccessLevelUsed = false;
                    foreach (Match match in matches)
                    {
                        var accessLevel = match.Groups["accessLevel"]?.Value;
                        if (string.IsNullOrEmpty(accessLevel))
                        {
                            continue;
                        }

                        // Detect v1 vs v2 based on the accessLevel value.
                        // v1 uses: "read", "write", "*", "all"
                        // v2 uses: letters from "cruds" (e.g., "c", "r", "u", "d", "s" or any combination)
                        if (accessLevel.Equals("read", StringComparison.OrdinalIgnoreCase) ||
                                           accessLevel.Equals("write", StringComparison.OrdinalIgnoreCase) ||
                                           accessLevel.Equals("*", StringComparison.OrdinalIgnoreCase) ||
                                           accessLevel.Equals("all", StringComparison.OrdinalIgnoreCase))
                        {
                            smartV1AccessLevelUsed = true;
                        }
                        else
                        {
                            smartV2AccessLevelUsed = true;
                        }

                        // If both types are detected, throw an error.
                        if (smartV1AccessLevelUsed && smartV2AccessLevelUsed)
                        {
                            throw new BadHttpRequestException(string.Format(Api.Resources.MixedSMARTV1AndV2ScopesAreNotAllowed));
                        }

                        fhirRequestContext.AccessControlContext.ClinicalScopes.Add(match.Value);
                        SearchParams smartScopeSearchParameters = new SearchParams();

                        var id = match.Groups["id"]?.Value;
                        var resource = match.Groups["resource"]?.Value;
                        permittedDataActions = ParseScopePermissions(accessLevel);

                        if (!string.IsNullOrEmpty(resource)
                            && !string.IsNullOrEmpty(id))
                        {
                            if (resource.Equals("*", StringComparison.OrdinalIgnoreCase))
                            {
                                resource = KnownResourceTypes.All;
                            }

                            // If Finer-grained resource constraints using search parameters present
                            if (match.Groups["searchParams"].Success)
                            {
                                smartScopeSearchParameters = new SearchParams();
                                var searchParamsString = match.Groups["searchParams"].Value;
                                var searchParamsPairs = searchParamsString.Split('&');

                                // iterate through each key-value pair and add them to the SearchParams
                                foreach (var parts in searchParamsPairs.Select(kvPair => kvPair.Split('=')).Where(parts => parts.Length == 2))
                                {
                                    smartScopeSearchParameters.Add(parts[0], parts[1]);
                                }

                                if (smartScopeSearchParameters.Parameters.Count > 0)
                                {
                                    fhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControlWithSearchParameters = true;
                                }
                            }

                            fhirRequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(resource, permittedDataActions, id, smartScopeSearchParameters.Parameters.Any() ? smartScopeSearchParameters : null));

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
