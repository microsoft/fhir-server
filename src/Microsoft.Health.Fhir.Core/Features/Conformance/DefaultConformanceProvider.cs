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
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;

namespace Microsoft.Health.Fhir.Core.Features.Conformance
{
    public class DefaultConformanceProvider : ConformanceProviderBase, IConfiguredConformanceProvider
    {
        private readonly FhirJsonParser _parser;
        private readonly SearchConfiguration _searchConfiguration;

        private CapabilityStatement _capabilityStatement;
        private readonly List<Action<CapabilityStatement>> _builderActions = new List<Action<CapabilityStatement>>();

        public DefaultConformanceProvider(FhirJsonParser parser, IOptions<SearchConfiguration> searchConfiguration)
        {
            EnsureArg.IsNotNull(parser, nameof(parser));
            EnsureArg.IsNotNull(searchConfiguration?.Value, nameof(searchConfiguration));

            _parser = parser;
            _searchConfiguration = searchConfiguration.Value;
        }

        public override async Task<CapabilityStatement> GetCapabilityStatementAsync(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (_capabilityStatement == null)
            {
                string manifestName = _searchConfiguration.UseLegacySearch ?
                    $"{GetType().Namespace}.LegacyDefaultCapabilities.json" :
                    $"{GetType().Namespace}.DefaultCapabilities.json";

                using (Stream resourceStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(manifestName))
                using (var reader = new StreamReader(resourceStream))
                {
                    _capabilityStatement = _parser.Parse<CapabilityStatement>(await reader.ReadToEndAsync());
                }

                _builderActions.ForEach(action => action(_capabilityStatement));
            }

            return _capabilityStatement;
        }

        public void ConfigureOptionalCapabilities(Action<CapabilityStatement> builder)
        {
            if (_capabilityStatement != null)
            {
                builder(_capabilityStatement);
            }
            else
            {
                _builderActions.Add(builder);
            }
        }
    }
}
