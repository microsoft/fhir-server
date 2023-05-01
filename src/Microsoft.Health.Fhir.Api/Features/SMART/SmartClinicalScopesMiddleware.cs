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
        private const string AllDataActions = "all";
        private readonly ILogger<SmartClinicalScopesMiddleware> _logger;

        // Regex based on SMART on FHIR clinical scopes v1.0, http://hl7.org/fhir/smart-app-launch/1.0.0/scopes-and-launch-context/index.html#clinical-scope-syntax
        private static readonly Regex ClinicalScopeRegEx = new Regex(@"(^|\s+)(?<id>patient|user)(/|\$|\.)(?<resource>\*|([a-zA-Z]*)|all)\.(?<accessLevel>read|write|\*|all)", RegexOptions.Compiled);

        public SmartClinicalScopesMiddleware(RequestDelegate next, ILogger<SmartClinicalScopesMiddleware> logger)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _logger = logger;
            _next = next;
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

                        switch (accessLevel)
                        {
                            case "read":
                                permittedDataActions |= DataActions.Read;
                                break;
                            case "write":
                                permittedDataActions |= DataActions.Write;
                                break;
                            case "*":
                            case AllDataActions:
                                permittedDataActions |= DataActions.Read | DataActions.Write | DataActions.Export;
                                break;
                        }

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
                        var fhirUser = principal.FindFirstValue(authorizationConfiguration.FhirUserClaim);
                        if (string.IsNullOrEmpty(fhirUser))
                        {
                            // look for the fhirUser info in a header
                            if (context.Request.Headers.ContainsKey(KnownHeaders.FhirUserHeader)
                                && context.Request.Headers.TryGetValue(KnownHeaders.FhirUserHeader, out var hValue))
                            {
                                fhirUser = hValue.ToString();
                            }
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
                                throw new BadHttpRequestException(string.Format(Resources.FhirUserClaimMustBeURL, fhirUser));
                            }
                        }
                        catch (ArgumentNullException)
                        {
                            if (authorizationConfiguration.ErrorOnMissingFhirUserClaim)
                            {
                                throw new BadHttpRequestException(Resources.FhirUserClaimCannotBeNull);
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
