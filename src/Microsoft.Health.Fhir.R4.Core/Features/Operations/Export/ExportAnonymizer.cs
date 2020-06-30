// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Fhir.Anonymizer.Core;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportAnonymizer : IAnonymizer
    {
        private readonly IAnonymizerSettingsProvider _provider;
        private readonly ResourceDeserializer _resourceDeserializer;
        private readonly Lazy<AnonymizerEngine> _engine;

        public ExportAnonymizer(IAnonymizerSettingsProvider provider, ResourceDeserializer resourceDeserializer)
        {
            _provider = provider;
            _resourceDeserializer = resourceDeserializer;

            _engine = new Lazy<AnonymizerEngine>(CreateAnonymizerEngine);
        }

        public ResourceElement Anonymize(ResourceElement resourceElement)
        {
            return new ResourceElement(_engine.Value.AnonymizeTypedElement(resourceElement.Instance));
        }

        private AnonymizerEngine CreateAnonymizerEngine()
        {
            AnonymizerEngine.InitializeFhirPathExtensionSymbols();
            string settings = _provider.GetAnonymizerSettings();
            return new AnonymizerEngine(AnonymizerConfigurationManager.CreateFromSettings(settings));
        }
    }
}
