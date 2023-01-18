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
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportAnonymizerFactory : IAnonymizerFactory
    {
        private IArtifactProvider _artifactProvider;
        private ILogger<ExportJobTask> _logger;

        public ExportAnonymizerFactory(IArtifactProvider artifactProvider, ILogger<ExportJobTask> logger)
        {
            EnsureArg.IsNotNull(artifactProvider, nameof(artifactProvider));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _artifactProvider = artifactProvider;
            _logger = logger;
        }

        public async Task<IAnonymizer> CreateAnonymizerAsync(ExportJobRecord exportJobRecord, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(exportJobRecord.AnonymizationConfigurationLocation, nameof(exportJobRecord.AnonymizationConfigurationLocation));

            using (Stream stream = new MemoryStream())
            {
                try
                {
                    await _artifactProvider.FetchAsync(exportJobRecord, stream, cancellationToken);
                    stream.Position = 0;
                }
                catch (FileNotFoundException ex)
                {
                    throw new AnonymizationConfigurationNotFoundException(ex.Message, ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Failed to fetch Anonymization configuration file: {ConfigLocation}", exportJobRecord.AnonymizationConfigurationLocation);
                    throw new AnonymizationConfigurationFetchException(ex.Message, ex);
                }

                using (StreamReader reader = new StreamReader(stream))
                {
#pragma warning disable CA2016
                    string configurationContent = await reader.ReadToEndAsync();
#pragma warning restore CA2016
                    try
                    {
                        var engine = new AnonymizerEngine(AnonymizerConfigurationManager.CreateFromSettingsInJson(configurationContent));
                        return new ExportAnonymizer(engine);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError("Failed to parse configuration file: {Message}", ex.Message);
                        throw new FailedToParseAnonymizationConfigurationException(ex.Message, ex);
                    }
                }
            }
        }
    }
}
