// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Fhir.Anonymizer.Core;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.ExportDestinationClient;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportAnonymizerFactory : IAnonymizerFactory
    {
        private IExportDestinationClient _exportDestinationClient;

        public ExportAnonymizerFactory(IExportDestinationClient exportDestinationClient)
        {
            _exportDestinationClient = exportDestinationClient;
        }

        public async Task<IAnonymizer> CreateAnonymizerAsync(string configurationLocation, string fileHash, CancellationToken cancellationToken)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                await _exportDestinationClient.DownloadFileToStreamAsync(new Uri(configurationLocation), stream, cancellationToken);

                using (StreamReader reader = new StreamReader(stream))
                {
                    string configurationContent = await reader.ReadToEndAsync();
                    var engine = new AnonymizerEngine(AnonymizerConfigurationManager.CreateFromSettingsInJson(configurationContent));
                    return new ExportAnonymizer(engine);
                }
            }
        }
    }
}
