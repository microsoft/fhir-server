// -------------------------------------------------------------------------------------------------
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
using Microsoft.Health.Extensions.DependencyInjection;
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
        private SemaphoreSlim _sem = new SemaphoreSlim(1, 1);
        private readonly List<Action<ListedCapabilityStatement>> _configurationUpdates = new List<Action<ListedCapabilityStatement>>();
        private readonly IKnowSupportedProfiles _supportedProfiles;

        public SystemConformanceProvider(
            IModelInfoProvider modelInfoProvider,
            ISearchParameterDefinitionManager.SearchableSearchParameterDefinitionManagerResolver searchParameterDefinitionManagerResolver,
            Func<IScoped<IEnumerable<IProvideCapability>>> capabilityProviders,
            IKnowSupportedProfiles supportedProfiles)
        {
            EnsureArg.IsNotNull(modelInfoProvider, nameof(modelInfoProvider));
            EnsureArg.IsNotNull(searchParameterDefinitionManagerResolver, nameof(searchParameterDefinitionManagerResolver));
            EnsureArg.IsNotNull(capabilityProviders, nameof(capabilityProviders));
            EnsureArg.IsNotNull(supportedProfiles, nameof(supportedProfiles));

            _modelInfoProvider = modelInfoProvider;
            _searchParameterDefinitionManager = searchParameterDefinitionManagerResolver();
            _capabilityProviders = capabilityProviders;
            _supportedProfiles = supportedProfiles;
        }

        public override async Task<ResourceElement> GetCapabilityStatementAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_listedCapabilityStatement == null)
            {
                await _sem.WaitAsync(cancellationToken);

                try
                {
                    if (_listedCapabilityStatement == null)
                    {
                        ICapabilityStatementBuilder builder = CapabilityStatementBuilder.Create(_modelInfoProvider, _searchParameterDefinitionManager, _supportedProfiles)
                            .Update(x =>
                            {
                                x.FhirVersion = _modelInfoProvider.SupportedVersion.ToString();
                                x.Software = new SoftwareComponent
                                {
                                    Name = Resources.ServerName,
                                    Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                                };
                            });

                        using (IScoped<IEnumerable<IProvideCapability>> providerFactory = _capabilityProviders())
                        {
                            foreach (IProvideCapability provider in providerFactory.Value)
                            {
                                provider.Build(builder);
                            }
                        }

                        foreach (Action<ListedCapabilityStatement> postConfiguration in _configurationUpdates)
                        {
                            builder.Update(statement => postConfiguration(statement));
                        }

                        _listedCapabilityStatement = builder.Build().ToResourceElement();
                    }
                }
                finally
                {
                    _configurationUpdates.Clear();
                    _sem.Release();
                }
            }

            return _listedCapabilityStatement;
        }

        public void ConfigureOptionalCapabilities(Action<ListedCapabilityStatement> builderAction)
        {
            EnsureArg.IsNotNull(builderAction, nameof(builderAction));

            if (_listedCapabilityStatement != null)
            {
                throw new InvalidOperationException("Post capability configuration changes can no longer be applied.");
            }

            _configurationUpdates.Add(builderAction);
        }

        public void Dispose()
        {
            _sem?.Dispose();
            _sem = null;
        }

        public Task Handle(RebuildCapabilityStatement notification, CancellationToken cancellationToken)
        {
            _listedCapabilityStatement = null;

            return Task.CompletedTask;
        }
    }
}
