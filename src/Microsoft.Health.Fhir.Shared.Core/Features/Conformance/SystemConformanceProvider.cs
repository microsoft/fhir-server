// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public sealed class SystemConformanceProvider : ISystemConformanceProvider, IDisposable
    {
        private readonly Func<IScoped<IEnumerable<IProvideCapability>>> _capabilityProviders;
        private ListedCapabilityStatement _listedCapabilityStatement;
        private SemaphoreSlim _sem = new SemaphoreSlim(1, 1);

        public SystemConformanceProvider(Func<IScoped<IEnumerable<IProvideCapability>>> capabilityProviders)
        {
            EnsureArg.IsNotNull(capabilityProviders, nameof(capabilityProviders));

            _capabilityProviders = capabilityProviders;
        }

        public async Task<ListedCapabilityStatement> GetSystemListedCapabilitiesStatementAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_listedCapabilityStatement == null)
            {
                await _sem.WaitAsync(cancellationToken);

                try
                {
                    if (_listedCapabilityStatement == null)
                    {
                        ListedCapabilityStatement buildCapabilityStatement;

                        using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{GetType().Namespace}.BaseCapabilities.json"))
                        using (var reader = new StreamReader(resourceStream))
                        {
                            buildCapabilityStatement = JsonConvert.DeserializeObject<ListedCapabilityStatement>(await reader.ReadToEndAsync());
                            buildCapabilityStatement.Software = new Hl7.Fhir.Model.CapabilityStatement.SoftwareComponent
                            {
                                Name = Core.Resources.ServerName,
                                Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(),
                            };
                        }

                        using (var providerFactory = _capabilityProviders())
                        {
                            foreach (IProvideCapability provider in providerFactory.Value)
                            {
                                provider.Build(buildCapabilityStatement);
                            }
                        }

                        _listedCapabilityStatement = buildCapabilityStatement;
                    }
                }
                finally
                {
                    _sem.Release();
                }
            }

            return _listedCapabilityStatement;
        }

        public void Dispose()
        {
            _sem?.Dispose();
            _sem = null;
        }
    }
}
