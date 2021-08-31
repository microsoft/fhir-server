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
        public const string InputFormatParamterName = "inputFormat";
        public const string DefaultInputFormat = "application/fhir+ndjson";
        public const string InputSourceParamterName = "inputSource";
        public const string InputParamterName = "input";
        public const string TypeParamterName = "type";
        public const string UrlParamterName = "url";
        public const string EtagParamterName = "etag";
        public const string StorageDetailParamterName = "storageDetail";
        public const string ModeParamterName = "mode";
        public const string ForceParamterName = "force";
        public const string DefaultStorageDetailType = "azure-blob";

        public static Parameters ToParameters(this ImportRequest importRequest)
        {
            Parameters paramters = new Parameters();

            if (string.IsNullOrEmpty(importRequest.InputFormat))
            {
                paramters.Add(InputFormatParamterName, new FhirString(DefaultInputFormat));
            }
            else
            {
                paramters.Add(InputFormatParamterName, new FhirString(importRequest.InputFormat));
            }

            if (importRequest.InputSource != null)
            {
                paramters.Add(InputSourceParamterName, new FhirUri(importRequest.InputSource));
            }

            if (importRequest.Input != null)
            {
                foreach (InputResource importResource in importRequest.Input)
                {
                    ParameterComponent inputResourceComponent = new ParameterComponent() { Name = InputParamterName };
                    paramters.Parameter.Add(inputResourceComponent);

                    if (!string.IsNullOrEmpty(importResource.Type))
                    {
                        inputResourceComponent.Part.Add(new ParameterComponent() { Name = TypeParamterName, Value = new FhirString(importResource.Type) });
                    }

                    if (!string.IsNullOrEmpty(importResource.Etag))
                    {
                        inputResourceComponent.Part.Add(new ParameterComponent() { Name = EtagParamterName, Value = new FhirString(importResource.Etag) });
                    }

                    if (importResource.Url != null)
                    {
                        inputResourceComponent.Part.Add(new ParameterComponent() { Name = UrlParamterName, Value = new FhirUri(importResource.Url) });
                    }
                }
            }

            ParameterComponent storageDetailsParameterComponent = new ParameterComponent() { Name = StorageDetailParamterName };
            if (!string.IsNullOrWhiteSpace(importRequest.StorageDetail?.Type))
            {
                storageDetailsParameterComponent.Part.Add(new ParameterComponent() { Name = TypeParamterName, Value = new FhirString(importRequest.StorageDetail.Type) });
            }

            paramters.Parameter.Add(storageDetailsParameterComponent);

            if (!string.IsNullOrEmpty(importRequest.Mode))
            {
                paramters.Add(ModeParamterName, new FhirString(importRequest.Mode));
            }

            if (importRequest.Force)
            {
                paramters.Add(ForceParamterName, new FhirBoolean(true));
            }

            return paramters;
        }

        public static ImportRequest ExtractImportRequest(this Parameters parameters)
        {
            ImportRequest importRequest = new ImportRequest();

            if (parameters.TryGetStringValue(InputFormatParamterName, out string inputFormat))
            {
                importRequest.InputFormat = inputFormat;
            }

            if (parameters.TryGetUriValue(InputSourceParamterName, out Uri uriValue))
            {
                importRequest.InputSource = uriValue;
            }

            var inputResources = new List<InputResource>();
            foreach (ParameterComponent paramComponent in parameters.Get(InputParamterName))
            {
                ParameterComponent typeParam = paramComponent.Part?.Where(p => TypeParamterName.Equals(p.Name, StringComparison.Ordinal))?.FirstOrDefault();
                ParameterComponent urlParam = paramComponent.Part?.Where(p => UrlParamterName.Equals(p.Name, StringComparison.Ordinal))?.FirstOrDefault();
                ParameterComponent etagParam = paramComponent.Part?.Where(p => EtagParamterName.Equals(p.Name, StringComparison.Ordinal))?.FirstOrDefault();

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

            ParameterComponent storageDetailsComponent = parameters.GetSingle(StorageDetailParamterName);
            ParameterComponent storageTypeParam = storageDetailsComponent?.Part?.Where(p => TypeParamterName.Equals(p.Name, StringComparison.Ordinal))?.FirstOrDefault();
            if (storageTypeParam.TryGetStringValue(out string storageType))
            {
                importRequest.StorageDetail.Type = storageType;
            }
            else
            {
                importRequest.StorageDetail.Type = DefaultStorageDetailType;
            }

            if (parameters.TryGetStringValue(ModeParamterName, out string mode))
            {
                importRequest.Mode = mode;
            }

            if (parameters.TryGetBooleanValue(ForceParamterName, out bool force))
            {
                importRequest.Force = force;
            }

            return importRequest;
        }
    }
}
