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
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Constants = Microsoft.Health.Fhir.Core.Features.Search.Parameters.Constants;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public class SearchParameterValidator : ISearchParameterValidator
    {
        private readonly Func<IScoped<IFhirOperationDataStore>> _fhirOperationDataStoreFactory;
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ILogger _logger;
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly ISearchParameterConflictingCodeValidator _searchParameterConflictingCodeValidator;

        private const string HttpPostName = "POST";
        private const string HttpPutName = "PUT";
        private const string HttpDeleteName = "DELETE";

        public SearchParameterValidator(
            Func<IScoped<IFhirOperationDataStore>> fhirOperationDataStoreFactory,
            IAuthorizationService<DataActions> authorizationService,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            ISearchParameterConflictingCodeValidator searchParameterConflictingCodeValidator,
            ILogger<SearchParameterValidator> logger)
        {
            EnsureArg.IsNotNull(fhirOperationDataStoreFactory, nameof(fhirOperationDataStoreFactory));
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));

            _fhirOperationDataStoreFactory = fhirOperationDataStoreFactory;
            _authorizationService = authorizationService;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _contextAccessor = contextAccessor;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
            _searchParameterConflictingCodeValidator = searchParameterConflictingCodeValidator;
        }

        public async Task ValidateSearchParameterInput(SearchParameter searchParam, string method, CancellationToken cancellationToken)
        {
            Uri duplicateOf = null;

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

            var validationFailures = new Collection<ValidationFailure>();

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

                        duplicateOf = _searchParameterConflictingCodeValidator.CheckForConflictingCodeValue(searchParam, validationFailures);
                    }
                }
            }

            if (validationFailures.Any())
            {
                throw new ResourceNotValidException(validationFailures);
            }
            else if (duplicateOf != null)
            {
                _contextAccessor.RequestContext.Properties.Add(Constants.DuplicateSearchParameterUrl, duplicateOf);
            }
        }
    }
}
