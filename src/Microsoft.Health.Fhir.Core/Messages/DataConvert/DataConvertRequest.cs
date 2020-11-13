// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.DataConvert.Models;

namespace Microsoft.Health.Fhir.Core.Messages.DataConvert
{
    public class DataConvertRequest : IRequest<DataConvertResponse>
    {
        public DataConvertRequest(string inputData, DataConvertInputDataType inputDataType, string registryServer, string templateCollectionReference, string entryPointTemplate)
        {
            EnsureArg.IsNotNullOrEmpty(inputData, nameof(inputData));
            EnsureArg.IsNotNull<DataConvertInputDataType>(inputDataType, nameof(inputDataType));
            EnsureArg.IsNotNull(registryServer, nameof(registryServer));
            EnsureArg.IsNotNull(templateCollectionReference, nameof(templateCollectionReference));
            EnsureArg.IsNotNullOrEmpty(entryPointTemplate, nameof(entryPointTemplate));

            InputData = inputData;
            InputDataType = inputDataType;
            RegistryServer = registryServer;
            TemplateCollectionReference = templateCollectionReference;
            EntryPointTemplate = entryPointTemplate;
        }

        public string InputData { get; }

        public DataConvertInputDataType InputDataType { get; }

        public string RegistryServer { get; }

        public string TemplateCollectionReference { get; }

        public string EntryPointTemplate { get; }
    }
}
