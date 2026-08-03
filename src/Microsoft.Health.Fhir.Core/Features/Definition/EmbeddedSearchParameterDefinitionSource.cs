// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Definition
{
    /// <summary>
    /// Reads the system search parameter definitions from the embedded resources exactly once per source instance.
    /// </summary>
    public sealed class EmbeddedSearchParameterDefinitionSource : ISearchParameterDefinitionSource
    {
        private const string SpecificationBundleResourceName = "search-parameters.json";
        private const string MicrosoftBundleResourceName = "ms-search-parameters.json";

        private readonly Lazy<IReadOnlyList<ITypedElement>> _systemSearchParameterResources;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbeddedSearchParameterDefinitionSource"/> class.
        /// </summary>
        /// <param name="modelInfoProvider">Provides access to version-specific embedded definition resources.</param>
        public EmbeddedSearchParameterDefinitionSource(IModelInfoProvider modelInfoProvider)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));

            _systemSearchParameterResources = new Lazy<IReadOnlyList<ITypedElement>>(
                () => Read(modelInfoProvider),
                isThreadSafe: true);
        }

        /// <inheritdoc />
        public IReadOnlyList<ITypedElement> GetSystemSearchParameterResources() => _systemSearchParameterResources.Value;

        private static List<ITypedElement> Read(IModelInfoProvider modelInfoProvider)
        {
            BundleWrapper specificationBundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(SpecificationBundleResourceName, modelInfoProvider);
            BundleWrapper microsoftBundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters(MicrosoftBundleResourceName, modelInfoProvider);

            var resources = specificationBundle.Entries.Select(e => e.Resource).ToList();
            resources.AddRange(microsoftBundle.Entries.Select(e => e.Resource));

            return resources;
        }
    }
}
