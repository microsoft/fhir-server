// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
#if !Stu3 && !R4 && !R4B
using Microsoft.Health.Fhir.R5.Core.Extensions;
#endif
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public class SearchParameterValidator : ISearchParameterValidator
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer;
        private readonly ILogger _logger;

        private const string HttpPostName = "POST";
        private const string HttpPutName = "PUT";
        private const string HttpDeleteName = "DELETE";
        private const string HttpPatchName = "PATCH";

        public SearchParameterValidator(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IAuthorizationService<DataActions> authorizationService,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterOperations searchParameterOperations,
            ISearchParameterComparer<SearchParameterInfo> searchParameterComparer,
            ILogger<SearchParameterValidator> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(searchParameterComparer, nameof(searchParameterComparer));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _authorizationService = authorizationService;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterOperations = searchParameterOperations;
            _searchParameterComparer = searchParameterComparer;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task ValidateSearchParameterInput(SearchParameter searchParam, string method, CancellationToken cancellationToken)
        {
            if (await _authorizationService.CheckAccess(DataActions.Reindex, cancellationToken) != DataActions.Reindex)
            {
                throw new UnauthorizedFhirActionException();
            }

            // check if reindex job is running
            using (IScoped<IFhirOperationDataStore> fhirOperationDataStore = _fhirOperationDataStoreFactory())
            {
                (var activeReindexJobs, var reindexJobId) = await fhirOperationDataStore.Value.CheckActiveReindexJobsAsync(cancellationToken);
                if (activeReindexJobs)
                {
                    throw new JobConflictException(string.Format(Resources.ChangesToSearchParametersNotAllowedWhileReindexing, reindexJobId));
                }
            }

            if (string.IsNullOrEmpty(searchParam.Url) && (method.Equals(HttpDeleteName, StringComparison.Ordinal) || method.Equals(HttpPatchName, StringComparison.Ordinal)))
            {
                // Return out if this is delete OR patch call and no Url so FHIRController can move to next action
                return;
            }

            var validationFailures = new List<ValidationFailure>();

            if (string.IsNullOrEmpty(searchParam.Url))
            {
                _logger.LogInformation("Search parameter definition is missing a url. url is null or empty.");
                validationFailures.Add(
                    new ValidationFailure(nameof(Base.TypeName), Resources.SearchParameterDefinitionInvalidMissingUri));
            }
            else
            {
                // Checks if the url is a valid url
                if (!Uri.TryCreate(searchParam.Url, UriKind.Absolute, out _))
                {
                    _logger.LogInformation("Search parameter definition has an invalid url. url: {Url}", searchParam.Url);
                    validationFailures.Add(
                          new ValidationFailure(
                              nameof(searchParam.Url),
                              string.Format(Resources.SearchParameterDefinitionInvalidDefinitionUri, searchParam.Url)));
                }
                else
                {
                    // Refresh the search parameter cache in the search parameter definition manager before starting the validation.
                    await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);

                    // If a search parameter with the same uri exists already
                    if (_searchParameterDefinitionManager.TryGetSearchParameter(searchParam.Url, out var searchParameterInfo))
                    {
                        // And if this is a request to create a new search parameter
                        if (method.Equals(HttpPostName, StringComparison.OrdinalIgnoreCase))
                        {
                            if (searchParameterInfo.SearchParameterStatus != SearchParameterStatus.PendingDelete
                                && searchParameterInfo.SearchParameterStatus != SearchParameterStatus.Deleted)
                            {
                                _logger.LogInformation("Requested to create a new Search parameter but Search parameter definition has a duplicate url. url: {Url}", searchParam.Url);

                                // We have a conflict
                                validationFailures.Add(
                                    new ValidationFailure(
                                        nameof(searchParam.Url),
                                        string.Format(Resources.SearchParameterDefinitionDuplicatedEntry, searchParam.Url)));
                            }
                            else
                            {
                                CheckForConflictingCodeValue(searchParam, validationFailures);
                            }
                        }
                        else if (method.Equals(HttpPutName, StringComparison.OrdinalIgnoreCase))
                        {
                            CheckForConflictingCodeValue(searchParam, validationFailures);
                        }
                    }
                    else
                    {
                        // Otherwise, no search parameters with a matching uri exist
                        // Ensure this isn't a request to modify an existing parameter
                        if (method.Equals(HttpPutName, StringComparison.OrdinalIgnoreCase) ||
                            method.Equals(HttpDeleteName, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Requested to modify an existing Search parameter but Search parameter definition does not exist. url: {Url}", searchParam.Url);
                            validationFailures.Add(
                                new ValidationFailure(
                                    nameof(searchParam.Url),
                                    string.Format(Resources.SearchParameterDefinitionNotFound, searchParam.Url)));
                        }

                        CheckForConflictingCodeValue(searchParam, validationFailures);
                    }
                }
            }

            if (validationFailures.Any())
            {
                throw new ResourceNotValidException(validationFailures);
            }
        }

        private void CheckForConflictingCodeValue(SearchParameter searchParam, List<ValidationFailure> validationFailures)
        {
            // Ensure the search parameter's code value does not already exist for its base type(s)
            var baseTypes = searchParam.Base?.Where(x => x != null).Select(x => x?.ToString()).ToList() ?? new List<string>();
            foreach (string baseType in baseTypes)
            {
                if (searchParam.Code is null)
                {
                    _logger.LogInformation("Search parameter definition has a null or empty code value. code: {Code}, baseType: {BaseType}", searchParam.Code, baseType);
                    validationFailures.Add(
                        new ValidationFailure(
                            nameof(searchParam.Code),
                            string.Format(Resources.SearchParameterDefinitionNullorEmptyCodeValue, searchParam.Code, baseType)));
                }
                else
                {
                    if (string.Equals(baseType, KnownResourceTypes.Resource, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
                        {
                            if (_searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, true, out var existingSearchParameter)
                                && !CompareSearchParameterProperties(baseType, searchParam.Code, searchParam, existingSearchParameter, validationFailures))
                            {
                                break;
                            }
                        }
                    }
                    else if (baseType == KnownResourceTypes.DomainResource)
                    {
                        foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
                        {
                            Type type = _modelInfoProvider.GetTypeForFhirType(resource);
                            string fhirBaseType = _modelInfoProvider.GetFhirTypeNameForType(type.BaseType);

                            if (fhirBaseType == KnownResourceTypes.DomainResource
                                && _searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, true, out var existingSearchParameter)
                                && !CompareSearchParameterProperties(baseType, searchParam.Code, searchParam, existingSearchParameter, validationFailures))
                            {
                                break;
                            }
                        }
                    }
                    else if (_searchParameterDefinitionManager.TryGetSearchParameter(baseType, searchParam.Code, true, out var existingSearchParameter))
                    {
                        CompareSearchParameterProperties(baseType, searchParam.Code, searchParam, existingSearchParameter, validationFailures);
                    }
                }
            }
        }

        private bool CompareSearchParameterProperties(
            string baseType,
            string code,
            SearchParameter incomingSearchParameter,
            SearchParameterInfo existingSearchParameter,
            List<ValidationFailure> validationFailures)
        {
            EnsureArg.IsNotNull(incomingSearchParameter, nameof(incomingSearchParameter));
            EnsureArg.IsNotNull(existingSearchParameter, nameof(existingSearchParameter));

            _logger.LogInformation($"Comparing types...: '{incomingSearchParameter.Type}', '{existingSearchParameter.Type}'");
            if (!string.Equals(incomingSearchParameter.Type?.ToString(), existingSearchParameter.Type.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("The types of the incoming and existing search parameter are different.");
                validationFailures.Add(
                    new ValidationFailure(
                        nameof(code),
                        string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, code, baseType)));
                return false;
            }

            try
            {
                var result = _searchParameterComparer.CompareExpression(
                    incomingSearchParameter.Expression,
                    existingSearchParameter.Expression,
                    existingSearchParameter.IsBaseTypeSearchParameter());
                switch (result)
                {
                    case 0:
                        _logger.LogInformation("The expressions of the incoming and existing search parameter are identical.");
                        break;

                    case 1:
                        _logger.LogInformation("The incoming expression is a superset of the existing expression.");
                        break;

                    case -1:
                        _logger.LogInformation("The existing expression is a superset of the incoming expression.");
                        break;

                    default:
                        _logger.LogInformation("The expressions of the incoming and existing search parameter are different.");
                        validationFailures.Add(
                            new ValidationFailure(
                                nameof(code),
                                string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, code, baseType)));
                        return false;
                }
            }
            catch (FormatException ex)
            {
                _logger.LogError(ex, "Failed to parse expression.");
                validationFailures.Add(
                    new ValidationFailure(
                        nameof(code),
                        Resources.SearchParameterDefinitionContainsInvalidEntry));
                return false;
            }

            if (incomingSearchParameter.Type == SearchParamType.Composite)
            {
                _logger.LogInformation($"Comparing components...: '{incomingSearchParameter.Component?.Count ?? 0} components', '{existingSearchParameter.Component?.Count ?? 0} components'");
                var incomingComponent = incomingSearchParameter.Component?.Select<SearchParameter.ComponentComponent, (string, string)>(x => new(x.GetComponentDefinitionUri().OriginalString, x.Expression)).ToList() ?? new List<(string, string)>();
                var existingComponent = existingSearchParameter.Component?.Select<SearchParameterComponentInfo, (string, string)>(x => new(x.DefinitionUrl.OriginalString, x.Expression)).ToList() ?? new List<(string, string)>();
                if (_searchParameterComparer.CompareComponent(incomingComponent, existingComponent) != 0)
                {
                    _logger.LogInformation("Components are different.");
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
