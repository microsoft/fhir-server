// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
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
        static SearchParameterFixtureData()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();

            Compiler = new FhirPathCompiler();
            Manager = CreateFhirElementToSearchValueTypeConverterManager();
            SearchDefinitionManager = CreateSearchParameterDefinitionManager(new VersionSpecificModelInfoProvider());
            SupportedSearchDefinitionManager = new SupportedSearchParameterDefinitionManager(SearchDefinitionManager);
        }

        public static SearchParameterDefinitionManager SearchDefinitionManager { get; }

        public static SupportedSearchParameterDefinitionManager SupportedSearchDefinitionManager { get; }

        public static FhirNodeToSearchValueTypeConverterManager Manager { get; } = CreateFhirElementToSearchValueTypeConverterManager();

        public static FhirPathCompiler Compiler { get; }

        public static FhirNodeToSearchValueTypeConverterManager CreateFhirElementToSearchValueTypeConverterManager()
        {
            var types = typeof(IFhirNodeToSearchValueTypeConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(IFhirNodeToSearchValueTypeConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var referenceSearchValueParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor());
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            codeSystemResolver.Start();

            var fhirNodeToSearchValueTypeConverters =
                types.Select(x => (IFhirNodeToSearchValueTypeConverter)Mock.TypeWithArguments(x, referenceSearchValueParser, codeSystemResolver));

            return new FhirNodeToSearchValueTypeConverterManager(fhirNodeToSearchValueTypeConverters);
        }

        public static SearchParameterDefinitionManager CreateSearchParameterDefinitionManager(IModelInfoProvider modelInfoProvider)
        {
            if (Manager == null)
            {
                throw new InvalidOperationException($"{nameof(Manager)} was not instantiated.");
            }

            var definitionManager = new SearchParameterDefinitionManager(modelInfoProvider);
            definitionManager.Start();

            var statusRegistry = new FilebasedSearchParameterRegistry(
                definitionManager,
                modelInfoProvider);
            var statusManager = new SearchParameterStatusManager(
                statusRegistry,
                definitionManager,
                new SearchParameterSupportResolver(definitionManager, Manager),
                Substitute.For<IMediator>());
            statusManager.EnsureInitialized().GetAwaiter().GetResult();

            return definitionManager;
        }
    }
}
