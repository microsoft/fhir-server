// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
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
    public class SearchParameterValidator : ISearchParameterValidator
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ILogger _logger;

        private const string HttpPostName = "POST";
        private const string HttpPutName = "PUT";
        private const string HttpDeleteName = "DELETE";

        public SearchParameterValidator(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IAuthorizationService<DataActions> authorizationService,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ILogger<SearchParameterValidator> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _authorizationService = authorizationService;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
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
                    _logger.LogInformation("Reindex job {ReindexJobId} is running. Changes to search parameters are not allowed.", reindexJobId);
                    throw new JobConflictException(string.Format(Resources.ChangesToSearchParametersNotAllowedWhileReindexing, reindexJobId));
                }
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
                    // If a search parameter with the same uri exists already
                    if (_searchParameterDefinitionManager.TryGetSearchParameter(searchParam.Url, out _))
                    {
                        // And if this is a request to create a new search parameter
                        if (method.Equals(HttpPostName, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogInformation("Requested to create a new Search parameter but Search parameter definition has a duplicate url. url: {Url}", searchParam.Url);

                            // We have a conflict
                            validationFailures.Add(
                                new ValidationFailure(
                                    nameof(searchParam.Url),
                                    string.Format(Resources.SearchParameterDefinitionDuplicatedEntry, searchParam.Url)));
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

            // validate that the url does not correspond to a search param from the spec
            // TODO: still need a method to determine spec defined search params

            // validation of the fhir path
            // TODO: separate user story for this validation

            if (validationFailures.Any())
            {
                throw new ResourceNotValidException(validationFailures);
            }
        }

        private void CheckForConflictingCodeValue(SearchParameter searchParam, List<ValidationFailure> validationFailures)
        {
            // Ensure the search parameter's code value does not already exist for its base type(s)
            foreach (ResourceType? baseType in searchParam.Base)
            {
                if (searchParam.Code is null)
                {
                    _logger.LogInformation("Search parameter definition has a null or empty code value. code: {Code}, baseType: {BaseType}", searchParam.Code, baseType.ToString());
                    validationFailures.Add(
                        new ValidationFailure(
                            nameof(searchParam.Code),
                            string.Format(Resources.SearchParameterDefinitionNullorEmptyCodeValue, searchParam.Code, baseType.ToString())));
                }
                else
                {
                    if (string.Equals(baseType.ToString(), KnownResourceTypes.Resource, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
                        {
                            if (_searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, out _))
                            {
                                _logger.LogInformation("Search parameter definition has a conflicting code value. code: {Code}, baseType: {BaseType}", searchParam.Code, resource);
                                validationFailures.Add(
                                    new ValidationFailure(
                                    nameof(searchParam.Code),
                                    string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, searchParam.Code, resource)));
                                break;
                            }
                        }
                    }
                    else if (baseType.ToString() == KnownResourceTypes.DomainResource)
                    {
                        foreach (string resource in _modelInfoProvider.GetResourceTypeNames())
                        {
                            Type type = _modelInfoProvider.GetTypeForFhirType(resource);
                            string fhirBaseType = _modelInfoProvider.GetFhirTypeNameForType(type.BaseType);

                            if (fhirBaseType == KnownResourceTypes.DomainResource && _searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, out _))
                            {
                                _logger.LogInformation("Search parameter definition has a conflicting code value. code: {Code}, baseType: {BaseType}", searchParam.Code, resource);
                                validationFailures.Add(
                                    new ValidationFailure(
                                    nameof(searchParam.Code),
                                    string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, searchParam.Code, resource)));
                                break;
                            }
                        }
                    }
                    else if (_searchParameterDefinitionManager.TryGetSearchParameter(baseType.ToString(), searchParam.Code, out _))
                    {
                        // The search parameter's code value conflicts with an existing one
                        _logger.LogInformation("Search parameter definition has a conflicting code value with an existing one. code: {Code}, baseType: {BaseType}", searchParam.Code, baseType.ToString());
                        validationFailures.Add(
                        new ValidationFailure(
                            nameof(searchParam.Code),
                            string.Format(Resources.SearchParameterDefinitionConflictingCodeValue, searchParam.Code, baseType.ToString())));
                    }
                }
            }
        }
    }
}
