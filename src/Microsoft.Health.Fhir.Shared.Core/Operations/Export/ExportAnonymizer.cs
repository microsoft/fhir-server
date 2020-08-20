// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Anonymizer.Core;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Export
{
    public class ExportAnonymizer : IAnonymizer
    {
        private AnonymizerEngine _engine;

        public ExportAnonymizer(AnonymizerEngine engine)
        {
            EnsureArg.IsNotNull(engine, nameof(engine));

            _engine = engine;
        }

        public ResourceElement Anonymize(ResourceElement resourceElement)
        {
            EnsureArg.IsNotNull(resourceElement, nameof(resourceElement));

            return new ResourceElement(_engine.AnonymizeElement(resourceElement.Instance));
        }
    }
}
