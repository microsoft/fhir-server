// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Search
{
    public class SearchCapabilityProvider : IProvideCapability
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterDefinitionManager _parameterDefinitionManager;

        public SearchCapabilityProvider(
            IModelInfoProvider modelInfoProvider,
            ISearchParameterDefinitionManager parameterDefinitionManager)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(parameterDefinitionManager, nameof(parameterDefinitionManager));

            _modelInfoProvider = modelInfoProvider;
            _parameterDefinitionManager = parameterDefinitionManager;
        }

        public void Build(ICapabilityStatementBuilder builder)
        {
            foreach (var resource in _modelInfoProvider.GetResourceTypeNames())
            {
                IEnumerable<SearchParameterInfo> searchParams = _parameterDefinitionManager.GetSearchParameters(resource);

                builder.TryAddSearchParams(resource, searchParams.Select(x => new SearchParamComponent
                {
                    Name = x.Name,
                    Type = x.Type,
                }));

                builder.TryAddRestInteraction(resource, TypeRestfulInteraction.SearchType);
            }
        }
    }
}
