// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Converter.Hl7v2;
using Microsoft.Health.Fhir.Converter.TemplateManagement;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;
using Microsoft.Health.Fhir.Core.Messages.DataConvert;

namespace Microsoft.Health.Fhir.Core.Features.Operations.DataConvert
{
    public class DataConvertEngine : IDataConvertEngine
    {
        private readonly IContainerRegistryTokenProvider _containerRegistryTokenProvider;
        private readonly DataConvertConfiguration _dataConvertConfiguration;
        private readonly Hl7v2Processor _hl7v2Processor;

        private const char ImageDigestDelimiter = '@';
        private const char ImageTagDelimiter = ':';
        private const char ImageRegistryDelimiter = '/';

        public DataConvertEngine(
            IContainerRegistryTokenProvider containerRegistryTokenProvider,
            IOptions<DataConvertConfiguration> dataConvertConfiguration)
        {
            EnsureArg.IsNotNull(containerRegistryTokenProvider);
            EnsureArg.IsNotNull(dataConvertConfiguration);

            _containerRegistryTokenProvider = containerRegistryTokenProvider;
            _dataConvertConfiguration = dataConvertConfiguration.Value;
            _hl7v2Processor = new Hl7v2Processor();
        }

        public async Task<DataConvertResponse> Process(DataConvertRequest convertRequest, CancellationToken cancellationToken)
        {
            var imageInfo = ParseTemplateImageReference(convertRequest.TemplateSetReference);

            var targetRegistry = _dataConvertConfiguration.ContainerRegistries
                .Where(registry => imageInfo.Registry.Equals(registry.ContainerRegistryServer, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();

            if (targetRegistry == null)
            {
                throw new ContainerRegistryNotRegisteredException($"Container registry server {imageInfo.Registry} not registered.");
            }

            var token = await _containerRegistryTokenProvider.GetTokenAsync(targetRegistry, cancellationToken);
            TemplateContainer templates = ServerEngine.Engine.OCIPull(token, imageInfo);

            // ToDo: implement generic processor selector
            string bundleResult = _hl7v2Processor.Convert(convertRequest.InputData, convertRequest.EntryPointTemplate, templates.Layers.Last().templates);
            return new DataConvertResponse(bundleResult);
        }

        private static ImageInfo ParseTemplateImageReference(string imageReference)
        {
            var registryDelimiterPosition = imageReference.IndexOf(ImageRegistryDelimiter, StringComparison.InvariantCultureIgnoreCase);
            if (registryDelimiterPosition == -1)
            {
                throw new TemplateReferenceInvalidException("Template image format is invalid: registry server is missing.");
            }

            var registryServer = imageReference.Substring(0, registryDelimiterPosition);
            imageReference = imageReference.Substring(registryDelimiterPosition + 1);

            if (imageReference.Contains(ImageDigestDelimiter, StringComparison.OrdinalIgnoreCase))
            {
                Tuple<string, string> imageMeta = SplitApart(imageReference, ImageDigestDelimiter);
                if (string.IsNullOrEmpty(imageMeta.Item1) || string.IsNullOrEmpty(imageMeta.Item2))
                {
                    throw new TemplateReferenceInvalidException("Template image format is invalid.");
                }

                return new ImageInfo(registryServer, imageMeta.Item1, tag: null, digest: imageMeta.Item2);
            }
            else if (imageReference.Contains(ImageTagDelimiter, StringComparison.OrdinalIgnoreCase))
            {
                Tuple<string, string> imageMeta = SplitApart(imageReference, ImageTagDelimiter);
                if (string.IsNullOrEmpty(imageMeta.Item1) || string.IsNullOrEmpty(imageMeta.Item2))
                {
                    throw new TemplateReferenceInvalidException("Template image format is invalid.");
                }

                return new ImageInfo(registryServer, imageMeta.Item1, tag: imageMeta.Item2);
            }

            return new ImageInfo(registryServer, imageReference);
        }

        private static Tuple<string, string> SplitApart(string input, char dilimeter)
        {
            var index = input.IndexOf(dilimeter, StringComparison.InvariantCultureIgnoreCase);
            return new Tuple<string, string>(input.Substring(0, index), input.Substring(index + 1));
        }
    }
}
