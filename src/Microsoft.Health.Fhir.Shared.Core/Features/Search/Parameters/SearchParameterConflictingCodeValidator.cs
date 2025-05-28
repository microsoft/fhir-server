// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public class SearchParameterConflictingCodeValidator : ISearchParameterConflictingCodeValidator
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ILogger<SearchParameterConflictingCodeValidator> _logger;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;

        public SearchParameterConflictingCodeValidator(
            IModelInfoProvider modelInfoProvider,
            ILogger<SearchParameterConflictingCodeValidator> logger,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            _modelInfoProvider = EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _searchParameterDefinitionManager = EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
        }

        public Uri CheckForConflictingCodeValue(SearchParameter searchParam, Collection<ValidationFailure> validationFailures)
        {
            Uri duplicateOf = null;

            // Ensure the search parameter's code value does not already exist for its base type(s)
            foreach (ResourceType? baseType in searchParam.Base)
            {
                if (searchParam.Code is null)
                {
                    _logger.LogInformation("Search parameter definition has a null or empty code value. code: {Code}, baseType: {BaseType}", searchParam.Code, baseType.ToString());
                    validationFailures.Add(
                        new ValidationFailure(
                            nameof(searchParam.Code),
                            string.Format(Resources.SearchParameterDefinitionNullorEmptyCodeValue, searchParam.Code, baseType)));
                }
                else
                {
                    if (string.Equals(baseType.ToString(), KnownResourceTypes.Resource, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
                        {
                            if (_searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, out var conflictingSearchParameter))
                            {
                                duplicateOf = HandleConflictingCode(searchParam, baseType, conflictingSearchParameter, validationFailures);
                            }
                        }
                    }
                    else if (baseType.ToString() == KnownResourceTypes.DomainResource)
                    {
                        foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
                        {
                            Type type = _modelInfoProvider.GetTypeForFhirType(resource);
                            string fhirBaseType = _modelInfoProvider.GetFhirTypeNameForType(type.BaseType);

                            if (fhirBaseType == KnownResourceTypes.DomainResource && _searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, out var conflictingSearchParameter))
                            {
                                duplicateOf = HandleConflictingCode(searchParam, baseType, conflictingSearchParameter, validationFailures);
                                break;
                            }
                        }
                    }
                    else if (_searchParameterDefinitionManager.TryGetSearchParameter(baseType.ToString(), searchParam.Code, out var conflictingSearchParameter))
                    {
                        // The search parameter's code value conflicts with an existing one
                        duplicateOf = HandleConflictingCode(searchParam, baseType, conflictingSearchParameter, validationFailures);
                    }
                }
            }

            return duplicateOf;
        }

        private Uri HandleConflictingCode(SearchParameter searchParam, ResourceType? baseType, SearchParameterInfo conflictingSearchParam, Collection<ValidationFailure> validationFailures)
        {
            Uri duplicateOf = conflictingSearchParam.Url;

            // check that all the base types are the same, or that the conflicting parameter is a base type of the new one, or contains a superset of the base types
            if (!conflictingSearchParam.BaseResourceTypes.SequenceEqual(searchParam.Base.Select(b => b.ToString())) &&
                !conflictingSearchParam.BaseResourceTypes.Contains(KnownResourceTypes.Resource) &&
                !conflictingSearchParam.BaseResourceTypes.Contains(KnownResourceTypes.DomainResource) &&
                !conflictingSearchParam.BaseResourceTypes.ToHashSet().IsSupersetOf(searchParam.Base.Select(b => b.ToString())))
            {
                // The search parameter's code value conflicts with an existing one
                _logger.LogInformation("Search parameter definition has a conflicting code value with an existing one. code: {Code}, baseType: {BaseType}, and the base types do not match.", searchParam.Code, baseType.ToString());
                validationFailures.Add(
                    new ValidationFailure(
                        nameof(searchParam.Code),
                        string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, searchParam.Code, baseType)));
                duplicateOf = null;
            }

            // check if the expression is equivalent
            var expressionsMisMatch = false;

            if (!conflictingSearchParam.Expression.Equals(searchParam.Expression, StringComparison.Ordinal))
            {
                expressionsMisMatch = true;
            }

            // check for complex expressions where | is used to separate multiple expressions
            var conflictingExpressions = conflictingSearchParam.Expression.Split('|').Select(s => s.Trim());
            var expressions = searchParam.Expression.Split('|').Select(s => s.Trim());
            expressionsMisMatch = !conflictingExpressions.ToHashSet().IsSupersetOf(expressions);

            // check if the conflicting expression is Resource or DomainResource base
            if (conflictingSearchParam.BaseResourceTypes.Contains(KnownResourceTypes.Resource) || conflictingSearchParam.BaseResourceTypes.Contains(KnownResourceTypes.DomainResource))
            {
                var firstBaseType = searchParam.Base.First().ToString();
                var modifiedResourceExpression = searchParam.Expression.Replace(firstBaseType, "Resource", System.StringComparison.Ordinal);
                var modifiedDomainResourceExpression = searchParam.Expression.Replace(firstBaseType, "DomainResource", System.StringComparison.Ordinal);

                expressionsMisMatch = !conflictingSearchParam.Expression.Equals(modifiedDomainResourceExpression, System.StringComparison.Ordinal) &&
                    !conflictingSearchParam.Expression.Equals(modifiedResourceExpression, System.StringComparison.Ordinal);
            }

            if (expressionsMisMatch)
            {
                // The search parameter's code value conflicts with an existing one
                _logger.LogInformation("Search parameter definition has a conflicting code value with an existing one. code: {Code}, baseType: {BaseType}, and the expression does not match.", searchParam.Code, baseType.ToString());
                validationFailures.Add(
                    new ValidationFailure(
                        nameof(searchParam.Code),
                        string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, searchParam.Code, baseType)));
                duplicateOf = null;
            }

            // check each component
            if (conflictingSearchParam.Component?.Count > 0)
            {
                var componentsMatch = true;

                // Check if the components are equivalent
                if (conflictingSearchParam.Component.Count != searchParam.Component.Count)
                {
                    componentsMatch = false;
                }
                else
                {
                    foreach (var componentInfo in conflictingSearchParam.Component)
                    {
                        // find matching component in the new search parameter
#if Stu3
                        var matchingComponent = searchParam.Component.Where(c => c.Definition.ReferenceElement.ToString() == componentInfo.DefinitionUrl.ToString()).FirstOrDefault();
#else
                        var matchingComponent = searchParam.Component.Where(c => c.Definition == componentInfo.DefinitionUrl.ToString()).FirstOrDefault();
#endif
                        if (matchingComponent == null)
                        {
                            componentsMatch = false;
                        }
                        else
                        {
                            // TODO: check if the fhirPath expression is equivalent, rather than a string match
                            if (matchingComponent.Expression != componentInfo.Expression)
                            {
                                componentsMatch = false;
                            }
                        }
                    }
                }

                if (!componentsMatch)
                {
                    // The search parameter's code value conflicts with an existing one
                    _logger.LogInformation("Search parameter definition has a conflicting code value with an existing one. code: {Code}, baseType: {BaseType}, and the components do not match.", searchParam.Code, baseType.ToString());
                    validationFailures.Add(
                    new ValidationFailure(
                        nameof(searchParam.Code),
                        string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, searchParam.Code, baseType)));
                    duplicateOf = null;
                }
            }

            // mark the new search parameter as duplicate
            return duplicateOf;
        }
    }
}
