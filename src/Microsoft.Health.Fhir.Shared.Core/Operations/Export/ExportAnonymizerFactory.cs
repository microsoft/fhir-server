// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Anonymizer.Core;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportAnonymizerFactory : IAnonymizerFactory
    {
        private ArtifactProviderResolver _artifactProviderResolver;
        private ILogger<ExportJobTask> _logger;

        public ExportAnonymizerFactory(ArtifactProviderResolver artifactProviderResolver, ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(artifactProviderResolver, nameof(artifactProviderResolver));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _artifactProviderResolver = artifactProviderResolver;
            _logger = logger;
        }

        public async Task<IAnonymizer> CreateAnonymizerAsync(string configurationLocation, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(configurationLocation, nameof(configurationLocation));

            using (Stream stream = new MemoryStream())
            {
                try
                {
                    string[] configLocation = configurationLocation.Split(':');
                    string type = configLocation[0];
                    await _artifactProviderResolver(type).FetchAsync(configLocation[1], stream, cancellationToken);
                    stream.Position = 0;
                }
                catch (FileNotFoundException ex)
                {
                    throw new AnonymizationConfigurationNotFoundException(ex.Message, ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to fetch Anonymization configuration file: {configurationLocation}");
                    throw new AnonymizationConfigurationFetchException(ex.Message, ex);
                }

                using (StreamReader reader = new StreamReader(stream))
                {
                    string configurationContent = await reader.ReadToEndAsync();
                    try
                    {
                        var engine = new AnonymizerEngine(AnonymizerConfigurationManager.CreateFromSettingsInJson(configurationContent));
                        return new ExportAnonymizer(engine);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Failed to parse configuration file: {ex.Message}");
                        throw new FailedToParseAnonymizationConfigurationException(ex.Message, ex);
                    }
                }
            }
        }
    }
}
