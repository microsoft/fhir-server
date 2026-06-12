// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SearchParamType = Microsoft.Health.Fhir.ValueSets.SearchParamType;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SearchParameterValueSetExpanderTests
    {
        private readonly ITerminologyServiceProxy _terminologyServiceProxy = Substitute.For<ITerminologyServiceProxy>();
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly SearchParameterValueSetExpander _expander;

        public SearchParameterValueSetExpanderTests()
        {
            _expander = new SearchParameterValueSetExpander(
                _terminologyServiceProxy,
                () => _searchParameterDefinitionManager);
        }

        [Fact]
        public async Task GivenTokenInModifier_WhenExpanding_ThenValueSetExpansionIsConvertedToTokenSearch()
        {
            const string valueSetUrl = "http://example.org/fhir/ValueSet/medications";
            var searchParameter = CreateSearchParameter(SearchParamType.Token);
            ConfigureSearchParameter("Medication", "code", searchParameter);

            _terminologyServiceProxy.ExpandAsync(
                Arg.Any<IReadOnlyList<Tuple<string, string>>>(),
                null,
                Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ValueSet
                {
                    Expansion = new ValueSet.ExpansionComponent
                    {
                        Contains = new List<ValueSet.ContainsComponent>
                        {
                            new ValueSet.ContainsComponent { System = "http://rx.example", Code = "a" },
                            new ValueSet.ContainsComponent { System = "http://rx.example", Code = "b" },
                        },
                    },
                }.ToResourceElement()));

            IReadOnlyList<Tuple<string, string>> result = await _expander.ExpandAsync(
                "Medication",
                new[] { Tuple.Create("code:in", valueSetUrl) },
                CancellationToken.None);

            Tuple<string, string> expanded = Assert.Single(result);
            Assert.Equal("code", expanded.Item1);
            Assert.Equal("http://rx.example|a,http://rx.example|b", expanded.Item2);

            await _terminologyServiceProxy.Received(1).ExpandAsync(
                Arg.Is<IReadOnlyList<Tuple<string, string>>>(x => x.Single().Item1 == TerminologyOperationParameterNames.Expand.Url && x.Single().Item2 == valueSetUrl),
                null,
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenNonTokenInModifier_WhenExpanding_ThenQueryParameterIsNotChanged()
        {
            var queryParameter = Tuple.Create("name:in", "http://example.org/fhir/ValueSet/names");
            ConfigureSearchParameter("Patient", "name", CreateSearchParameter(SearchParamType.String));

            IReadOnlyList<Tuple<string, string>> result = await _expander.ExpandAsync(
                "Patient",
                new[] { queryParameter },
                CancellationToken.None);

            Assert.Same(queryParameter, Assert.Single(result));
            await _terminologyServiceProxy.DidNotReceiveWithAnyArgs().ExpandAsync(default, default, default);
        }

        private void ConfigureSearchParameter(string resourceType, string code, SearchParameterInfo searchParameter)
        {
            _searchParameterDefinitionManager
                .TryGetSearchParameter(resourceType, code, out Arg.Any<SearchParameterInfo>())
                .Returns(x =>
                {
                    x[2] = searchParameter;
                    return true;
                });
        }

        private static SearchParameterInfo CreateSearchParameter(SearchParamType searchParamType)
        {
            return new SearchParameter
            {
                Name = "code",
                Code = "code",
                Type = Enum.Parse<Hl7.Fhir.Model.SearchParamType>(searchParamType.ToString()),
            }.ToInfo();
        }
    }
}
