// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class CreateImportRequest : IRequest<CreateImportResponse>
    {
        public CreateImportRequest(
            Uri requestUri,
            string inputFormat,
            Uri inputSource,
            IReadOnlyList<ImportRequestInput> input,
            ImportRequestStorageDetail storageDetail)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            RequestUri = requestUri;
            InputFormat = inputFormat;
            InputSource = inputSource;
            Input = input;
            StorageDetail = storageDetail;
        }

        public Uri RequestUri { get; }

        public string InputFormat { get; }

        public Uri InputSource { get; }

        public IReadOnlyList<ImportRequestInput> Input { get; }

        public ImportRequestStorageDetail StorageDetail { get; }
    }
}
