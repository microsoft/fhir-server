// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
#if !Stu3 && !R4 && !R4B
using Microsoft.Health.Fhir.R5.Core.Extensions;
#endif

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    /// <summary>
    /// Rules that decide whether a SearchParameter's code conflicts with a SearchParameter already defined
    /// on the same base resource type. Shared by single-resource validation and package installation.
    /// </summary>
    internal static class SearchParameterConflictRules
    {
        /// <summary>
        /// Adds a validation failure for each base type where the SearchParameter's code is already used by an
        /// incompatible SearchParameter.
        /// </summary>
        /// <param name="searchParam">The incoming SearchParameter.</param>
        /// <param name="searchParameterDefinitionManager">The current SearchParameter definitions.</param>
        /// <param name="modelInfoProvider">The model info provider.</param>
        /// <param name="searchParameterComparer">The comparer used to decide expression compatibility.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="validationFailures">The list the failures are added to.</param>
        internal static void CheckForConflictingCodeValue(
            SearchParameter searchParam,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterComparer<SearchParameterInfo> searchParameterComparer,
            ILogger logger,
            List<ValidationFailure> validationFailures)
        {
            EnsureArg.IsNotNull(searchParam, nameof(searchParam));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterComparer, nameof(searchParameterComparer));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(validationFailures, nameof(validationFailures));

            // Ensure the search parameter's code value does not already exist for its base type(s)
            var baseTypes = searchParam.Base?.Where(x => x != null).Select(x => x?.ToString()).ToList() ?? new List<string>();
            foreach (string baseType in baseTypes)
            {
                if (searchParam.Code is null)
                {
                    logger.LogInformation("Search parameter definition has a null or empty code value. code: {Code}, baseType: {BaseType}", searchParam.Code, baseType);
                    validationFailures.Add(
                        new ValidationFailure(
                            nameof(searchParam.Code),
                            string.Format(Resources.SearchParameterDefinitionNullorEmptyCodeValue, searchParam.Code, baseType)));
                }
                else
                {
                    if (string.Equals(baseType, KnownResourceTypes.Resource, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string resource in modelInfoProvider.GetResourceTypeNames())
                        {
                            if (searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, true, out var existingSearchParameter)
                                && !CompareSearchParameterProperties(baseType, searchParam.Code, searchParam, existingSearchParameter, searchParameterComparer, logger, validationFailures))
                            {
                                break;
                            }
                        }
                    }
                    else if (baseType == KnownResourceTypes.DomainResource)
                    {
                        foreach (string resource in modelInfoProvider.GetResourceTypeNames())
                        {
                            Type type = modelInfoProvider.GetTypeForFhirType(resource);
                            string fhirBaseType = modelInfoProvider.GetFhirTypeNameForType(type.BaseType);

                            if (fhirBaseType == KnownResourceTypes.DomainResource
                                && searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, true, out var existingSearchParameter)
                                && !CompareSearchParameterProperties(baseType, searchParam.Code, searchParam, existingSearchParameter, searchParameterComparer, logger, validationFailures))
                            {
                                break;
                            }
                        }
                    }
                    else if (searchParameterDefinitionManager.TryGetSearchParameter(baseType, searchParam.Code, true, out var existingSearchParameter))
                    {
                        CompareSearchParameterProperties(baseType, searchParam.Code, searchParam, existingSearchParameter, searchParameterComparer, logger, validationFailures);
                    }
                }
            }
        }

        private static bool CompareSearchParameterProperties(
            string baseType,
            string code,
            SearchParameter incomingSearchParameter,
            SearchParameterInfo existingSearchParameter,
            ISearchParameterComparer<SearchParameterInfo> searchParameterComparer,
            ILogger logger,
            List<ValidationFailure> validationFailures)
        {
            EnsureArg.IsNotNull(incomingSearchParameter, nameof(incomingSearchParameter));
            EnsureArg.IsNotNull(existingSearchParameter, nameof(existingSearchParameter));

            logger.LogInformation($"Comparing types...: '{incomingSearchParameter.Type}', '{existingSearchParameter.Type}'");
            if (!string.Equals(incomingSearchParameter.Type?.ToString(), existingSearchParameter.Type.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("The types of the incoming and existing search parameter are different.");
                validationFailures.Add(
                    new ValidationFailure(
                        nameof(code),
                        string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, code, baseType)));
                return false;
            }

            try
            {
                var result = searchParameterComparer.CompareExpression(
                    incomingSearchParameter.Expression,
                    existingSearchParameter.Expression,
                    existingSearchParameter.IsBaseTypeSearchParameter());
                switch (result)
                {
                    case 0:
                        logger.LogInformation("The expressions of the incoming and existing search parameter are identical.");
                        break;

                    case 1:
                        logger.LogInformation("The incoming expression is a superset of the existing expression.");
                        break;

                    case -1:
                        logger.LogInformation("The existing expression is a superset of the incoming expression.");
                        break;

                    default:
                        logger.LogInformation("The expressions of the incoming and existing search parameter are different.");
                        validationFailures.Add(
                            new ValidationFailure(
                                nameof(code),
                                string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, code, baseType)));
                        return false;
                }
            }
            catch (FormatException ex)
            {
                logger.LogError(ex, "Failed to parse expression.");
                validationFailures.Add(
                    new ValidationFailure(
                        nameof(code),
                        Resources.SearchParameterDefinitionContainsInvalidEntry));
                return false;
            }

            if (incomingSearchParameter.Type == SearchParamType.Composite)
            {
                logger.LogInformation($"Comparing components...: '{incomingSearchParameter.Component?.Count ?? 0} components', '{existingSearchParameter.Component?.Count ?? 0} components'");
                var incomingComponent = incomingSearchParameter.Component?.Select<SearchParameter.ComponentComponent, (string, string)>(x => new(x.GetComponentDefinitionUri().OriginalString, x.Expression)).ToList() ?? new List<(string, string)>();
                var existingComponent = existingSearchParameter.Component?.Select<SearchParameterComponentInfo, (string, string)>(x => new(x.DefinitionUrl.OriginalString, x.Expression)).ToList() ?? new List<(string, string)>();
                if (searchParameterComparer.CompareComponent(incomingComponent, existingComponent) != 0)
                {
                    logger.LogInformation("Components are different.");
                    validationFailures.Add(
                        new ValidationFailure(
                            nameof(code),
                            string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, code, baseType)));
                    return false;
                }
            }

            return true;
        }
    }
}
