// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class SearchParameterOperations : ISearchParameterOperations
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;
        private IModelInfoProvider _modelInfoProvider;

        public SearchParameterOperations(
            SearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterDefinitionManager searchParameterDefinitionManager,
            IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
            _modelInfoProvider = modelInfoProvider;
        }

        public async Task AddSearchParameterAsync(ITypedElement searchParam)
        {
            try
            {
                _searchParameterDefinitionManager.AddNewSearchParameters(new List<ITypedElement>() { searchParam });

                var searchParameterUrl = searchParam.GetStringScalar("url");
                await _searchParameterStatusManager.AddSearchParameterStatusAsync(new List<string>() { searchParameterUrl });
            }
            catch (FhirException fex)
            {
                fex.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    Core.Resources.CustomSearchCreateError));

                throw;
            }
            catch (Exception ex)
            {
                var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchCreateError);
                customSearchException.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                throw customSearchException;
            }
        }

        public async Task DeleteSearchParameterAsync(RawResource searchParamResource)
        {
            try
            {
                var searchParam = _modelInfoProvider.ToTypedElement(searchParamResource);
                var searchParameterUrl = searchParam.GetStringScalar("url");

                // First we delete the status metadata from the data store as this fuction depends on the
                // the in memory definition manager.  Once complete we remove the SearchParameter from
                // the definition manager.
                await _searchParameterStatusManager.DeleteSearchParameterStatusAsync(searchParameterUrl);
                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam);
            }
            catch (FhirException fex)
            {
                fex.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    Core.Resources.CustomSearchDeleteError));

                throw;
            }
            catch (Exception ex)
            {
                var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchDeleteError);
                customSearchException.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                throw customSearchException;
            }
        }

        public async Task UpdateSearchParameterAsync(ITypedElement searchParam, RawResource prevSearchParamRaw)
        {
            try
            {
                var searchParameterWrapper = new SearchParameterWrapper(searchParam);

                var prevSearchParam = _modelInfoProvider.ToTypedElement(prevSearchParamRaw);
                var prevSearchParamUrl = prevSearchParam.GetStringScalar("url");

                // As any part of the SearchParameter may have been changed, including the URL
                // the most reliable method of updating the SearchParameter is to delete the previous
                // data and insert the updated version
                await _searchParameterStatusManager.DeleteSearchParameterStatusAsync(prevSearchParamUrl);
                _searchParameterDefinitionManager.DeleteSearchParameter(prevSearchParam);
                _searchParameterDefinitionManager.AddNewSearchParameters(new List<ITypedElement>() { searchParam });
                await _searchParameterStatusManager.AddSearchParameterStatusAsync(new List<string>() { searchParameterWrapper.Url });
            }
            catch (FhirException fex)
            {
                fex.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    Core.Resources.CustomSearchUpdateError));

                throw;
            }
            catch (Exception ex)
            {
                var customSearchException = new ConfigureCustomSearchException(Core.Resources.CustomSearchUpdateError);
                customSearchException.Issues.Add(new OperationOutcomeIssue(
                    OperationOutcomeConstants.IssueSeverity.Error,
                    OperationOutcomeConstants.IssueType.Exception,
                    ex.Message));

                throw customSearchException;
            }
        }
    }
}
