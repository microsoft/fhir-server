// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Serialization;
using Hl7.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    public class SearchParameterFixtureData
    {
        public SearchParameterFixtureData()
        {
            Compiler = new FhirPathCompiler();

            var types = typeof(IFhirElementToSearchValueTypeConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(IFhirElementToSearchValueTypeConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var fhirElementToSearchValueTypeConverters =
                types.Select(x => (IFhirElementToSearchValueTypeConverter)Mock.TypeWithArguments(x));

            Manager = new FhirElementToSearchValueTypeConverterManager(fhirElementToSearchValueTypeConverters);

            SearchDefinitionManager = CreateSearchParameterDefinitionManager();
        }

        public SearchParameterDefinitionManager SearchDefinitionManager { get; set; }

        public FhirElementToSearchValueTypeConverterManager Manager { get; set; }

        public FhirPathCompiler Compiler { get; set; }

        public static SearchParameterDefinitionManager CreateSearchParameterDefinitionManager()
        {
            var manager = new SearchParameterDefinitionManager(new FhirJsonParser(), ModelInfoProvider.Instance);
            manager.Start();
            return manager;
        }
    }
}
