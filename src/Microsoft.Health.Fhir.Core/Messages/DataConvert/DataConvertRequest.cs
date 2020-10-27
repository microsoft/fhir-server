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
        public DataConvertRequest(string inputData, DataConvertInputDataType inputDataType, string templateSetReference, string entryPointTemplate)
        {
            EnsureArg.IsNotNullOrEmpty(inputData);
            EnsureArg.IsNotNull(templateSetReference);
            EnsureArg.IsNotNullOrEmpty(entryPointTemplate);

            InputData = inputData;
            InputDataType = inputDataType;
            TemplateSetReference = templateSetReference;
            EntryPointTemplate = entryPointTemplate;
        }

        public string InputData { get; set; }

        public DataConvertInputDataType InputDataType { get; }

        public string TemplateSetReference { get; }

        public string EntryPointTemplate { get; }
    }
}
