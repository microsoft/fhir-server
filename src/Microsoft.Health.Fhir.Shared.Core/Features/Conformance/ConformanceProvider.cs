// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Routing;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public sealed class ConformanceProvider : ConformanceProviderBase, System.IDisposable
    {
        private readonly SystemConformanceProvider _systemConformance;
        private readonly IConfiguredConformanceProvider _configuredConformanceProvider;
        private readonly IUrlResolver _urlResolver;
        private readonly ConformanceConfiguration _conformanceConfiguration;

        private SemaphoreSlim _sem = new SemaphoreSlim(1, 1);
        private CapabilityStatement _capabilityStatement;

        public ConformanceProvider(
            SystemConformanceProvider systemConformance,
            IConfiguredConformanceProvider configuredConformanceProvider,
            IUrlResolver urlResolver,
            IOptions<ConformanceConfiguration> conformanceConfiguration)
        {
            EnsureArg.IsNotNull(systemConformance, nameof(systemConformance));
            EnsureArg.IsNotNull(configuredConformanceProvider, nameof(configuredConformanceProvider));
            EnsureArg.IsNotNull(urlResolver, nameof(urlResolver));
            EnsureArg.IsNotNull(conformanceConfiguration, nameof(conformanceConfiguration));
            EnsureArg.IsNotNull(conformanceConfiguration.Value, nameof(conformanceConfiguration));

            _systemConformance = systemConformance;
            _configuredConformanceProvider = configuredConformanceProvider;
            _urlResolver = urlResolver;
            _conformanceConfiguration = conformanceConfiguration.Value;
        }

        public override async Task<ITypedElement> GetCapabilityStatementAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_capabilityStatement == null)
            {
                await _sem.WaitAsync(cancellationToken);

                try
                {
                    if (_capabilityStatement == null)
                    {
                        var generated =
                            await _systemConformance.GetSystemListedCapabilitiesStatementAsync(cancellationToken);
                        var configured =
                            await _configuredConformanceProvider.GetCapabilityStatementAsync(cancellationToken);

                        _capabilityStatement =
                            generated.Intersect(configured.ToPoco() as CapabilityStatement, _conformanceConfiguration.UseStrictConformance);

                        _capabilityStatement.UrlElement = new FhirUri(_urlResolver.ResolveMetadataUrl(false));
                    }
                }
                finally
                {
                    _sem.Release();
                }
            }

            return _capabilityStatement.ToTypedElement();
        }

        public void Dispose()
        {
            _sem?.Dispose();
            _sem = null;
        }
    }
}
