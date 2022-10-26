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

        private const string HttpPostName = "POST";
        private const string HttpPutName = "PUT";
        private const string HttpDeleteName = "DELETE";

        public SearchParameterValidator(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IAuthorizationService<DataActions> authorizationService,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _authorizationService = authorizationService;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
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

            var validationFailures = new List<ValidationFailure>();

            if (string.IsNullOrEmpty(searchParam.Url))
            {
                validationFailures.Add(
                    new ValidationFailure(nameof(Base.TypeName), Resources.SearchParameterDefinitionInvalidMissingUri));
            }
            else
            {
                // Checks if the url is a valid url
                if (!Uri.TryCreate(searchParam.Url, UriKind.Absolute, out _))
                {
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
                    validationFailures.Add(
                        new ValidationFailure(
                            nameof(searchParam.Code),
                            string.Format(Resources.SearchParameterDefinitionNullorEmptyCodeValue, searchParam.Code, baseType.ToString())));
                }
                else
                {
                    if (baseType.ToString() == KnownResourceTypes.Resource)
                    {
                        foreach (string resource in ModelInfoProvider.GetResourceTypeNames())
                        {
                            if (_searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, out _))
                            {
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
                        foreach (string resource in ModelInfoProvider.GetResourceTypeNames())
                        {
                            Type type = ModelInfoProvider.GetTypeForFhirType(resource);
                            string fhirBaseType = ModelInfoProvider.GetFhirTypeNameForType(type.BaseType);

                            if (fhirBaseType == KnownResourceTypes.DomainResource && _searchParameterDefinitionManager.TryGetSearchParameter(resource, searchParam.Code, out _))
                            {
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
