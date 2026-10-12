// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using FluentValidation.Results;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Core.Features.Security.Authorization;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Features.Security.Authorization;
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
        private readonly IAuthorizationService<DataActions> _authorizationService;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterOperations _searchParameterOperations;
        private readonly ISearchParameterComparer<SearchParameterInfo> _searchParameterComparer;
        private readonly ILogger _logger;
        private readonly int _maxUrlLength = 128;

        private const string HttpPostName = "POST";
        private const string HttpPutName = "PUT";
        private const string HttpDeleteName = "DELETE";
        private const string HttpPatchName = "PATCH";

        public SearchParameterValidator(
            IAuthorizationService<DataActions> authorizationService,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider,
            ISearchParameterOperations searchParameterOperations,
            ISearchParameterComparer<SearchParameterInfo> searchParameterComparer,
            ILogger<SearchParameterValidator> logger)
        {
            EnsureArg.IsNotNull(authorizationService, nameof(authorizationService));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterOperations, nameof(searchParameterOperations));
            EnsureArg.IsNotNull(searchParameterComparer, nameof(searchParameterComparer));

            _authorizationService = authorizationService;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
            _searchParameterOperations = searchParameterOperations;
            _searchParameterComparer = searchParameterComparer;
            _logger = EnsureArg.IsNotNull(logger, nameof(logger));
        }

        public async Task<DateTimeOffset?> ValidateSearchParameterInput(SearchParameter searchParam, string method, CancellationToken cancellationToken, DateTimeOffset? lastUpdated = null)
        {
            await _authorizationService.CheckAccess(DataActions.Reindex, true, cancellationToken);

            if (string.IsNullOrEmpty(searchParam.Url) && (method.Equals(HttpDeleteName, StringComparison.Ordinal) || method.Equals(HttpPatchName, StringComparison.Ordinal)))
            {
                // Return out if this is delete OR patch call and no Url so FHIRController can move to next action
                return null;
            }

            var validationFailures = new List<ValidationFailure>();

            if (string.IsNullOrEmpty(searchParam.Url))
            {
                _logger.LogInformation("Search parameter definition is missing a url. url is null or empty.");
                validationFailures.Add(
                    new ValidationFailure(nameof(Base.TypeName), Resources.SearchParameterDefinitionInvalidMissingUri));
            }
            else if (searchParam.Url.Length > _maxUrlLength)
            {
                _logger.LogInformation("Search parameter definition has a url that exceeds the maximum length. url: {Url}, length: {Length}", searchParam.Url, searchParam.Url.Length);
                validationFailures.Add(
                    new ValidationFailure(nameof(searchParam.Url), string.Format(Resources.SearchParameterDefinitionInvalidUriExceedsMaxLength, searchParam.Url, _maxUrlLength)));
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
                    if (!lastUpdated.HasValue)
                    {
                        await _searchParameterOperations.GetAndApplySearchParameterUpdates(cancellationToken);
                        lastUpdated = _searchParameterOperations.SearchParamLastUpdated;
                    }

                    // If a search parameter with the same uri exists already
                    if (_searchParameterDefinitionManager.TryGetSearchParameter(searchParam.Url, out var searchParameterInfo))
                    {
                        // Block PUT operations on system-defined search parameters
                        // Note: DELETE without URL (from filter) will not reach this code, so system-defined
                        // check only applies to PUT operations where we have the actual resource body
                        if (searchParameterInfo.IsSystemDefined && method.Equals(HttpPutName, StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("Attempt to update a system-defined search parameter. url: {Url}", searchParam.Url);
                            throw new MethodNotAllowedException(string.Format(Resources.SearchParameterDefinitionSystemDefined, searchParam.Url));
                        }

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
                        // PUT is allowed to create new resources (upsert behavior)
                        CheckForConflictingCodeValue(searchParam, validationFailures);
                    }
                }
            }

            if (validationFailures.Any())
            {
                throw new ResourceNotValidException(validationFailures);
            }

            return lastUpdated.Value; // value should not be null here.
        }

        private void CheckForConflictingCodeValue(SearchParameter searchParam, List<ValidationFailure> validationFailures)
        {
            SearchParameterConflictRules.CheckForConflictingCodeValue(
                searchParam,
                _searchParameterDefinitionManager,
                _modelInfoProvider,
                _searchParameterComparer,
                _logger,
                validationFailures);
        }
    }
}
