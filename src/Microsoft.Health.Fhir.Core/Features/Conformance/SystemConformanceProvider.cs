// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Context;
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
        private readonly RequestContextAccessor<IFhirRequestContext> _contextAccessor;
        private readonly Func<IScoped<IEnumerable<IProvideCapability>>> _capabilityProviders;
        private readonly List<Action<ListedCapabilityStatement>> _configurationUpdates = new List<Action<ListedCapabilityStatement>>();
        private readonly IOptions<CoreFeatureConfiguration> _configuration;
        private readonly ISupportedProfilesStore _supportedProfiles;
        private readonly ILogger _logger;
        private readonly SearchParameterStatusManager _searchParameterStatusManager;

        private ResourceElement _listedCapabilityStatement;
        private ResourceElement _backgroundJobCapabilityStatement;
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
            RequestContextAccessor<IFhirRequestContext> contextAccessor,
            SearchParameterStatusManager searchParameterStatusManager)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(capabilityProviders, nameof(capabilityProviders));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(supportedProfiles, nameof(supportedProfiles));
            EnsureArg.IsNotNull(logger, nameof(logger));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(contextAccessor, nameof(contextAccessor));
            EnsureArg.IsNotNull(searchParameterStatusManager, nameof(searchParameterStatusManager));

            _modelInfoProvider = modelInfoProvider;
            _searchParameterDefinitionManager = searchParameterDefinitionManagerResolver();
            _capabilityProviders = capabilityProviders;
            _configuration = configuration;
            _supportedProfiles = supportedProfiles;
            _logger = logger;
            _disposed = false;
            _urlResolver = urlResolver;
            _contextAccessor = contextAccessor;
            _searchParameterStatusManager = searchParameterStatusManager;

            LogVersioningPolicyConfiguration();
        }

        public override async Task<ResourceElement> GetCapabilityStatementOnStartup(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_disposed)
            {
                _logger.LogError("SystemConformanceProvider is already disposed.");
            }

            if (_listedCapabilityStatement == null)
            {
                var cacheResult = true;
                if (IsBackgroundJob())
                {
                    if (_backgroundJobCapabilityStatement != null)
                    {
                        return _backgroundJobCapabilityStatement;
                    }
                }

                _logger.LogInformation("SystemConformanceProvider: Initializing new Capability Statement.");

                await _defaultCapabilitySemaphore.WaitAsync(cancellationToken);

                try
                {
                    if (_listedCapabilityStatement == null)
                    {
                        // Background jobs don't have functioning _urlResolvers, so we have to use a stand in. This value won't be cached for general use so that customers won't see it.
                        Uri metadataUrl;
                        if (IsBackgroundJob())
                        {
                            metadataUrl = new Uri("/metadata", UriKind.Relative);
                            cacheResult = false;
                        }
                        else
                        {
                            metadataUrl = _urlResolver.ResolveMetadataUrl(false);
                        }

                        _builder = CapabilityStatementBuilder.Create(_modelInfoProvider, _searchParameterDefinitionManager, _configuration, _supportedProfiles, metadataUrl, _searchParameterStatusManager);

                        using (IScoped<IEnumerable<IProvideCapability>> providerFactory = _capabilityProviders())
                        {
                            var providers = providerFactory.Value.ToList();
                            foreach (IProvideCapability provider in providers)
                            {
                                Stopwatch watch = Stopwatch.StartNew();

                                try
                                {
                                    _logger.LogInformation("SystemConformanceProvider: Building Capability Statement. Provider '{ProviderName}'.", provider.ToString());
                                    await provider.BuildAsync(_builder, cancellationToken);
                                }
                                catch (Exception e)
                                {
                                    if (IsBackgroundJob())
                                    {
                                        _logger.LogWarning(e, "Failed to make Capability Statement during background job.");

                                        // Something has gone wrong, so we shouldn't save the result for general use.
                                        // Since this is a background job it doesn't need a full capability statement though. A partial one will still be accurate for its usage.
                                        cacheResult = false;
                                    }
                                    else
                                    {
                                        _logger.LogError(e, "SystemConformanceProvider: Failed running '{ProviderName}' when building a new CapabilityStatement.", provider.ToString());
                                        throw;
                                    }
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

                        if (cacheResult)
                        {
                            _listedCapabilityStatement = _builder.Build().ToResourceElement();
                            _backgroundJobCapabilityStatement = _listedCapabilityStatement;

                            _rebuilder = Task.Run(BackgroudLoop, CancellationToken.None);
                        }
                        else
                        {
                            _backgroundJobCapabilityStatement = _builder.Build().ToResourceElement();
                            return _backgroundJobCapabilityStatement;
                        }
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

        public bool IsBackgroundJob()
        {
            return _contextAccessor.RequestContext.IsBackgroundTask;
        }

        public async Task BackgroudLoop()
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                Stopwatch sw = Stopwatch.StartNew();
                for (int i = 0; i < _rebuildDelay; i++)
                {
                    if (_builder != null && _builder.IsSyncProfilesRequested())
                    {
                        _logger.LogInformation("SystemConformanceProvider sync is requested.");
                        break;
                    }

                    await Task.Delay(TimeSpan.FromMinutes(1), _cancellationTokenSource.Token);

                    if (_disposed)
                    {
                        _logger.LogError("SystemConformanceProvider is already disposed. SystemConformanceProvider's BackgroudLoop is completed.");
                        return;
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

                try
                {
                    if (_builder != null)
                    {
                        // Update search params.
                        _builder.SyncSearchParameters();

                        // Update supported profiles.
                        await _builder.SyncProfilesAsync(_cancellationTokenSource.Token);

                        // Update other fields populated by providers.
                        await UpdateMetadataAsync(cancellationToken);
                    }
                }
                catch (Exception e)
                {
                    // Do not let exceptions escape the background loop.
                    _logger.LogError(e, "SystemConformanceProvider: Unexpected error during background capability statement rebuild.");
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

            if (_disposed)
            {
                _logger.LogInformation("SystemConformanceProvider: Instance is already disposed.");
                return;
            }

            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                await _cancellationTokenSource.CancelAsync();
            }

            if (_rebuilder != null)
            {
                try
                {
                    await _rebuilder;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("DisposeAsync detected OperationCanceledException while awaiting background loop. Exiting cleanly.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error while disposing SystemConformanceProvider.");
                }
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
                        _builder.SyncSearchParameters();
                        break;

                    case RebuildPart.Profiles:
                        // Update supported profiles;
                        await _builder.SyncProfilesAsync(cancellationToken, true);
                        break;
                }
            }

            await SetMetadataAsync(null);
        }

        public override async Task<ResourceElement> GetMetadata(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                _logger.LogError("SystemConformanceProvider is already disposed.");
            }

            // There is a chance that the BackgroundLoop handler sets _metadata to null between when it is checked and returned, so the value is stored in a local variable.
            ResourceElement metadata = await UpdateMetadataAsync(cancellationToken);
            if (metadata == null)
            {
                metadata = await GetCapabilityStatementOnStartup(cancellationToken);
            }

            // The semaphore is only used for building the metadata because claiming it before the GetCapabilityStatementOnStartup was leading to deadlocks where the creation
            // of metadata could trigger a rebuild. The rebuild handler had to wait on the metadata semaphore, which wouldn't be released until the metadata could be built.
            // But the metadata builder was waiting on the rebuild handler.
            return await SetMetadataAsync(metadata);
        }

        private void LogVersioningPolicyConfiguration()
        {
            if (!string.IsNullOrEmpty(_configuration.Value.Versioning.Default) || _configuration.Value.Versioning.ResourceTypeOverrides.Any())
            {
                StringBuilder versioning = new StringBuilder();
                versioning.AppendLine($"Default version is: '{_configuration.Value.Versioning.Default ?? "(default)"}'.");

                foreach (var resourceTypeVersioning in _configuration.Value.Versioning.ResourceTypeOverrides)
                {
                    versioning.AppendLine($"'{resourceTypeVersioning.Key}' version overridden to: '{resourceTypeVersioning.Value}'.");
                }

                _logger.LogInformation(versioning.ToString());
            }
        }

        private async Task<ResourceElement> UpdateMetadataAsync(CancellationToken cancellationToken)
        {
            // Note: the method will update non-static sections of the metadata only; thus, it does nothing
            //       when the full metadata is not yet built.
            if (_builder != null && _metadata != null)
            {
                await (_metadataSemaphore?.WaitAsync(_cancellationTokenSource.Token) ?? Task.CompletedTask);
                _logger.LogInformation("SystemConformanceProvider: Updating the metadata.");

                try
                {
                    if (_builder != null && _metadata != null)
                    {
                        using (IScoped<IEnumerable<IProvideCapability>> providerFactory = _capabilityProviders())
                        {
                            var providers = providerFactory.Value?
                                .Where(x => x is IVolatileProvideCapability)?
                                .Select(x => (IVolatileProvideCapability)x)
                                .ToList()
                                ?? new List<IVolatileProvideCapability>();
                            foreach (var provider in providers)
                            {
                                Stopwatch watch = Stopwatch.StartNew();

                                try
                                {
                                    _logger.LogInformation("SystemConformanceProvider: Updating the metadata with '{ProviderName}'.", provider.ToString());
                                    await provider.UpdateAsync(_builder, cancellationToken);
                                }
                                catch (Exception e)
                                {
                                    _logger.LogWarning(e, "Failed to update Capability Statement.");
                                    throw;
                                }
                                finally
                                {
                                    _logger.LogInformation("SystemConformanceProvider: Updating the metadata with '{ProviderName}' completed. Elapsed time {ElapsedTime}.", provider.ToString(), watch.Elapsed);
                                }
                            }
                        }

                        _metadata = _builder.Build().ToResourceElement();
                    }
                }
                finally
                {
                    _metadataSemaphore?.Release();
                }
            }

            return _metadata;
        }

        private async Task<ResourceElement> SetMetadataAsync(ResourceElement metadata)
        {
            await (_metadataSemaphore?.WaitAsync(_cancellationTokenSource.Token) ?? Task.CompletedTask);
            try
            {
                _metadata = metadata;
                return _metadata;
            }
            finally
            {
                _metadataSemaphore?.Release();
            }
        }
    }
}
