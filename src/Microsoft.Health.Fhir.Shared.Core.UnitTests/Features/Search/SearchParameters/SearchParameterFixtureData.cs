// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.FhirPath;
using Hl7.FhirPath;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Test.Utilities;
using NSubstitute;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchParameterFixtureData
    {
        // this type is immutable and is safe to reuse
        private static FhirTypedElementToSearchValueConverterManager _fhirTypedElementToSearchValueConverterManager;

        private SearchParameterDefinitionManager _searchDefinitionManager;
        private SupportedSearchParameterDefinitionManager _supportedSearchDefinitionManager;
        private readonly IMediator _mediator = Substitute.For<IMediator>();

        static SearchParameterFixtureData()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        public static FhirPathCompiler Compiler { get; } = new FhirPathCompiler();

        public async Task<SearchParameterDefinitionManager> GetSearchDefinitionManagerAsync()
        {
            return _searchDefinitionManager ??= await CreateSearchParameterDefinitionManagerAsync(new VersionSpecificModelInfoProvider(), _mediator);
        }

        public async Task<SupportedSearchParameterDefinitionManager> GetSupportedSearchDefinitionManagerAsync()
        {
            return _supportedSearchDefinitionManager ??= new SupportedSearchParameterDefinitionManager(await GetSearchDefinitionManagerAsync());
        }

        public static async Task<FhirTypedElementToSearchValueConverterManager> GetFhirTypedElementToSearchValueConverterManagerAsync()
        {
            return _fhirTypedElementToSearchValueConverterManager ??= await CreateFhirTypedElementToSearchValueConverterManagerAsync();
        }

        private static async Task<FhirTypedElementToSearchValueConverterManager> CreateFhirTypedElementToSearchValueConverterManagerAsync()
        {
            var types = typeof(ITypedElementToSearchValueConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(ITypedElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var referenceSearchValueParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor());
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);

            var fhirElementToSearchValueConverters =
                types.Select(x => (ITypedElementToSearchValueConverter)Mock.TypeWithArguments(x, referenceSearchValueParser, codeSystemResolver));

            return new FhirTypedElementToSearchValueConverterManager(fhirElementToSearchValueConverters);
        }

        public static async Task<SearchParameterDefinitionManager> CreateSearchParameterDefinitionManagerAsync(IModelInfoProvider modelInfoProvider, IMediator mediator)
        {
            var searchService = Substitute.For<ISearchService>();
            var definitionManager = new SearchParameterDefinitionManager(modelInfoProvider, mediator, () => searchService.CreateMockScope(), NullLogger<SearchParameterDefinitionManager>.Instance);
            await definitionManager.StartAsync(CancellationToken.None);
            await definitionManager.EnsureInitializedAsync(CancellationToken.None);

            var statusRegistry = new FilebasedSearchParameterStatusDataStore(
                definitionManager,
                modelInfoProvider);
            var statusManager = new SearchParameterStatusManager(
                statusRegistry,
                definitionManager,
                new SearchParameterSupportResolver(await GetFhirTypedElementToSearchValueConverterManagerAsync()),
                Substitute.For<IMediator>(),
                NullLogger<SearchParameterStatusManager>.Instance);
            await statusManager.EnsureInitializedAsync(CancellationToken.None);

            return definitionManager;
        }
    }
}
