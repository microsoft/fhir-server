// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Definition
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class SemanticSearchParameterDefinitionTests
    {
        [Fact]
        public void GivenEmbeddedR4MicrosoftSearchParameters_WhenBuilt_ThenSemanticDefinitionsAreSystemDefined()
        {
            var uriDictionary = new ConcurrentDictionary<string, SearchParameterInfo>();
            var resourceTypeDictionary = new ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentQueue<string>>>();
            var searchParameterComparer = new SearchParameterComparer(Substitute.For<ILogger<ISearchParameterComparer<SearchParameterInfo>>>());
            var bundle = SearchParameterDefinitionBuilder.ReadEmbeddedSearchParameters("ms-search-parameters.json", ModelInfoProvider.Instance);

            SearchParameterDefinitionBuilder.Build(
                bundle.Entries.Select(entry => entry.Resource).ToList(),
                uriDictionary,
                resourceTypeDictionary,
                ModelInfoProvider.Instance,
                searchParameterComparer,
                NullLogger.Instance,
                isSystemDefined: true);

            AssertDefinition(
                "https://azurehealthcareapis.com/search-parameters/observation-semantic-text",
                "Observation",
                "Observation.note.text",
                VectorTextSourceStrategy.DirectText);
            AssertDefinition(
                "https://azurehealthcareapis.com/search-parameters/diagnostic-report-semantic-text",
                "DiagnosticReport",
                "DiagnosticReport.conclusion",
                VectorTextSourceStrategy.DirectText);
            AssertDefinition(
                "https://azurehealthcareapis.com/search-parameters/document-reference-semantic-text",
                "DocumentReference",
                "DocumentReference.content.attachment.url.toString()",
                VectorTextSourceStrategy.LocalBinaryReference);

            void AssertDefinition(string canonicalUrl, string resourceType, string expression, VectorTextSourceStrategy sourceStrategy)
            {
                Assert.True(uriDictionary.TryGetValue(canonicalUrl, out SearchParameterInfo definition));
                Assert.True(definition.IsSystemDefined);
                Assert.Equal("semantic-text", definition.Code);
                Assert.Equal(SearchParamType.Special, definition.Type);
                Assert.Equal("active", definition.DefinitionStatus);
                Assert.Equal(expression, definition.Expression);
                Assert.Contains(resourceType, definition.BaseResourceTypes);
                Assert.NotNull(definition.VectorConfig);
                Assert.Equal(sourceStrategy, definition.VectorConfig.SourceStrategy);
                Assert.Equal(VectorTextExtractionPolicy.PerValueRow, definition.VectorConfig.ExtractionPolicy);
                Assert.Equal(8000, definition.VectorConfig.MaxInputTokens);
                Assert.Contains(canonicalUrl, resourceTypeDictionary[resourceType]["semantic-text"]);
            }
        }
    }
}
