// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Utility;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core.Features.Validation.FhirPrimitiveTypes;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Api.Features.Smart
{
    /// <summary>
    /// Middleware that runs after authentication middleware so the scopes field in the token can be examined for SMART on FHIR clinical scopes
    /// </summary>
    public class SmartClinicalScopesMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<SmartClinicalScopesMiddleware> _logger;
        private readonly ResourceIdPolicy _resourceIdPolicy;

        // Regex based on SMART on FHIR clinical scopes v1.0 and v2.0
        // v1: http://hl7.org/fhir/smart-app-launch/1.0.0/scopes-and-launch-context/index.html#clinical-scope-syntax
        // v2: http://hl7.org/fhir/smart-app-launch/scopes-and-launch-context/index.html#scopes-for-requesting-fhir-resources
        // Note: search parameter names include ':' and '.' so the full key is captured for chained params
        // (e.g., subject.name), type/reverse-chaining (e.g., subject:Patient, _has:Observation:patient:code)
        // and detection of FHIR search modifiers (e.g., category:in, code:text, category:not).
        // The search parameters are matched while still percent-encoded, so keys may contain '%' escapes; each key and
        // value is decoded only after the query is split on its raw '&' and '=' delimiters.
        private static readonly Regex ClinicalScopeRegEx = new Regex(
            @"(?:^|\s+)(?<id>patient|user|system)(?>/|\$|\.)(?<resource>\*|(?>[a-zA-Z]+)|all)\.(?<accessLevel>read|write|\*|all|(?>[cruds]+))(?:\?(?<searchParams>(?>[a-zA-Z0-9_\-:.%]+=[^&\s]+)(?>&[a-zA-Z0-9_\-:.%]+=[^&\s]+)*))?",
            RegexOptions.Compiled,
            TimeSpan.FromMilliseconds(100));

        // FHIR search modifiers built from the SearchModifierCode enum: https://hl7.org/fhir/R4/codesystem-search-modifier-code.html
        // The "iterate" (R4) and "recurse" (STU3) literals are the _include/_revinclude modifiers, which are not part of
        // the SearchModifierCode value set, so they are added explicitly to ensure scopes like "_include:iterate=..." are rejected.
        private static readonly HashSet<string> KnownSearchModifiers = new HashSet<string>(
            Enum.GetValues<SearchModifierCode>()
                .Select(m => m.GetLiteral())
                .Concat(new[] { "iterate", "recurse" }),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes the middleware with the standard FHIR resource id policy.
        /// </summary>
        /// <param name="next">The next middleware delegate.</param>
        /// <param name="logger">The middleware logger.</param>
        public SmartClinicalScopesMiddleware(RequestDelegate next, ILogger<SmartClinicalScopesMiddleware> logger)
            : this(next, logger, ResourceIdPolicy.Standard)
        {
        }

        /// <summary>
        /// Initializes the middleware with the configured resource id policy.
        /// </summary>
        /// <param name="next">The next middleware delegate.</param>
        /// <param name="logger">The middleware logger.</param>
        /// <param name="resourceIdPolicy">The resource id validation policy.</param>
        public SmartClinicalScopesMiddleware(RequestDelegate next, ILogger<SmartClinicalScopesMiddleware> logger, ResourceIdPolicy resourceIdPolicy)
        {
            EnsureArg.IsNotNull(next, nameof(next));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(resourceIdPolicy, nameof(resourceIdPolicy));

            _logger = logger;
            _next = next;
            _resourceIdPolicy = resourceIdPolicy;
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

        // Returns true when the search parameter key represents a chained search, which is not supported in SMART
        // on FHIR clinical scopes. Forward chaining uses a '.' in the key (e.g., "subject.name",
        // "subject:Patient.name"); reverse chaining uses the "_has" prefix (e.g., "_has:Observation:patient:code").
        private static bool IsChainedSearchParameter(string paramKey)
        {
            return paramKey.Contains('.', StringComparison.Ordinal)
                   || paramKey.StartsWith("_has:", StringComparison.OrdinalIgnoreCase);
        }

        // Returns true when the search parameter key uses a FHIR search modifier (e.g., :not, :exact, :missing),
        // which is not supported in SMART on FHIR clinical scopes. Only tokens in modifier position (after a ':')
        // are evaluated; the base parameter name (before the first ':') is excluded because it may legitimately
        // equal a modifier literal (e.g., "identifier" or "type" are valid search parameter names). Reference type
        // targets (e.g., "subject:Patient") simply do not match any modifier literal, so they pass through. Chained
        // keys (containing '.') are rejected earlier by IsChainedSearchParameter and never reach this method.
        private static bool ContainsSearchModifier(string paramKey)
        {
            var colonSeparatedTokens = paramKey.Split(':');

            // Skip index 0 (the base parameter name); every later token is in modifier position.
            for (int i = 1; i < colonSeparatedTokens.Length; i++)
            {
                if (KnownSearchModifiers.Contains(colonSeparatedTokens[i]))
                {
                    return true;
                }
            }

            return false;
        }

        // Returns true when the search parameter key is a result parameter that pulls in additional resources
        // (_include / _revinclude). These are not enforceable as SMART on FHIR clinical scope constraints and can
        // broaden the response to include resource types the scope does not otherwise grant, so they are rejected.
        // The base name (before any ':') is compared so both the plain form ("_include") and the modifier form
        // ("_include:iterate") are covered; the modifier form is also caught earlier by ContainsSearchModifier.
        private static bool IsIncludeParameter(string paramKey)
        {
            var baseName = paramKey.Split(':')[0];
            return baseName.Equals("_include", StringComparison.OrdinalIgnoreCase)
                   || baseName.Equals("_revinclude", StringComparison.OrdinalIgnoreCase);
        }

        // Percent-decodes only the part of each whitespace-separated scope before its first raw '?' (for example
        // "patient%2fObservation.rs" -> "patient/Observation.rs") and keeps the search-parameter query encoded, so
        // the query can be split on its raw '&' and '=' delimiters before each key and value is decoded. Whitespace
        // between scopes is preserved as-is, and encoded whitespace at either end of a decoded prefix is trimmed.
        // Returns false when a decoded prefix forms or hides a clinical scope through an encoded delimiter: an encoded
        // '?' (a fully encoded scope such as "patient%2fObservation.rs%3Fcode%3D1"), '&', '=', or encoded whitespace
        // inside the prefix (one scope value expanding into several scopes, such as
        // "patient%2fObservation.rs%20patient%2fPatient.rs").
        private static bool TryDecodeScopePrefixes(string scopeClaims, out string decodedScopeClaims)
        {
            var builder = new StringBuilder(scopeClaims.Length);
            int index = 0;

            while (index < scopeClaims.Length)
            {
                if (char.IsWhiteSpace(scopeClaims[index]))
                {
                    builder.Append(scopeClaims[index]);
                    index++;
                    continue;
                }

                int end = index;
                while (end < scopeClaims.Length && !char.IsWhiteSpace(scopeClaims[end]))
                {
                    end++;
                }

                string scope = scopeClaims.Substring(index, end - index);
                int queryStart = scope.IndexOf('?', StringComparison.Ordinal);
                string prefix = queryStart < 0 ? scope : scope.Substring(0, queryStart);
                string decodedPrefix = System.Uri.UnescapeDataString(prefix).Trim();

                if (decodedPrefix.Any(c => c == '?' || c == '&' || c == '=' || char.IsWhiteSpace(c))
                    && ClinicalScopeRegEx.IsMatch(decodedPrefix))
                {
                    decodedScopeClaims = null;
                    return false;
                }

                builder.Append(decodedPrefix);
                if (queryStart >= 0)
                {
                    builder.Append(scope, queryStart, scope.Length - queryStart);
                }

                index = end;
            }

            decodedScopeClaims = builder.ToString();
            return true;
        }

        // Returns true when a decoded search parameter key only contains the characters allowed in a SMART scope key.
        private static bool IsValidSearchParameterKey(string paramKey)
        {
            return paramKey.Length > 0
                   && paramKey.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-' || c == ':' || c == '.');
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

                var smartActionPresent = await authorizationService.CheckAccess(DataActions.Smart, false, context.RequestAborted);

                _logger.LogInformation("Smart Data Action is present {Smart}", smartActionPresent);

                var scopeRestrictions = new StringBuilder();
                scopeRestrictions.Append("Resource(s) allowed and permitted data actions on it are : ");

                // Only read and apply SMART clinical scopes if the user has the Smart Data action
                if (smartActionPresent)
                {
                    fhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControl = true;

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

                    // Azure Entra ID doesn't allow '/' in scopes, so users encode them as '%2f'. Only the part of
                    // each scope before the first raw '?' is decoded here; the search parameters stay encoded until
                    // they are split on the raw '&' and '=' delimiters (see below). Uri.UnescapeDataString is lenient
                    // on malformed percent-encoding (it leaves invalid sequences as-is), but guard defensively so
                    // any decode failure surfaces as a BadRequest rather than an unhandled 500.
                    try
                    {
                        if (!TryDecodeScopePrefixes(scopeClaims, out string decodedScopeClaims))
                        {
                            var error = string.Format(Api.Resources.SmartScopeInvalidSearchParameters, scopeClaims);
                            _logger.LogWarning(error);
                            await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                            return;
                        }

                        scopeClaims = decodedScopeClaims;
                    }
                    catch (Exception ex) when (ex is UriFormatException || ex is FormatException || ex is ArgumentException || ex is RegexMatchTimeoutException)
                    {
                        var error = string.Format(Api.Resources.SmartScopeInvalidSearchParameters, scopeClaims);
                        _logger.LogWarning(ex, error);
                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                        return;
                    }

                    MatchCollection matches;
                    try
                    {
                        matches = ClinicalScopeRegEx.Matches(scopeClaims);
                    }
                    catch (RegexMatchTimeoutException ex)
                    {
                        var error = string.Format(Api.Resources.SmartScopeInvalidSearchParameters, scopeClaims);
                        _logger.LogWarning(ex, error);
                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                        return;
                    }

                    string clinicalScopeContext = null;
                    foreach (Match match in matches)
                    {
                        // The regex finds clinical-scope substrings rather than matching whole tokens, so a
                        // malformed scope whose token is only partially valid (e.g. "patient/Observation.read-only",
                        // "patient/Observation.rs?active", "patient/Observation.rsXYZ") still produces a match for
                        // the valid prefix. If the match is immediately followed by a non-whitespace character the
                        // token was not fully consumed; reject it rather than silently enforcing the truncated
                        // prefix, which would grant broader-than-intended access (fail-open).
                        int matchEnd = match.Index + match.Length;
                        if (matchEnd < scopeClaims.Length && !char.IsWhiteSpace(scopeClaims[matchEnd]))
                        {
                            var error = string.Format(Api.Resources.SmartScopeInvalidSearchParameters, scopeClaims);
                            _logger.LogWarning(error);
                            await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                            return;
                        }

                        string currentClinicalScopeContext = match.Groups["id"].Value;
                        if (clinicalScopeContext != null
                            && !clinicalScopeContext.Equals(currentClinicalScopeContext, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning(Api.Resources.MixedSMARTScopeContextsAreNotAllowed);
                            await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, Api.Resources.MixedSMARTScopeContextsAreNotAllowed);
                            return;
                        }

                        clinicalScopeContext = currentClinicalScopeContext;
                    }

                    bool includeFhirUserClaim = !string.Equals("system", clinicalScopeContext, StringComparison.OrdinalIgnoreCase);
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

                            // A v2 access level is a set of distinct permission letters from "cruds". The regex
                            // ([cruds]+) also matches repeated letters (e.g. "rrrrrs"), which are malformed even
                            // though they would idempotently OR the same DataActions flag. Reject duplicates so a
                            // malformed access level is not silently accepted.
                            if (accessLevel.Distinct().Count() != accessLevel.Length)
                            {
                                var error = string.Format(Api.Resources.SmartScopeInvalidSearchParameters, match.Value?.Trim());
                                _logger.LogWarning(error);
                                await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                return;
                            }
                        }

                        // If both types are detected, write an error and return.
                        if (smartV1AccessLevelUsed && smartV2AccessLevelUsed)
                        {
                            _logger.LogWarning(Api.Resources.MixedSMARTV1AndV2ScopesAreNotAllowed);
                            await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, Api.Resources.MixedSMARTV1AndV2ScopesAreNotAllowed);
                            return;
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

                            // Reject scopes that reference a resource type the running FHIR version does not
                            // recognize (e.g. a typo like "Observaton"). Without this, a malformed type is
                            // silently stored as a ScopeRestriction that matches nothing, masking client
                            // misconfiguration. The "all" wildcard is not a concrete resource type, so it is
                            // excluded from the check.
                            if (!resource.Equals(KnownResourceTypes.All, StringComparison.Ordinal)
                                && !ModelInfoProvider.IsKnownResource(resource, ignoreCase: false))
                            {
                                var error = string.Format(Api.Resources.SmartScopeUnknownResourceType, match.Value.Trim(), resource);
                                _logger.LogWarning(error);
                                await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                return;
                            }

                            // If Finer-grained resource constraints using search parameters present
                            if (match.Groups["searchParams"].Success)
                            {
                                smartScopeSearchParameters = new SearchParams();
                                var searchParamsString = match.Groups["searchParams"].Value;

                                // The query is still percent-encoded here, so only raw '&' separates constraints.
                                var searchParamsPairs = searchParamsString.Split('&');

                                // iterate through each key-value pair and add them to the SearchParams
                                foreach (var kvPair in searchParamsPairs)
                                {
                                    // Split on the first '=' only. A well-formed constraint is a single
                                    // "key=value" pair; anything with a missing '=' or an extra '=' (e.g.
                                    // "code:exact=a=b" or "identifier=x?mrn=12345") is malformed and must be
                                    // rejected rather than silently dropped, which would broaden the granted
                                    // access (fail-open).
                                    var parts = kvPair.Split('=', 2);
                                    var paramValue = parts.Length == 2 ? parts[1] : string.Empty;

                                    // Decode the key only after the split, then require the usual key characters so
                                    // an encoded '&', '=', '?' or space can't change the key. The checks below see the
                                    // decoded key, so an encoded ':' or '.' can't hide a modifier or a chained search.
                                    var paramKey = System.Uri.UnescapeDataString(parts[0]);
                                    if (!IsValidSearchParameterKey(paramKey))
                                    {
                                        var error = string.Format(Api.Resources.SmartScopeInvalidSearchParameters, match.Value?.Trim());
                                        _logger.LogWarning(error);
                                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                        return;
                                    }

                                    // Chained search parameters are not enforceable as SMART clinical scope
                                    // constraints, so reject them rather than silently dropping the constraint.
                                    // Forward chaining uses a '.' in the key (e.g., "subject.name",
                                    // "subject:Patient.name"); reverse chaining uses the "_has" prefix
                                    // (e.g., "_has:Observation:patient:code").
                                    if (IsChainedSearchParameter(paramKey))
                                    {
                                        var error = string.Format(Api.Resources.SmartScopeSearchParameterChainedSearchNotSupported, match.Value?.Trim(), $"{paramKey}={paramValue}");
                                        _logger.LogWarning(error);
                                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                        return;
                                    }

                                    // Detect FHIR search modifiers anywhere in the parameter key (e.g.
                                    // "name:exact", "category:not", "value-quantity:ofType").
                                    if (ContainsSearchModifier(paramKey))
                                    {
                                        var error = string.Format(Api.Resources.SmartScopeSearchParameterModifiersNotSupported, match.Value?.Trim(), $"{paramKey}={paramValue}");
                                        _logger.LogWarning(error);
                                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                        return;
                                    }

                                    // Result parameters that pull in additional resources (_include/_revinclude)
                                    // are not enforceable as scope constraints and can broaden the response to
                                    // resource types the scope does not grant, so reject them.
                                    if (IsIncludeParameter(paramKey))
                                    {
                                        var error = string.Format(Api.Resources.SmartScopeSearchParameterIncludesNotSupported, match.Value?.Trim(), $"{paramKey}={paramValue}");
                                        _logger.LogWarning(error);
                                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                        return;
                                    }

                                    // Reject malformed key-value pairs (a missing '=' or a value that still
                                    // contains a raw '=') instead of silently dropping the constraint (fail-open).
                                    if (parts.Length != 2 || paramValue.Contains('=', StringComparison.Ordinal))
                                    {
                                        var error = string.Format(Api.Resources.SmartScopeInvalidSearchParameters, match.Value?.Trim());
                                        _logger.LogWarning(error);
                                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                        return;
                                    }

                                    // Decode the value only now, after splitting on the raw '&' and '=' delimiters, so an
                                    // encoded "%26" or "%3D" stays part of this value (e.g. identifier=ABC%26status%3Dfinal
                                    // is the single value "ABC&status=final"), matching how FHIR search reads the same string.
                                    var decodedParamValue = System.Uri.UnescapeDataString(paramValue);

                                    // A value that decodes to whitespace only (e.g. "%20") can't be enforced as a
                                    // search constraint, so reject it rather than failing later with a 500.
                                    if (string.IsNullOrWhiteSpace(decodedParamValue))
                                    {
                                        var error = string.Format(Api.Resources.SmartScopeInvalidSearchParameters, match.Value?.Trim());
                                        _logger.LogWarning(error);
                                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                        return;
                                    }

                                    smartScopeSearchParameters.Add(paramKey, decodedParamValue);
                                }

                                if (smartScopeSearchParameters.Parameters.Count > 0)
                                {
                                    fhirRequestContext.AccessControlContext.ApplyFineGrainedAccessControlWithSearchParameters = true;
                                }
                            }

                            fhirRequestContext.AccessControlContext.AllowedResourceActions.Add(new ScopeRestriction(resource, permittedDataActions, id, smartScopeSearchParameters.Parameters.Any() ? smartScopeSearchParameters : null));

                            scopeRestrictions.Append($" ( {resource}-{permittedDataActions} ) ");
                        }
                    }

                    _logger.LogInformation("Scope restrictions allowed are {ScopeRestriction}", scopeRestrictions);
                    _logger.LogInformation("FhirUserClaim is present {FhirUserClaim}", includeFhirUserClaim);

                    bool isPatientScope = string.Equals("patient", clinicalScopeContext, StringComparison.Ordinal);
                    var patientClaims = isPatientScope ? principal.FindAll("patient").ToList() : new List<Claim>();
                    if (patientClaims.Count > 0)
                    {
                        if (patientClaims.Count != 1 || !_resourceIdPolicy.IsValid(patientClaims[0].Value))
                        {
                            _logger.LogWarning(Api.Resources.SmartPatientContextInvalid);
                            await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, Api.Resources.SmartPatientContextInvalid);
                            return;
                        }

                        fhirRequestContext.AccessControlContext.CompartmentResourceType = KnownResourceTypes.Patient;
                        fhirRequestContext.AccessControlContext.CompartmentId = patientClaims[0].Value;
                    }
                    else if (includeFhirUserClaim)
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
                            FhirUserClaimParser.ParseFhirUserClaim(fhirRequestContext.AccessControlContext, authorizationConfiguration.ErrorOnMissingFhirUserClaim && !isPatientScope);
                        }
                        catch (UriFormatException)
                        {
                            if (authorizationConfiguration.ErrorOnMissingFhirUserClaim)
                            {
                                var error = string.Format(Api.Resources.FhirUserClaimMustBeURL, fhirUser);
                                _logger.LogError(error);
                                await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, error);
                                return;
                            }
                        }
                        catch (ArgumentNullException)
                        {
                            if (authorizationConfiguration.ErrorOnMissingFhirUserClaim)
                            {
                                _logger.LogWarning(Api.Resources.FhirUserClaimCannotBeNull);
                                await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, Api.Resources.FhirUserClaimCannotBeNull);
                                return;
                            }
                        }
                    }

                    // Legacy patient tokens may identify the patient through fhirUser, but a user
                    // identity must never widen a patient scope to a practitioner's compartment.
                    if (isPatientScope
                        && (fhirRequestContext.AccessControlContext.CompartmentResourceType != KnownResourceTypes.Patient
                            || !_resourceIdPolicy.IsValid(fhirRequestContext.AccessControlContext.CompartmentId)))
                    {
                        _logger.LogWarning(Api.Resources.SmartPatientContextInvalid);
                        await WriteErrorResponseAsync(fhirRequestContext, context, HttpStatusCode.BadRequest, Api.Resources.SmartPatientContextInvalid);
                        return;
                    }
                }
            }

            // Call the next delegate/middleware in the pipeline
            await _next(context);
        }

        private async Task WriteErrorResponseAsync(
            IFhirRequestContext requestContext,
            HttpContext httpContext,
            HttpStatusCode statusCode,
            string errorMessage,
            OperationOutcome.IssueType issueType = OperationOutcome.IssueType.Invalid)
        {
            EnsureArg.IsNotNull(requestContext, nameof(requestContext));
            EnsureArg.IsNotNull(httpContext, nameof(httpContext));
            EnsureArg.IsNotNull(httpContext.Response, nameof(httpContext.Response));

            try
            {
                var body = new
                {
                    resourceType = nameof(OperationOutcome),
                    id = requestContext.CorrelationId,
                    meta = new
                    {
                        lastUpdated = Clock.UtcNow.ToString("o"),
                    },
                    issue = new[]
                    {
                        new
                        {
                           severity = OperationOutcome.IssueSeverity.Error.GetLiteral(),
                           code = issueType.GetLiteral(),
                           diagnostics = errorMessage,
                        },
                    },
                };

                httpContext.Response.Clear();
                httpContext.Response.StatusCode = (int)statusCode;
                await httpContext.Response.WriteAsJsonAsync(body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write error response.");
                throw;
            }
        }
    }
}
