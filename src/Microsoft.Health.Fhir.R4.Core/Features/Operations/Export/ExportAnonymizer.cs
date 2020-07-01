// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Fhir.Anonymizer.Core;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportAnonymizer : IAnonymizer
    {
        private IAnonymizerSettingsProvider _provider;
        private AnonymizerEngine _engine;

        public ExportAnonymizer(IAnonymizerSettingsProvider provider)
        {
            _provider = provider;
        }

        public async Task InitailizeAsync()
        {
            // TODO: validate config
            string settings = await _provider.GetAnonymizerSettingsAsync();
            _engine = new AnonymizerEngine(AnonymizerConfigurationManager.CreateFromSettings(settings));
        }

        public ResourceElement Anonymize(ResourceElement resourceElement)
        {
            return new ResourceElement(_engine.AnonymizeTypedElement(resourceElement.Instance));
        }
    }
}
