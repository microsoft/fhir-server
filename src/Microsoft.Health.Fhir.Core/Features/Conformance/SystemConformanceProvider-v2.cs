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
    public class SystemConformanceProvider_v2
        : ConformanceProviderBase, IConfiguredConformanceProvider, INotificationHandler<RebuildCapabilityStatement>, IAsyncDisposable
    {

#pragma warning disable CA2213 // Disposable fields should be disposed // SystemConformanceProvider is a Singleton class.
        private readonly SemaphoreSlim _defaultCapabilitySemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _metadataSemaphore = new SemaphoreSlim(1, 1);
#pragma warning restore CA2213 // Disposable fields should be disposed // SystemConformanceProvider is a Singleton class.

        private readonly TimeSpan _cacheRefreshInterval;
        private readonly TimeSpan _cacheRebuildInterval;
        private readonly TimeSpan _backgroundLoopLoggingInterval = TimeSpan.FromMinutes(10);
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
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

        public SystemConformanceProvider_v2(
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

            _cacheRebuildInterval = TimeSpan.FromSeconds(_configuration.Value.SystemConformanceProviderRebuildIntervalSeconds);
            _cacheRefreshInterval = TimeSpan.FromSeconds(_configuration.Value.SystemConformanceProviderRefreshIntervalSeconds);

            LogVersioningPolicyConfiguration();
        }

        public void ConfigureOptionalCapabilities(Action<ListedCapabilityStatement> builder)
        {
        }

        public async Task<ResourceElement> GetCapabilityStatementOnStartup(CancellationToken cancellationToken = default(CancellationToken))
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            if (_listedCapabilityStatement != null)
            {
                throw new InvalidOperationException("Post capability configuration changes can no longer be applied.");
            }

            _configurationUpdates.Add(builder);
        }

        public override Task<ResourceElement> GetMetadata(CancellationToken cancellationToken = default)
        {
            return new Task<ResourceElement>(() => _metadata);
        }

        public async Task BackgroundLoop()
        {

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
    }
}
