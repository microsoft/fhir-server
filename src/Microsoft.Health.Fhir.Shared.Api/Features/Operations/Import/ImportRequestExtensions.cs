// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using static Hl7.Fhir.Model.Parameters;

namespace Microsoft.Health.Fhir.Api.Features.Operations.Import
{
    public static class ImportRequestExtensions
    {
        public const string InputFormatParameterName = "inputFormat";
        public const string DefaultInputFormat = "application/fhir+ndjson";
        public const string InputSourceParameterName = "inputSource";
        public const string InputParameterName = "input";
        public const string TypeParameterName = "type";
        public const string UrlParameterName = "url";
        public const string EtagParameterName = "etag";
        public const string StorageDetailParameterName = "storageDetail";
        public const string ModeParameterName = "mode";
        public const string ForceParameterName = "force";
        public const string AllowNegativeVersionsParameterName = "allowNegativeVersions";
        public const string EventualConsistencyParameterName = "eventualConsistency";
        public const string ProcessingUnitBytesToReadParameterName = "processingUnitBytesToRead";
        public const string ErrorContainerNameParameterName = "errorContainerName";
        public const string DefaultStorageDetailType = "azure-blob";

        public static Parameters ToParameters(this ImportRequest importRequest)
        {
            Parameters parameters = new Parameters();

            if (string.IsNullOrEmpty(importRequest.InputFormat))
            {
                parameters.Add(InputFormatParameterName, new FhirString(DefaultInputFormat));
            }
            else
            {
                parameters.Add(InputFormatParameterName, new FhirString(importRequest.InputFormat));
            }

            if (importRequest.InputSource != null)
            {
                parameters.Add(InputSourceParameterName, new FhirUri(importRequest.InputSource));
            }

            if (importRequest.Input != null)
            {
                foreach (InputResource importResource in importRequest.Input)
                {
                    ParameterComponent inputResourceComponent = new ParameterComponent() { Name = InputParameterName };
                    parameters.Parameter.Add(inputResourceComponent);

                    if (!string.IsNullOrEmpty(importResource.Type))
                    {
                        inputResourceComponent.Part.Add(new ParameterComponent() { Name = TypeParameterName, Value = new FhirString(importResource.Type) });
                    }

                    if (!string.IsNullOrEmpty(importResource.Etag))
                    {
                        inputResourceComponent.Part.Add(new ParameterComponent() { Name = EtagParameterName, Value = new FhirString(importResource.Etag) });
                    }

                    if (importResource.Url != null)
                    {
                        inputResourceComponent.Part.Add(new ParameterComponent() { Name = UrlParameterName, Value = new FhirUri(importResource.Url) });
                    }
                }
            }

            ParameterComponent storageDetailsParameterComponent = new ParameterComponent() { Name = StorageDetailParameterName };
            if (!string.IsNullOrWhiteSpace(importRequest.StorageDetail?.Type))
            {
                storageDetailsParameterComponent.Part.Add(new ParameterComponent() { Name = TypeParameterName, Value = new FhirString(importRequest.StorageDetail.Type) });
            }

            parameters.Parameter.Add(storageDetailsParameterComponent);

            if (!string.IsNullOrEmpty(importRequest.Mode))
            {
                parameters.Add(ModeParameterName, new FhirString(importRequest.Mode));
            }

            if (importRequest.Force)
            {
                parameters.Add(ForceParameterName, new FhirBoolean(true));
            }

            if (importRequest.AllowNegativeVersions)
            {
                parameters.Add(AllowNegativeVersionsParameterName, new FhirBoolean(true));
            }

            if (importRequest.EventualConsistency)
            {
                parameters.Add(EventualConsistencyParameterName, new FhirBoolean(true));
            }

            if (!string.IsNullOrEmpty(importRequest.ErrorContainerName))
            {
                parameters.Add(ErrorContainerNameParameterName, new FhirString(importRequest.ErrorContainerName));
            }

            if (importRequest.ProcessingUnitBytesToRead > 0)
            {
                parameters.Add(ProcessingUnitBytesToReadParameterName, new Integer(importRequest.ProcessingUnitBytesToRead));
            }

            return parameters;
        }

        public static ImportRequest ExtractImportRequest(this Parameters parameters)
        {
            ImportRequest importRequest = new ImportRequest();

            if (parameters.TryGetStringValue(InputFormatParameterName, out string inputFormat))
            {
                importRequest.InputFormat = inputFormat;
            }

            if (parameters.TryGetUriValue(InputSourceParameterName, out Uri uriValue))
            {
                importRequest.InputSource = uriValue;
            }

            var inputResources = new List<InputResource>();
            foreach (ParameterComponent paramComponent in parameters.Get(InputParameterName))
            {
                ParameterComponent typeParam = paramComponent.Part?.Where(p => TypeParameterName.Equals(p.Name, StringComparison.Ordinal))?.FirstOrDefault();
                ParameterComponent urlParam = paramComponent.Part?.Where(p => UrlParameterName.Equals(p.Name, StringComparison.Ordinal))?.FirstOrDefault();
                ParameterComponent etagParam = paramComponent.Part?.Where(p => EtagParameterName.Equals(p.Name, StringComparison.Ordinal))?.FirstOrDefault();

                InputResource inputResource = new InputResource();

                if (typeParam.TryGetStringValue(out string type))
                {
                    inputResource.Type = type;
                }

                if (urlParam.TryGetUriValue(out Uri url))
                {
                    inputResource.Url = url;
                }

                if (etagParam.TryGetStringValue(out string etag))
                {
                    inputResource.Etag = etag;
                }

                inputResources.Add(inputResource);
            }

            importRequest.Input = inputResources;
            importRequest.StorageDetail = new ImportRequestStorageDetail();

            ParameterComponent storageDetailsComponent = parameters.GetSingle(StorageDetailParameterName);
            ParameterComponent storageTypeParam = storageDetailsComponent?.Part?.Where(p => TypeParameterName.Equals(p.Name, StringComparison.Ordinal))?.FirstOrDefault();
            if (storageTypeParam.TryGetStringValue(out string storageType))
            {
                importRequest.StorageDetail.Type = storageType;
            }
            else
            {
                importRequest.StorageDetail.Type = DefaultStorageDetailType;
            }

            if (parameters.TryGetStringValue(ModeParameterName, out string mode))
            {
                importRequest.Mode = mode;
            }

            if (parameters.TryGetBooleanValue(ForceParameterName, out bool force))
            {
                importRequest.Force = force;
            }

            if (parameters.TryGetBooleanValue(AllowNegativeVersionsParameterName, out bool allow))
            {
                importRequest.AllowNegativeVersions = allow;
            }

            if (parameters.TryGetBooleanValue(EventualConsistencyParameterName, out bool eventualConsistency))
            {
                importRequest.EventualConsistency = eventualConsistency;
            }

            if (parameters.TryGetStringValue(ErrorContainerNameParameterName, out string errorContainerName))
            {
                importRequest.ErrorContainerName = errorContainerName;
            }

            if (parameters.TryGetIntValue(ProcessingUnitBytesToReadParameterName, out int bytesToRead))
            {
                importRequest.ProcessingUnitBytesToRead = bytesToRead;
            }

            return importRequest;
        }
    }
}
