// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResourceParser : IImportResourceParser
    {
        private FhirJsonParser _parser;
        private IResourceWrapperFactory _resourceFactory;
        private RecyclableMemoryStreamManager _recyclableMemoryStreamManager;
        private ICompressedRawResourceConverter _compressedRawResourceConverter;

        public ImportResourceParser(FhirJsonParser parser, IResourceWrapperFactory resourceFactory, ICompressedRawResourceConverter compressedRawResourceConverter)
        {
            EnsureArg.IsNotNull(parser, nameof(parser));
            EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
            EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));

            _parser = parser;
            _resourceFactory = resourceFactory;
            _compressedRawResourceConverter = compressedRawResourceConverter;
            _recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public ImportResource Parse(long index, long offset, int length, string rawContent)
        {
            var resource = _parser.Parse<Resource>(rawContent);
            CheckConditionalReferenceInResource(resource);

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
                    throw new NotSupportedException("Conditional reference not supported for initial import.");
                }
            }
        }

        private Stream GenerateCompressedRawResource(string rawResource)
        {
            var outputStream = new RecyclableMemoryStream(_recyclableMemoryStreamManager, tag: nameof(ImportResourceParser));
            _compressedRawResourceConverter.WriteCompressedRawResource(outputStream, rawResource);

            return outputStream;
        }
    }
}
