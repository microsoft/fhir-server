// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.Core.Models;
using CoreSearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    internal sealed class SemanticSearchTestParameterResolver : IVectorSearchParameterResolver
    {
        internal static readonly Uri DocumentReferenceCanonical = new Uri("https://azurehealthcareapis.com/search-parameters/document-reference-semantic-text");
        internal static readonly Uri ObservationCanonical = new Uri("https://azurehealthcareapis.com/search-parameters/observation-semantic-text");
        internal static readonly Uri DiagnosticReportCanonical = new Uri("https://azurehealthcareapis.com/search-parameters/diagnostic-report-semantic-text");
        internal static readonly Uri CoverageCanonical = new Uri("https://example.org/fhir/SearchParameter/coverage-semantic");

        private readonly IReadOnlyDictionary<string, SearchParameterInfo> _searchParameters = new Dictionary<string, SearchParameterInfo>(StringComparer.Ordinal)
        {
            [ResourceType.DocumentReference.ToString()] = new SearchParameterInfo(
                name: "DocumentReferenceSemantic",
                code: "semantic-text",
                searchParamType: CoreSearchParamType.Special,
                url: DocumentReferenceCanonical,
                expression: "DocumentReference.content.attachment.url.toString()",
                baseResourceTypes: new[] { ResourceType.DocumentReference.ToString() },
                vectorConfig: new VectorSearchParameterConfig { SourceStrategy = VectorTextSourceStrategy.LocalBinaryReference }),
            [ResourceType.Observation.ToString()] = new SearchParameterInfo(
                name: "ObservationSemantic",
                code: "semantic-text",
                searchParamType: CoreSearchParamType.Special,
                url: ObservationCanonical,
                expression: "Observation.note.text",
                baseResourceTypes: new[] { ResourceType.Observation.ToString() },
                vectorConfig: new VectorSearchParameterConfig()),
            [ResourceType.DiagnosticReport.ToString()] = new SearchParameterInfo(
                name: "DiagnosticReportSemantic",
                code: "semantic-text",
                searchParamType: CoreSearchParamType.Special,
                url: DiagnosticReportCanonical,
                expression: "DiagnosticReport.conclusion",
                baseResourceTypes: new[] { ResourceType.DiagnosticReport.ToString() },
                vectorConfig: new VectorSearchParameterConfig()),
            [ResourceType.Coverage.ToString()] = new SearchParameterInfo(
                name: "CoverageSemantic",
                code: "semantic",
                searchParamType: CoreSearchParamType.Special,
                url: CoverageCanonical,
                expression: "Coverage.class.name",
                baseResourceTypes: new[] { ResourceType.Coverage.ToString() },
                vectorConfig: new VectorSearchParameterConfig()),
        };

        public IReadOnlyList<SearchParameterInfo> GetSearchParameters(string resourceType)
        {
            return _searchParameters.TryGetValue(resourceType, out SearchParameterInfo searchParameter)
                ? new[] { searchParameter }
                : Array.Empty<SearchParameterInfo>();
        }

        public IReadOnlyList<SearchParameterInfo> GetIndexingSearchParameters(string resourceType)
        {
            return GetSearchParameters(resourceType);
        }

        public SearchParameterInfo GetSearchParameter(Uri canonicalUri)
        {
            return _searchParameters.Values.Single(searchParameter => searchParameter.Url == canonicalUri);
        }
    }
}
