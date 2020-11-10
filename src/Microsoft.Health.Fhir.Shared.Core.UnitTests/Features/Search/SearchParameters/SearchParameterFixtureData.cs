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
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Test.Utilities;
using NSubstitute;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchParameterFixtureData
    {
        // this type is immutable and is safe to reuse
        private static FhirNodeToSearchValueTypeConverterManager _fhirNodeToSearchValueTypeConverterManager;

        private SearchParameterDefinitionManager _searchDefinitionManager;
        private SupportedSearchParameterDefinitionManager _supportedSearchDefinitionManager;

        static SearchParameterFixtureData()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        public static FhirPathCompiler Compiler { get; } = new FhirPathCompiler();

        public async Task<SearchParameterDefinitionManager> GetSearchDefinitionManagerAsync()
        {
            return _searchDefinitionManager ??= await CreateSearchParameterDefinitionManagerAsync(new VersionSpecificModelInfoProvider());
        }

        public async Task<SupportedSearchParameterDefinitionManager> GetSupportedSearchDefinitionManagerAsync()
        {
            return _supportedSearchDefinitionManager ??= new SupportedSearchParameterDefinitionManager(await GetSearchDefinitionManagerAsync());
        }

        public static async Task<FhirNodeToSearchValueTypeConverterManager> GetFhirNodeToSearchValueTypeConverterManagerAsync()
        {
            return _fhirNodeToSearchValueTypeConverterManager ??= await CreateFhirElementToSearchValueTypeConverterManagerAsync();
        }

        private static async Task<FhirNodeToSearchValueTypeConverterManager> CreateFhirElementToSearchValueTypeConverterManagerAsync()
        {
            var types = typeof(IFhirNodeToSearchValueTypeConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(IFhirNodeToSearchValueTypeConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var referenceSearchValueParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor());
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);

            var fhirNodeToSearchValueTypeConverters =
                types.Select(x => (IFhirNodeToSearchValueTypeConverter)Mock.TypeWithArguments(x, referenceSearchValueParser, codeSystemResolver));

            return new FhirNodeToSearchValueTypeConverterManager(fhirNodeToSearchValueTypeConverters);
        }

        public static async Task<SearchParameterDefinitionManager> CreateSearchParameterDefinitionManagerAsync(IModelInfoProvider modelInfoProvider)
        {
            var definitionManager = new SearchParameterDefinitionManager(modelInfoProvider);
            await definitionManager.StartAsync(CancellationToken.None);

            var statusRegistry = new FilebasedSearchParameterRegistry(
                definitionManager,
                modelInfoProvider);
            var statusManager = new SearchParameterStatusManager(
                statusRegistry,
                definitionManager,
                new SearchParameterSupportResolver(definitionManager, await GetFhirNodeToSearchValueTypeConverterManagerAsync()),
                Substitute.For<IMediator>());
            await statusManager.EnsureInitialized();

            return definitionManager;
        }
    }
}
