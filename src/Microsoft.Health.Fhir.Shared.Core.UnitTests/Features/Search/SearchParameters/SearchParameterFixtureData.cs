// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Hl7.Fhir.FhirPath;
using Hl7.FhirPath;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
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
        static SearchParameterFixtureData()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();

            Compiler = new FhirPathCompiler();
            Manager = CreateFhirElementToSearchValueTypeConverterManager();
            SearchDefinitionManager = CreateSearchParameterDefinitionManager(new VersionSpecificModelInfoProvider());
        }

        public static SearchParameterDefinitionManager SearchDefinitionManager { get; }

        public static FhirElementToSearchValueTypeConverterManager Manager { get; }

        public static FhirPathCompiler Compiler { get; }

        public static FhirElementToSearchValueTypeConverterManager CreateFhirElementToSearchValueTypeConverterManager()
        {
            var types = typeof(IFhirElementToSearchValueTypeConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(IFhirElementToSearchValueTypeConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var fhirElementToSearchValueTypeConverters =
                types.Select(x => (IFhirElementToSearchValueTypeConverter)Mock.TypeWithArguments(x, new ReferenceSearchValueParser(new FhirRequestContextAccessor())));

            return new FhirElementToSearchValueTypeConverterManager(fhirElementToSearchValueTypeConverters);
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
