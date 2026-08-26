// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Medino;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class SystemConformanceProviderTests
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly Func<IScoped<IEnumerable<IProvideCapability>>> _capabilityProviders;
        private readonly IOptions<CoreFeatureConfiguration> _configuration;
        private readonly ISupportedProfilesStore _supportedProfiles;
        private readonly ILogger<SystemConformanceProvider> _logger;
        private readonly IUrlResolver _urlResolver;
        private readonly CapturingRequestContextAccessor _contextAccessor;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;

        public SystemConformanceProviderTests()
        {
            _modelInfoProvider = Substitute.For<IModelInfoProvider>();
            _modelInfoProvider.Version.Returns(FhirSpecification.R4);
            _modelInfoProvider.GetResourceTypeNames().Returns(new[] { "Patient", "Observation" });

            _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();

            var scopedProviders = Substitute.For<IScoped<IEnumerable<IProvideCapability>>>();
            scopedProviders.Value.Returns(new List<IProvideCapability>());
            _capabilityProviders = () => scopedProviders;

            _configuration = Options.Create(new CoreFeatureConfiguration
            {
                SystemConformanceProviderRebuildIntervalSeconds = 1,
                SystemConformanceProviderRefreshIntervalSeconds = 1,
            });

            _supportedProfiles = Substitute.For<ISupportedProfilesStore>();
            _logger = Substitute.For<ILogger<SystemConformanceProvider>>();

            _urlResolver = Substitute.For<IUrlResolver>();
            _urlResolver.ResolveMetadataUrl(Arg.Any<bool>()).Returns(new Uri("https://localhost/metadata"));

            _contextAccessor = new CapturingRequestContextAccessor();

            _searchParameterStatusManager = Substitute.For<SearchParameterStatusManager>(
                Substitute.For<ISearchParameterStatusDataStore>(),
                _searchParameterDefinitionManager,
                Substitute.For<ISearchParameterSupportResolver>(),
                Substitute.For<IMediator>(),
                Substitute.For<ILogger<SearchParameterStatusManager>>());
        }

        [Fact]
        public async Task GivenStaleHttpContextWithReadOnlyHeaders_WhenBackgroudLoopRuns_ThenResponseHeadersAreWritable()
        {
            // Arrange - simulate read-only response headers (like Kestrel after response starts)
            _contextAccessor.RequestContext = CreateStaledRequestContext();

            // Verify they throw on write (this is the condition that causes the bug)
            Assert.Throws<InvalidOperationException>(() =>
                _contextAccessor.RequestContext.ResponseHeaders["x-ms-request-charge"] = "1.0");

            var provider = CreateProvider();

            // Act - BackgroudLoop sets a fresh context immediately before entering its loop.
            Task loopTask = provider.BackgroudLoop();
            await Task.Delay(100);
            await provider.DisposeAsync();

            try
            {
                await loopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation token triggers during Task.Delay
            }

            // Assert - the context set by BackgroudLoop has writable headers
            IFhirRequestContext contextSetByLoop = _contextAccessor.LastSetContext;
            Assert.NotNull(contextSetByLoop);
            var exception = Record.Exception(() =>
                contextSetByLoop.ResponseHeaders["x-ms-request-charge"] = "42.0");
            Assert.Null(exception);
        }

        [Fact]
        public async Task GivenStaleHttpContext_WhenBackgroudLoopRuns_ThenContextIsMarkedAsBackgroundTask()
        {
            // Arrange
            _contextAccessor.RequestContext = CreateStaledRequestContext();

            var provider = CreateProvider();

            // Act
            Task loopTask = provider.BackgroudLoop();
            await Task.Delay(100);
            await provider.DisposeAsync();

            try
            {
                await loopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected if cancellation fires during Task.Delay inside the loop
            }

            // Assert
            Assert.True(_contextAccessor.LastSetContext.IsBackgroundTask);
        }

        [Fact]
        public async Task GivenStaleHttpContext_WhenBackgroudLoopRuns_ThenContextIsReplacedWithNewInstance()
        {
            // Arrange
            var staleContext = CreateStaledRequestContext();
            _contextAccessor.RequestContext = staleContext;

            var provider = CreateProvider();

            // Act
            Task loopTask = provider.BackgroudLoop();
            await Task.Delay(100);
            await provider.DisposeAsync();

            try
            {
                await loopTask;
            }
            catch (OperationCanceledException)
            {
                // Expected if cancellation fires during Task.Delay inside the loop
            }

            // Assert - context was replaced, not the stale one
            Assert.NotSame(staleContext, _contextAccessor.LastSetContext);
        }

        private SystemConformanceProvider CreateProvider()
        {
            return new SystemConformanceProvider(
                _modelInfoProvider,
                () => _searchParameterDefinitionManager,
                _capabilityProviders,
                _configuration,
                _supportedProfiles,
                _logger,
                _urlResolver,
                _contextAccessor,
                _searchParameterStatusManager);
        }

        private static FhirRequestContext CreateStaledRequestContext(
            IDictionary<string, StringValues> responseHeaders = null)
        {
            return new FhirRequestContext(
                method: "GET",
                uriString: "https://localhost/metadata",
                baseUriString: "https://localhost/",
                correlationId: Guid.NewGuid().ToString(),
                requestHeaders: new Dictionary<string, StringValues>(),
                responseHeaders: responseHeaders ?? new ReadOnlyHeaderDictionary());
        }

        /// <summary>
        /// A request context accessor that uses a plain field (not AsyncLocal) to capture
        /// the last value set, regardless of which async execution context wrote it.
        /// This allows tests to observe writes made from within BackgroudLoop's context.
        /// </summary>
        private sealed class CapturingRequestContextAccessor : RequestContextAccessor<IFhirRequestContext>
        {
            private volatile IFhirRequestContext _current;

            /// <summary>
            /// Gets the last context that was set, regardless of execution context.
            /// </summary>
            public IFhirRequestContext LastSetContext { get; private set; }

            public override IFhirRequestContext RequestContext
            {
                get => _current;
                set
                {
                    _current = value;
                    LastSetContext = value;
                }
            }
        }

        /// <summary>
        /// Simulates Kestrel's read-only headers after a response has started.
        /// Any write operation throws <see cref="InvalidOperationException"/>.
        /// </summary>
        private sealed class ReadOnlyHeaderDictionary : IDictionary<string, StringValues>
        {
            public ICollection<string> Keys => Array.Empty<string>();

            public ICollection<StringValues> Values => Array.Empty<StringValues>();

            public int Count => 0;

            public bool IsReadOnly => true;

            public StringValues this[string key]
            {
                get => StringValues.Empty;
                set => throw new InvalidOperationException("Headers are read-only, response has already started.");
            }

            public void Add(string key, StringValues value) =>
                throw new InvalidOperationException("Headers are read-only, response has already started.");

            public void Add(KeyValuePair<string, StringValues> item) =>
                throw new InvalidOperationException("Headers are read-only, response has already started.");

            public void Clear() =>
                throw new InvalidOperationException("Headers are read-only, response has already started.");

            public bool Contains(KeyValuePair<string, StringValues> item) => false;

            public bool ContainsKey(string key) => false;

            public void CopyTo(KeyValuePair<string, StringValues>[] array, int arrayIndex)
            {
            }

            public IEnumerator<KeyValuePair<string, StringValues>> GetEnumerator() =>
                ((IEnumerable<KeyValuePair<string, StringValues>>)Array.Empty<KeyValuePair<string, StringValues>>()).GetEnumerator();

            public bool Remove(string key) =>
                throw new InvalidOperationException("Headers are read-only, response has already started.");

            public bool Remove(KeyValuePair<string, StringValues> item) =>
                throw new InvalidOperationException("Headers are read-only, response has already started.");

            public bool TryGetValue(string key, out StringValues value)
            {
                value = StringValues.Empty;
                return false;
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        }
    }
}
