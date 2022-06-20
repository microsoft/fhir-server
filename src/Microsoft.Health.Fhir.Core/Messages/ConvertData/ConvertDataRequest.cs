// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Liquid.Converter.Models;

namespace Microsoft.Health.Fhir.Core.Messages.ConvertData
{
    /// <summary>
    /// Request for data conversion, currently supports Hl7v2, C-CDA, Json and version conversion of Fhir only.
    /// </summary>
    public class ConvertDataRequest : IRequest<ConvertDataResponse>
    {
        public ConvertDataRequest(
            string inputData,
            DataType inputDataType,
            string registryServer,
            bool isDefaultTemplateReference,
            string templateCollectionReference,
            string rootTemplate)
        {
            EnsureArg.IsNotNullOrEmpty(inputData, nameof(inputData));
            EnsureArg.IsNotNull<DataType>(inputDataType, nameof(inputDataType));
            EnsureArg.IsNotNull(registryServer, nameof(registryServer));
            EnsureArg.IsNotNull(templateCollectionReference, nameof(templateCollectionReference));
            EnsureArg.IsNotNullOrEmpty(rootTemplate, nameof(rootTemplate));

            InputData = inputData;
            InputDataType = inputDataType;
            RegistryServer = registryServer;
            IsDefaultTemplateReference = isDefaultTemplateReference;
            TemplateCollectionReference = templateCollectionReference;
            RootTemplate = rootTemplate;
        }

        /// <summary>
        /// Input data in string format.
        /// </summary>
        public string InputData { get; }

        /// <summary>
        /// Data type of input data, currently accepts Hl7v2 <see cref="DataType.Hl7v2"/>, C-CDA <see cref="DataType.Ccda"/>, JSON <see cref="DataType.Json"/> and FHIR <see cref="DataType.Fhir"/>
        /// </summary>
        public DataType InputDataType { get; }

        /// <summary>
        /// Container Registry Server extracted from template reference.
        /// </summary>
        public string RegistryServer { get; }

        /// <summary>
        /// Indicate whether we are using the default template or a custom template.
        /// </summary>
        public bool IsDefaultTemplateReference { get; }

        /// <summary>
        /// Reference for template collection.
        /// The format is "<registryServer>/<imageName>:<imageTag>" for template collection stored in container registries.
        /// Also supports image digest as reference. Will use 'latest' if no tag or digest present.
        /// </summary>
        public string TemplateCollectionReference { get; }

        /// <summary>
        /// Tells the convert engine which root template is used for this conversion call since we have a bunch of templates for different data types.
        /// </summary>
        public string RootTemplate { get; }
    }
}
