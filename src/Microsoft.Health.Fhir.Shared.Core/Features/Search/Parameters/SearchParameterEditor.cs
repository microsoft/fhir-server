// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public class SearchParameterEditor : ISearchParameterEditor
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly ISearchParameterRegistry _searchParameterRegistry;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;

        public SearchParameterEditor(
            ISearchParameterRegistry searchParameterRegistry,
            SearchParameterStatusManager searchParameterStatusManager,
            ISearchParameterDefinitionManager searchParameterDefinitionManager)
        {
            EnsureArg.IsNotNull(searchParameterRegistry, nameof(searchParameterRegistry));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));
            EnsureArg.IsNotNull(searchParameterDefinitionManager, nameof(searchParameterDefinitionManager));

            _searchParameterRegistry = searchParameterRegistry;
            _searchParameterStatusManager = searchParameterStatusManager;
            _searchParameterDefinitionManager = searchParameterDefinitionManager;
        }

        public async System.Threading.Tasks.Task AddSearchParameterAsync(SearchParameter searchParam, CancellationToken cancellationToken)
        {
            // create version agnostic bundle
            searchParam.ToTypedElement();
            var bundle = new Bundle();
            bundle.AddResourceEntry(searchParam, searchParam.Url);
            var bundleWrapper = new BundleWrapper(bundle.ToResourceElement().Instance);

            // add search parameter to definition manager
            _searchParameterDefinitionManager.AddNewSearchParameters(bundleWrapper);

            // update and persist the status of the new search parameter
            await _searchParameterStatusManager.AddSearchParameterStatus(searchParam.Url);
        }
    }
}
