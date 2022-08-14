// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Compartment;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Models;
using NSubstitute;

namespace Microsoft.Health.Fhir.Store.Copy
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            ////var C:\Sergey\Development\FHIR - Server\src\Microsoft.Health.Fhir.Core\Data\R4
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(x => new FhirRequestContext("get", "https://localhost/Patient", "https://localhost", "correlation", new Dictionary<string, StringValues>(), new Dictionary<string, StringValues>()));

            var wrapperFactory = new ResourceWrapperFactory(
                                     new RawResourceFactory(new FhirJsonSerializer()),
                                     requestContextAccessor,
                                     Substitute.For<ISearchIndexer>(),
                                     Substitute.For<IClaimsExtractor>(),
                                     Substitute.For<ICompartmentIndexer>(),
                                     Substitute.For<ISearchParameterDefinitionManager>(),
                                     new ResourceDeserializer());

            var parser = new FhirJsonParser();
            var resource = parser.Parse<Resource>("{\"resourceType\": \"Patient\", \"id\": \"patient-to-update\", \"gender\": \"male\", \"birthDate\": \"2017-09-05\" }");
            var resourceElement = resource.ToResourceElement();
            var wrapper = wrapperFactory.Create(resourceElement, false, true);

            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            ////var typedElementToSearchValueConverterManager = SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync().Result;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            var searchIndexer = new TypedElementSearchIndexer(supportedSearchParameterDefinitionManager, new FhirTypedElementToSearchValueConverterManager(null), referenceToElementResolver, modelInfoProvider, logger);
        }
    }
}
