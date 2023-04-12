// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EnsureThat;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.IO;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportResourceParser : IImportResourceParser
    {
        internal static readonly Encoding ResourceEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        private FhirJsonParser _parser;
        private IResourceWrapperFactory _resourceFactory;
        private IResourceMetaPopulator _resourceMetaPopulator;
        private RecyclableMemoryStreamManager _recyclableMemoryStreamManager;
        private ICompressedRawResourceConverter _compressedRawResourceConverter;

        public ImportResourceParser(FhirJsonParser parser, IResourceWrapperFactory resourceFactory, IResourceMetaPopulator resourceMetaPopulator, ICompressedRawResourceConverter compressedRawResourceConverter)
        {
            EnsureArg.IsNotNull(parser, nameof(parser));
            EnsureArg.IsNotNull(resourceFactory, nameof(resourceFactory));
            EnsureArg.IsNotNull(compressedRawResourceConverter, nameof(compressedRawResourceConverter));

            _parser = parser;
            _resourceFactory = resourceFactory;
            _resourceMetaPopulator = resourceMetaPopulator;
            _compressedRawResourceConverter = compressedRawResourceConverter;
            _recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();
        }

        public ImportResource Parse(long id, long index, string rawContent)
        {
            Resource resource = _parser.Parse<Resource>(rawContent);
            CheckConditionalReferenceInResource(resource);

            _resourceMetaPopulator.Populate(id, resource);

            ResourceElement resourceElement = resource.ToResourceElement();
            ResourceWrapper resourceWapper = _resourceFactory.Create(resourceElement, false, true);

            return new ImportResource(id, index, resourceWapper)
            {
                CompressedStream = null, // GenerateCompressedRawResource(resourceWapper.RawResource.Data),
            };
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
