// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Concurrent;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Tests.Common;
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
        public void GivenEmbeddedR4MicrosoftSearchParameters_WhenBuilt_ThenVectorDefinitionsAreNotSystemDefined()
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

            Assert.DoesNotContain(uriDictionary.Values, definition => definition.VectorConfig != null);
            Assert.True(uriDictionary.TryGetValue("https://azurehealthcareapis.com/data-extensions/expiry-date", out SearchParameterInfo expiryDate));
            Assert.True(expiryDate.IsSystemDefined);
        }
    }
}
