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
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public class SearchParameterUtilities : ISearchParameterUtilities
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;

        public SearchParameterUtilities(
            SearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public async Task AddSearchParameterAsync(ITypedElement searchParam)
        {
            try
            {
                _searchParameterDefinitionManager.AddNewSearchParameters(new List<ITypedElement>() { searchParam });

                var searchParameterWrapper = new SearchParameterWrapper(searchParam);
                await _searchParameterStatusManager.AddSearchParameterStatusAsync(new List<string>() { searchParameterWrapper.Url });
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

        public async Task DeleteSearchParameterAsync(ITypedElement searchParam)
        {
            try
            {
                _searchParameterDefinitionManager.DeleteSearchParameter(searchParam);

                var searchParameterWrapper = new SearchParameterWrapper(searchParam);
                await _searchParameterStatusManager.DeleteSearchParameterStatusAsync(searchParameterWrapper.Url);
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
    }
}
