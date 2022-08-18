// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using ResourceParser.Code;

namespace ResourceParser
{
    public static class Program
    {
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        private static RequestContextAccessor<IFhirRequestContext> _requestContextAccessor;
        private static ResourceWrapperFactory _resourceWrapperFactory;
        private static FhirJsonParser _fhirJsonParser;
        private static FhirJsonSerializer _fhirJsonSerializer;
        private static IModelInfoProvider _modelInfoProvider;
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        public static void Main(string[] args)
        {
            Startup();

            _requestContextAccessor.RequestContext = new FhirRequestContext("EXE", "http://null/", "https://null/", "null", new Dictionary<string, StringValues>(), new Dictionary<string, StringValues>());

            string input = "{\"resourceType\": \"Patient\", \"gender\": \"male\"}";
            Base resource = _fhirJsonParser.Parse(input);
            var resourceWrapper = _resourceWrapperFactory.Create(new ResourceElement(resource.ToTypedElement()), false, true);
            var output = _fhirJsonSerializer.SerializeToString(_fhirJsonParser.Parse(resourceWrapper.RawResource.ToITypedElement(_modelInfoProvider)));
            Console.Write(output);
        }

        public static void Startup()
        {
            var fhirRequestContextAccessor = new ExecutableRequestContextAccessor();
            var referenceSearchValueParser = new ReferenceSearchValueParser(fhirRequestContextAccessor);
            var modelInfoProvider = new VersionSpecificModelInfoProvider();
            var searchParameterDefinitionManager = new MinimalSearchParameterDefinitionManager(modelInfoProvider);

            var searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(searchParameterDefinitionManager, fhirRequestContextAccessor);

            var supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(searchParameterDefinitionManager);

            var filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(searchParameterDefinitionManager, modelInfoProvider);

            var fhirTypedElementConverters = MakeConverters(fhirRequestContextAccessor, modelInfoProvider);
            var fhirTypedElementToSearchValueConverterManager = new FhirTypedElementToSearchValueConverterManager(fhirTypedElementConverters);

            var codeSystemResolver = new CodeSystemResolver(modelInfoProvider);

            var searchParameterExpressionParser = new SearchParameterExpressionParser(referenceSearchValueParser);
            var expressionParser = new ExpressionParser(() => searchableSearchParameterDefinitionManager, searchParameterExpressionParser);

            // var searchOptionsFactory = new SearchOptionsFactory();
            var referenceToElementResolver = new LightweightReferenceToElementResolver(referenceSearchValueParser, modelInfoProvider);

            var logger = new NullLogger<TypedElementSearchIndexer>();
            var searchIndexer = new TypedElementSearchIndexer(supportedSearchParameterDefinitionManager, fhirTypedElementToSearchValueConverterManager, referenceToElementResolver, modelInfoProvider, logger);

            var compartmentDefinitionManager = new CompartmentDefinitionManager(modelInfoProvider);

            var compartmentIndexer = new CompartmentIndexer(compartmentDefinitionManager);

            var fhirJsonSerializer = new FhirJsonSerializer();
            var fhirJsonParser = new FhirJsonParser();
            var rawResourceFactory = new RawResourceFactory(fhirJsonSerializer);
            var claimsExtractor = new MockClaimsExtractor();
            var resourceDeserializer = new ResourceDeserializer((FhirResourceFormat.Json, new Func<string, string, DateTimeOffset, ResourceElement>((str, version, lastUpdated) => fhirJsonParser.Parse(str).ToResourceElement())));
            var resourceWrapperFactory = new ResourceWrapperFactory(rawResourceFactory, fhirRequestContextAccessor, searchIndexer, claimsExtractor, compartmentIndexer, searchParameterDefinitionManager, resourceDeserializer);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            searchParameterDefinitionManager.StartAsync(CancellationToken.None);
            codeSystemResolver.StartAsync(CancellationToken.None);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed

            _resourceWrapperFactory = resourceWrapperFactory;
            _requestContextAccessor = fhirRequestContextAccessor;
            _fhirJsonParser = fhirJsonParser;
            _fhirJsonSerializer = fhirJsonSerializer;
            _modelInfoProvider = modelInfoProvider;
        }

        private static IEnumerable<ITypedElementToSearchValueConverter> MakeConverters(RequestContextAccessor<IFhirRequestContext> requestContextAccessor, IModelInfoProvider modelInfoProvider)
        {
            var fhirTypedElementConverters = new List<ITypedElementToSearchValueConverter>();
            var referenceSearchValueParser = new ReferenceSearchValueParser(requestContextAccessor);
            var codeSystemResolver = new CodeSystemResolver(modelInfoProvider);

            fhirTypedElementConverters.Add(new AddressToStringSearchValueConverter());
            fhirTypedElementConverters.Add(new BooleanToTokenSearchValueConverter());
            fhirTypedElementConverters.Add(new CanonicalToUriSearchValueConverter());
            fhirTypedElementConverters.Add(new CodeableConceptToTokenSearchValueConverter());
            fhirTypedElementConverters.Add(new CodeableReferenceToReferenceSearchValueConverter(referenceSearchValueParser));
            fhirTypedElementConverters.Add(new CodeableReferenceToTokenSearchValueConverter());
            fhirTypedElementConverters.Add(new CodeToTokenSearchValueConverter(codeSystemResolver));
            fhirTypedElementConverters.Add(new CodingToTokenSearchValueConverter());
            fhirTypedElementConverters.Add(new ContactPointToTokenSearchValueConverter());
            fhirTypedElementConverters.Add(new DateToDateTimeSearchValueConverter());
            fhirTypedElementConverters.Add(new DecimalToNumberSearchValueConverter());
            fhirTypedElementConverters.Add(new HumanNameToStringSearchValueConverter());
            fhirTypedElementConverters.Add(new IdentifierToTokenSearchValueConverter());
            fhirTypedElementConverters.Add(new IdToTokenSearchValueConverter());
            fhirTypedElementConverters.Add(new InstantToDateTimeSearchValueConverter());
            fhirTypedElementConverters.Add(new IntegerToNumberSearchValueConverter());
            fhirTypedElementConverters.Add(new MarkdownToStringSearchValueConverter());
            fhirTypedElementConverters.Add(new MoneyToQuantitySearchValueConverter());
            fhirTypedElementConverters.Add(new OidToUriSearchValueConverter());
            fhirTypedElementConverters.Add(new PeriodToDateTimeSearchValueConverter());
            fhirTypedElementConverters.Add(new QuantityToQuantitySearchValueConverter());
            fhirTypedElementConverters.Add(new RangeToNumberSearchValueConverter());
            fhirTypedElementConverters.Add(new RangeToQuantitySearchValueConverter());
            fhirTypedElementConverters.Add(new ResourceReferenceToReferenceSearchValueConverter(referenceSearchValueParser));
            fhirTypedElementConverters.Add(new StringToStringSearchValueConverter());
            fhirTypedElementConverters.Add(new StringToTokenSearchValueConverter());
            fhirTypedElementConverters.Add(new UriToReferenceSearchValueConverter(referenceSearchValueParser));
            fhirTypedElementConverters.Add(new UriToUriSearchValueConverter());

            return fhirTypedElementConverters;
        }
    }
}
