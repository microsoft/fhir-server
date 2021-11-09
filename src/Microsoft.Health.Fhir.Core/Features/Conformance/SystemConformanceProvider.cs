﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Conformance.Models;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Validation;
using Microsoft.Health.Fhir.Core.Messages.CapabilityStatement;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public sealed class SystemConformanceProvider
        : ConformanceProviderBase, IConfiguredConformanceProvider, INotificationHandler<RebuildCapabilityStatement>, IDisposable
    {
        private readonly IModelInfoProvider _modelInfoProvider;
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager;
        private readonly Func<IScoped<IEnumerable<IProvideCapability>>> _capabilityProviders;
        private ResourceElement _listedCapabilityStatement;
        private ResourceElement _metadata;
        private ICapabilityStatementBuilder _builder;
        private SemaphoreSlim _defaultCapabilitySemaphore = new SemaphoreSlim(1, 1);
        private SemaphoreSlim _metadataSemaphore = new SemaphoreSlim(1, 1);
        private readonly List<Action<ListedCapabilityStatement>> _configurationUpdates = new List<Action<ListedCapabilityStatement>>();
        private readonly IOptions<CoreFeatureConfiguration> _configuration;
        private readonly IKnowSupportedProfiles _supportedProfiles;

        public SystemConformanceProvider(
            IModelInfoProvider modelInfoProvider,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
            Func<IScoped<IEnumerable<IProvideCapability>>> capabilityProviders,
            IOptions<CoreFeatureConfiguration> configuration,
            IKnowSupportedProfiles supportedProfiles)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(capabilityProviders, nameof(capabilityProviders));
            EnsureArg.IsNotNull(configuration, nameof(configuration));
            EnsureArg.IsNotNull(supportedProfiles, nameof(supportedProfiles));

            _modelInfoProvider = modelInfoProvider;
            _searchParameterDefinitionManager = searchParameterDefinitionManagerResolver();
            _capabilityProviders = capabilityProviders;
            _configuration = configuration;
            _supportedProfiles = supportedProfiles;
        }

        public override async Task<ResourceElement> GetCapabilityStatementOnStartup(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_listedCapabilityStatement == null)
            {
                await _defaultCapabilitySemaphore.WaitAsync(cancellationToken);

                try
                {
                    if (_listedCapabilityStatement == null)
                    {
                        _builder = CapabilityStatementBuilder.Create(_modelInfoProvider, _searchParameterDefinitionManager, _configuration, _supportedProfiles);

                        using (IScoped<IEnumerable<IProvideCapability>> providerFactory = _capabilityProviders())
                        {
                            foreach (IProvideCapability provider in providerFactory.Value)
                            {
                                provider.Build(_builder);
                            }
                        }

                        foreach (Action<ListedCapabilityStatement> postConfiguration in _configurationUpdates)
                        {
                            _builder.Apply(statement => postConfiguration(statement));
                        }

                        _listedCapabilityStatement = _builder.Build().ToResourceElement();
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

        public void ConfigureOptionalCapabilities(Action<ListedCapabilityStatement> builder)
        {
            EnsureArg.IsNotNull(builder, nameof(builder));

            if (_listedCapabilityStatement != null)
            {
                throw new InvalidOperationException("Post capability configuration changes can no longer be applied.");
            }

            _configurationUpdates.Add(builder);
        }

        public void Dispose()
        {
            _defaultCapabilitySemaphore?.Dispose();
            _defaultCapabilitySemaphore = null;
            _metadataSemaphore?.Dispose();
            _metadataSemaphore = null;
        }

        public async Task Handle(RebuildCapabilityStatement notification, CancellationToken cancellationToken)
        {
            await _metadataSemaphore.WaitAsync(cancellationToken);
            try
            {
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
                            _builder.SyncProfiles(true);
                            break;
                    }
                }

                _metadata = null;
            }
            finally
            {
                _metadataSemaphore.Release();
            }
        }

        public override async Task<ResourceElement> GetMetadata(CancellationToken cancellationToken = default)
        {
            await _metadataSemaphore.WaitAsync(cancellationToken);
            try
            {
                if (_metadata == null)
                {
                    _ = await GetCapabilityStatementOnStartup(cancellationToken);

                    _metadata = _builder.Build().ToResourceElement();
                }

                return _metadata;
            }
            finally
            {
                _metadataSemaphore.Release();
            }
        }
    }
}
