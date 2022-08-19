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
using Microsoft.Health.Fhir.R4.ResourceParser.Code;

namespace Microsoft.Health.Fhir.R4.ResourceParser
{
    public class ResourceWrapperParser
    {
        private ResourceWrapperFactory _resourceWrapperFactory;
        private FhirJsonParser _fhirJsonParser;
        private FhirJsonSerializer _fhirJsonSerializer;
        private IModelInfoProvider _modelInfoProvider;

        public ResourceWrapperParser()
        {
            var fhirRequestContextAccessor = new ExecutableRequestContextAccessor();
            var referenceSearchValueParser = new ReferenceSearchValueParser(fhirRequestContextAccessor);
            var modelInfoProvider = new VersionSpecificModelInfoProvider();
            var searchParameterDefinitionManager = new MinimalSearchParameterDefinitionManager(modelInfoProvider);

            var searchableSearchParameterDefinitionManager = new SearchableSearchParameterDefinitionManager(searchParameterDefinitionManager, fhirRequestContextAccessor);

            var supportedSearchParameterDefinitionManager = new SupportedSearchParameterDefinitionManager(searchParameterDefinitionManager);

            var filebasedSearchParameterStatusDataStore = new FilebasedSearchParameterStatusDataStore(searchParameterDefinitionManager, modelInfoProvider);

            var codeSystemResolver = new CodeSystemResolver(modelInfoProvider);
            var fhirTypedElementConverters = MakeConverters(fhirRequestContextAccessor, codeSystemResolver);
            var fhirTypedElementToSearchValueConverterManager = new FhirTypedElementToSearchValueConverterManager(fhirTypedElementConverters);

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

            var definitionManagerTask = searchParameterDefinitionManager.StartAsync(CancellationToken.None);
            var resolverTask = codeSystemResolver.StartAsync(CancellationToken.None);
            var comparmentTask = compartmentDefinitionManager.StartAsync(CancellationToken.None);

            var startupTasks = System.Threading.Tasks.Task.WhenAll(definitionManagerTask, resolverTask, comparmentTask);
            while (!startupTasks.IsCompleted)
            {
                Thread.Sleep(500);
            }

            fhirRequestContextAccessor.RequestContext = new FhirRequestContext("EXE", "http://null/", "https://null/", "null", new Dictionary<string, StringValues>(), new Dictionary<string, StringValues>());

            _resourceWrapperFactory = resourceWrapperFactory;
            _fhirJsonParser = fhirJsonParser;
            _fhirJsonSerializer = fhirJsonSerializer;
            _modelInfoProvider = modelInfoProvider;
        }

        public ResourceWrapper CreateResourceWrapper(string input)
        {
            Base resource = _fhirJsonParser.Parse(input);
            return _resourceWrapperFactory.Create(new ResourceElement(resource.ToTypedElement()), false, true);
        }

        public string SerializeToString(ResourceWrapper wrapper)
        {
            return _fhirJsonSerializer.SerializeToString(_fhirJsonParser.Parse(wrapper.RawResource.ToITypedElement(_modelInfoProvider)));
        }

        private static IEnumerable<ITypedElementToSearchValueConverter> MakeConverters(RequestContextAccessor<IFhirRequestContext> requestContextAccessor, ICodeSystemResolver codeSystemResolver)
        {
            var fhirTypedElementConverters = new List<ITypedElementToSearchValueConverter>();
            var referenceSearchValueParser = new ReferenceSearchValueParser(requestContextAccessor);

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
