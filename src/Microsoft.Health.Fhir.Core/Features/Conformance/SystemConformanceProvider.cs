// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Routing;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.CapabilityStatement;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public sealed class SystemConformanceProvider
        : ConformanceProviderBase, IConfiguredConformanceProvider, INotificationHandler<RebuildCapabilityStatement>, IAsyncDisposable
    {
#pragma warning disable CA2213 // Disposable fields should be disposed // SystemConformanceProvider is a Singleton class.
        private readonly SemaphoreSlim _defaultCapabilitySemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _metadataSemaphore = new SemaphoreSlim(1, 1);
#pragma warning restore CA2213 // Disposable fields should be disposed // SystemConformanceProvider is a Singleton class.

        private readonly TimeSpan _backgroundLoopLoggingInterval = TimeSpan.FromMinutes(10);
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly int _rebuildDelay = 240; // 4 hours in minutes
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly IUrlResolver _urlResolver;
        private readonly Func<IScoped<IEnumerable<IProvideCapability>>> _capabilityProviders;
        private readonly List<Action<ListedCapabilityStatement>> _configurationUpdates = new List<Action<ListedCapabilityStatement>>();
        private readonly IOptions<CoreFeatureConfiguration> _configuration;
        private readonly ISupportedProfilesStore _supportedProfiles;
        private readonly ILogger _logger;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;

        private ResourceElement _listedCapabilityStatement;
        private ResourceElement _metadata;
        private ICapabilityStatementBuilder _builder;
        private Task _rebuilder;
        private bool _disposed;

        public SystemConformanceProvider(
            IModelInfoProvider modelInfoProvider,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
            Func<IScoped<IEnumerable<IProvideCapability>>> capabilityProviders,
            IOptions<CoreFeatureConfiguration> configuration,
            ISupportedProfilesStore supportedProfiles,
            ILogger<SystemConformanceProvider> logger,
            IUrlResolver urlResolver,
            SearchParameterStatusManager searchParameterStatusManager)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(capabilityProviders, nameof(capabilityProviders));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(supportedProfiles, nameof(supportedProfiles));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));

            _modelInfoProvider = modelInfoProvider;
            _searchParameterDefinitionManager = searchParameterDefinitionManagerResolver();
            _capabilityProviders = capabilityProviders;
            _configuration = configuration;
            _supportedProfiles = supportedProfiles;
            _logger = logger;
            _disposed = false;
            _urlResolver = urlResolver;
            _searchParameterStatusManager = searchParameterStatusManager;
        }

        public override async Task<ResourceElement> GetCapabilityStatementOnStartup(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_disposed)
            {
                _logger.LogError("SystemConformanceProvider is already disposed.");
            }

            if (_listedCapabilityStatement == null)
            {
                _logger.LogInformation("SystemConformanceProvider: Initializing new Capability Statement.");

                await _defaultCapabilitySemaphore.WaitAsync(cancellationToken);

                try
                {
                    if (_listedCapabilityStatement == null)
                    {
                        _builder = CapabilityStatementBuilder.Create(_modelInfoProvider, _searchParameterDefinitionManager, _configuration, _supportedProfiles, _urlResolver, _searchParameterStatusManager);

                        using (IScoped<IEnumerable<IProvideCapability>> providerFactory = _capabilityProviders())
                        {
                            IEnumerable<IProvideCapability> providers = providerFactory.Value;
                            foreach (IProvideCapability provider in providers)
                            {
                                Stopwatch watch = Stopwatch.StartNew();
                                try
                                {
                                    _logger.LogInformation("SystemConformanceProvider: Building Capability Statement. Provider '{ProviderName}'.", provider.ToString());
                                    provider.Build(_builder);
                                }
                                catch (Exception e)
                                {
                                    _logger.LogError(e, "SystemConformanceProvider: Failed running '{ProviderName}' when building a new CapabilityStatement.", provider.ToString());
                                    throw;
                                }
                                finally
                                {
                                    _logger.LogInformation("SystemConformanceProvider: Building Capability Statement. Provider '{ProviderName}' completed. Elapsed time {ElapsedTime}.", provider.ToString(), watch.Elapsed);
                                }
                            }
                        }

                        foreach (Action<ListedCapabilityStatement> postConfiguration in _configurationUpdates)
                        {
                            _builder.Apply(statement => postConfiguration(statement));
                        }

                        _listedCapabilityStatement = _builder.Build().ToResourceElement();

                        _rebuilder = Task.Run(BackgroudLoop, CancellationToken.None);
                    }
                }
                finally
                {
                    _configurationUpdates.Clear();
                    _defaultCapabilitySemaphore.Release();
                }
            }

            return _listedCapabilityStatement;
        }

        public async Task BackgroudLoop()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < _rebuildDelay; i++)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1));

                    if (_disposed)
                    {
                        _logger.LogError("SystemConformanceProvider is already disposed. SystemConformanceProvider's BackgroudLoop is completed.");
                    }

                    if (_cancellationTokenSource.IsCancellationRequested)
                    {
                        _logger.LogInformation("SystemConformanceProvider's BackgroudLoop is canceled.");
                        return;
                    }

                    if (sw.Elapsed >= _backgroundLoopLoggingInterval)
                    {
                        _logger.LogInformation("SystemConformanceProvider's BackgroudLoop is active and running.");
                        sw.Restart();
                    }
                }

                if (_builder != null)
                {
                    // Update search params;
                    _builder.SyncSearchParametersAsync();

                    // Update supported profiles;
                    _builder.SyncProfiles();
                }

                await (_metadataSemaphore?.WaitAsync(CancellationToken.None) ?? Task.CompletedTask);
                try
                {
                    _metadata = null;
                }
                finally
                {
                    _metadataSemaphore?.Release();
                }
            }
        }

        public void ConfigureOptionalCapabilities(Action<ListedCapabilityStatement> builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            if (_listedCapabilityStatement != null)
            {
                throw new InvalidOperationException("Post capability configuration changes can no longer be applied.");
            }

            _configurationUpdates.Add(builder);
        }

        public async ValueTask DisposeAsync()
        {
            _logger.LogInformation("SystemConformanceProvider: DisposeAsync invoked.");

            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
            }

            if (_rebuilder != null)
            {
                await _rebuilder;
            }

            _cancellationTokenSource.Dispose();
            _rebuilder = null;

            // Stopped disposing '_defaultCapabilitySemaphore' to validate a null reference in prod.
            // Stopped disposing '_metadataSemaphore' to validate a null reference in prod.

            _disposed = true;

            _logger.LogInformation("SystemConformanceProvider: DisposeAsync completed.");
        }

        public async Task Handle(RebuildCapabilityStatement notification, CancellationToken cancellationToken)
        {
            if (_disposed)
            {
                _logger.LogError("SystemConformanceProvider is already disposed.");
            }

            EnsureArg.IsNotNull(notification, nameof(notification));

            _logger.LogInformation("SystemConformanceProvider: Rebuild capability statement notification handled");

            if (_builder != null)
            {
                switch (notification.Part)
                {
                    case RebuildPart.SearchParameter:
                        // Update search params;
                        _builder.SyncSearchParametersAsync();
                        break;

                    case RebuildPart.Profiles:
                        // Update supported profiles;
                        _builder.SyncProfiles(true);
                        break;
                }
            }

            await (_metadataSemaphore?.WaitAsync(cancellationToken) ?? Task.CompletedTask);
            try
            {
                _metadata = null;
            }
            finally
            {
                _metadataSemaphore?.Release();
            }
        }

        public override async Task<ResourceElement> GetMetadata(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                _logger.LogError("SystemConformanceProvider is already disposed.");
            }

            // There is a chance that the BackgroundLoop handler sets _metadata to null between when it is checked and returned, so the value is stored in a local variable.
            ResourceElement metadata;
            if ((metadata = _metadata) != null)
            {
                return metadata;
            }

            _ = await GetCapabilityStatementOnStartup(cancellationToken);

            // The semaphore is only used for building the metadata because claiming it before the GetCapabilityStatementOnStartup was leading to deadlocks where the creation
            // of metadata could trigger a rebuild. The rebuild handler had to wait on the metadata semaphore, which wouldn't be released until the metadata could be built.
            // But the metadata builder was waiting on the rebuild handler.
            await (_metadataSemaphore?.WaitAsync(cancellationToken) ?? Task.CompletedTask);
            try
            {
                _metadata = _builder.Build().ToResourceElement();
                return _metadata;
            }
            finally
            {
                _metadataSemaphore?.Release();
            }
        }
    }
}
