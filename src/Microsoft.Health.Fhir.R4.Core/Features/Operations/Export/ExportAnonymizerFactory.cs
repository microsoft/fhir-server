// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Fhir.Anonymizer.Core;
using Microsoft.Extensions.Logging;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportAnonymizerFactory : IAnonymizerFactory
    {
        private IArtifactProvider _artifactProvider;
        private ILogger<ExportJobTask> _logger;

        public ExportAnonymizerFactory(IArtifactProvider artifactProvider, ILogger<ExportJobTask> logger)
        {
            _artifactProvider = artifactProvider;
            _logger = logger;
        }

        public async Task<IAnonymizer> CreateAnonymizerAsync(string configurationLocation, string fileHash, CancellationToken cancellationToken)
        {
            using (Stream stream = new MemoryStream())
            {
                try
                {
                    await _artifactProvider.FetchArtifactAsync(configurationLocation, stream, cancellationToken);
                    stream.Position = 0;
                }
                catch (FileNotFoundException ex)
                {
                    _logger.LogError($"Anonymization configuration file not found: {configurationLocation}");
                    throw new AnonymizationConfigurationNotFoundException(ex.Message, ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Failed to fetch Anonymization configuration file: {configurationLocation}");
                    throw new AnonymizationConfigurationFetchException(ex.Message, ex);
                }

                if (!string.IsNullOrEmpty(fileHash))
                {
                    using (var md5 = SHA256.Create())
                    {
                        var actualHashValue = BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", string.Empty, StringComparison.InvariantCultureIgnoreCase);
                        if (!string.Equals(actualHashValue, fileHash, StringComparison.InvariantCultureIgnoreCase))
                        {
                            _logger.LogError($"Anonymization configuration file hash value not match: expected {fileHash}");
                            throw new AnonymizationConfigurationHashValueNotMatchException($"Configuration file hash value not match. Please double check file hash of sha256.");
                        }

                        stream.Position = 0;
                    }
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
                        _logger.LogError($"Loca anonymization configuration file failed: {ex.Message}");
                        throw new FailedToParseAnonymizationConfigurationException(ex.Message, ex);
                    }
                }
            }
        }
    }
}
