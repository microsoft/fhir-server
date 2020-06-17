// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.FhirPath;
using Hl7.FhirPath;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
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
        public SearchParameterFixtureData()
        {
            SearchDefinitionManager = CreateSearchParameterDefinitionManager();
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        public SearchParameterDefinitionManager SearchDefinitionManager { get; }

        public static FhirNodeToSearchValueTypeConverterManager Manager { get; } = CreateFhirElementToSearchValueTypeConverterManager();

        public static FhirPathCompiler Compiler { get; } = new FhirPathCompiler();

        public static FhirNodeToSearchValueTypeConverterManager CreateFhirElementToSearchValueTypeConverterManager()
        {
            var types = typeof(IFhirNodeToSearchValueTypeConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(IFhirNodeToSearchValueTypeConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var fhirNodeToSearchValueTypeConverters =
                types.Select(x => (IFhirNodeToSearchValueTypeConverter)Mock.TypeWithArguments(x, new ReferenceSearchValueParser(new FhirRequestContextAccessor())));

            return new FhirNodeToSearchValueTypeConverterManager(fhirNodeToSearchValueTypeConverters);
        }

        public static SearchParameterDefinitionManager CreateSearchParameterDefinitionManager()
        {
            var manager = new SearchParameterDefinitionManager(ModelInfoProvider.Instance);
            manager.Start();

            var statusRegistry = new FilebasedSearchParameterRegistry(
                manager,
                ModelInfoProvider.Instance);
            var statusManager = new SearchParameterStatusManager(
                statusRegistry,
                manager,
                new SearchParameterSupportResolver(manager, Manager),
                Substitute.For<IMediator>());
            statusManager.EnsureInitialized().GetAwaiter().GetResult();

            return manager;
        }
    }
}
