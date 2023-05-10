// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Core;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResourceParser : IImportResourceParser
    {
        private FhirJsonParser _parser;
        private IResourceWrapperFactory _resourceFactory;

        public ImportResourceParser(FhirJsonParser parser, IResourceWrapperFactory resourceFactory)
        {
            _parser = EnsureArg.IsNotNull(parser, nameof(parser));
            _resourceFactory = EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
        }

        public ImportResource Parse(long index, long offset, int length, string rawResource)
        {
            var resource = _parser.Parse<Resource>(rawResource);
            CheckConditionalReferenceInResource(resource);

            if (resource.Meta == null)
            {
                resource.Meta = new Meta();
            }

            if (resource.Meta.LastUpdated == null)
            {
                resource.Meta.LastUpdated = Clock.UtcNow;
            }

            var resourceElement = resource.ToResourceElement();
            var resourceWapper = _resourceFactory.Create(resourceElement, false, true);

            return new ImportResource(index, offset, length, resourceWapper);
        }

        private static void CheckConditionalReferenceInResource(Resource resource)
        {
            IEnumerable<ResourceReference> references = resource.GetAllChildren<ResourceReference>();
            foreach (ResourceReference reference in references)
            {
                if (string.IsNullOrWhiteSpace(reference.Reference))
                {
                    continue;
                }

                if (reference.Reference.Contains('?', StringComparison.Ordinal))
                {
                    throw new NotSupportedException("Conditional reference is not supported for $import.");
                }
            }
        }
    }
}
