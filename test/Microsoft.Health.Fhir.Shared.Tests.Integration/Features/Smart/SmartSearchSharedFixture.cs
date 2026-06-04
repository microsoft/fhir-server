// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Tests.Common.FixtureParameters;
using Microsoft.Health.Fhir.Tests.Integration.Persistence;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit.Abstractions;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Tests.Integration.Features.Smart
{
    public sealed class SmartSearchSharedFixture
    {
        private static readonly ConcurrentDictionary<SmartSearchSharedFixtureKey, Lazy<Task<SmartSearchSharedContext>>> SharedContexts = new();
        private static int _processExitCleanupRegistered;
        private readonly DataStore _dataStore;

        public SmartSearchSharedFixture(DataStore dataStore)
        {
            _dataStore = dataStore;
            RegisterProcessExitCleanup();
        }

        public DataStore DataStore => _dataStore;

        public Task<SmartSearchSharedContext> GetContextAsync(ITestOutputHelper output)
        {
            var key = new SmartSearchSharedFixtureKey(ModelInfoProvider.Instance.Version, _dataStore);
            return SharedContexts.GetOrAdd(
                key,
                _ => new Lazy<Task<SmartSearchSharedContext>>(() => CreateContextAsync(_dataStore, output), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }

        private static async Task<SmartSearchSharedContext> CreateContextAsync(DataStore dataStore, ITestOutputHelper output)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            var fixture = new FhirStorageTestsFixture(dataStore);
            await fixture.InitializeAsync();

            void LogTiming(string message)
            {
                output.WriteLine($"[SmartSearchTiming] {ModelInfoProvider.Instance.Version}/{fixture.DataStore.GetType().Name}: {message}");
            }

            async Task<T> MeasureAsync<T>(string operationName, Func<Task<T>> action)
            {
                Stopwatch operationStopwatch = Stopwatch.StartNew();
                try
                {
                    return await action();
                }
                finally
                {
                    LogTiming($"{operationName} completed in {operationStopwatch.Elapsed.TotalSeconds:F3}s.");
                }
            }

            var dataStoreSearchParameterValidator = Substitute.For<IDataStoreSearchParameterValidator>();
            dataStoreSearchParameterValidator.ValidateSearchParameter(default, out Arg.Any<string>()).ReturnsForAnyArgs(x =>
            {
                x[1] = null;
                return true;
            });

            var searchParameterSupportResolver = Substitute.For<ISearchParameterSupportResolver>();
            searchParameterSupportResolver.IsSearchParameterSupported(Arg.Any<SearchParameterInfo>()).Returns((true, false));

            var typedElementToSearchValueConverterManager = await MeasureAsync(
                "CreateFhirTypedElementToSearchValueConverterManagerAsync",
                CreateFhirTypedElementToSearchValueConverterManagerAsync);

            var searchIndexer = new TypedElementSearchIndexer(
                fixture.SupportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                NullLogger<TypedElementSearchIndexer>.Instance);

            ResourceWrapperFactory wrapperFactory = Mock.TypeWithArguments<ResourceWrapperFactory>(
                new RawResourceFactory(new FhirJsonSerializer()),
                new FhirRequestContextAccessor(),
                searchIndexer,
                fixture.SearchParameterDefinitionManager,
                Deserializers.ResourceDeserializer);

            var context = new SmartSearchSharedContext(
                fixture,
                fixture.OperationDataStore,
                fixture.TestHelper,
                fixture.DataStore.CreateMockScope(),
                fixture.SearchParameterDefinitionManager,
                fixture.SupportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                searchIndexer,
                fixture.SearchParameterStatusManager);

            async Task LoadBundleAsync(string sampleName)
            {
                Stopwatch bundleStopwatch = Stopwatch.StartNew();
                var smartBundle = Samples.GetJsonSample<Bundle>(sampleName);
                var upserted = 0;

                foreach (var entry in smartBundle.Entry)
                {
                    await MeasureAsync($"Upsert {sampleName}/{entry.Resource.TypeName}/{entry.Resource.Id}", () => context.UpsertResource(entry.Resource));
                    upserted++;
                }

                LogTiming($"Loaded bundle {sampleName}: resources={upserted}, elapsed={bundleStopwatch.Elapsed.TotalSeconds:F3}s.");
            }

            await LoadBundleAsync("SmartPatientA");
            await LoadBundleAsync("SmartPatientB");
            await LoadBundleAsync("SmartPatientC");
            await LoadBundleAsync("SmartPatientD");
            await LoadBundleAsync("SmartCommon");

            await MeasureAsync("Upsert Medication", () => context.UpsertResource(Samples.GetJsonSample<Medication>("Medication")));
            await MeasureAsync("Upsert Organization", () => context.UpsertResource(Samples.GetJsonSample<Organization>("Organization")));
            await MeasureAsync("Upsert Location-example-hq", () => context.UpsertResource(Samples.GetJsonSample<Location>("Location-example-hq")));

            LogTiming($"Shared SMART context initialization completed in {stopwatch.Elapsed.TotalSeconds:F3}s.");
            return context;
        }

        private static async Task<FhirTypedElementToSearchValueConverterManager> CreateFhirTypedElementToSearchValueConverterManagerAsync()
        {
            var types = typeof(ITypedElementToSearchValueConverter)
                .Assembly
                .GetTypes()
                .Where(x => typeof(ITypedElementToSearchValueConverter).IsAssignableFrom(x) && !x.IsAbstract && !x.IsInterface);

            var referenceSearchValueParser = new ReferenceSearchValueParser(new FhirRequestContextAccessor(), new FhirServerInstanceConfiguration());
            var codeSystemResolver = new CodeSystemResolver(ModelInfoProvider.Instance);
            await codeSystemResolver.StartAsync(CancellationToken.None);

            var fhirElementToSearchValueConverters = new List<ITypedElementToSearchValueConverter>();

            foreach (Type type in types.Where(type => type.Name != nameof(FhirTypedElementToSearchValueConverterManager.ExtensionConverter)))
            {
                // Filter out the extension converter because it will be added to the converter dictionary in the converter manager's constructor
                var x = (ITypedElementToSearchValueConverter)Mock.TypeWithArguments(type, referenceSearchValueParser, codeSystemResolver);
                fhirElementToSearchValueConverters.Add(x);
            }

            return new FhirTypedElementToSearchValueConverterManager(fhirElementToSearchValueConverters);
        }

        private static void RegisterProcessExitCleanup()
        {
            if (Interlocked.Exchange(ref _processExitCleanupRegistered, 1) == 1)
            {
                return;
            }

            AppDomain.CurrentDomain.ProcessExit += (_, _) => DisposeSharedContexts();
        }

        private static void DisposeSharedContexts()
        {
            foreach (Lazy<Task<SmartSearchSharedContext>> contextTask in SharedContexts.Values)
            {
                if (!contextTask.IsValueCreated || !contextTask.Value.IsCompletedSuccessfully)
                {
                    continue;
                }

                contextTask.Value.Result.Fixture.DisposeAsync().GetAwaiter().GetResult();
            }
        }

        private sealed class SmartSearchSharedFixtureKey : IEquatable<SmartSearchSharedFixtureKey>
        {
            public SmartSearchSharedFixtureKey(FhirSpecification version, DataStore dataStore)
            {
                Version = version;
                DataStore = dataStore;
            }

            public FhirSpecification Version { get; }

            public DataStore DataStore { get; }

            public bool Equals(SmartSearchSharedFixtureKey other)
            {
                return other != null && Version == other.Version && DataStore == other.DataStore;
            }

            public override bool Equals(object obj)
            {
                return obj is SmartSearchSharedFixtureKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Version, DataStore);
            }
        }
    }
}
