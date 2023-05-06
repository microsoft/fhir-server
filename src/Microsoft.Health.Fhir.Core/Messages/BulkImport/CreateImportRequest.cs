// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class CreateImportRequest : IRequest<CreateImportResponse>
    {
        public CreateImportRequest(
            Uri requestUri,
            string inputFormat,
            Uri inputSource,
            IReadOnlyList<InputResource> input,
            ImportRequestStorageDetail storageDetail,
            ImportMode importMode)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            RequestUri = requestUri;
            InputFormat = inputFormat;
            InputSource = inputSource;
            Input = input;
            StorageDetail = storageDetail;
            ImportMode = importMode;
        }

        /// <summary>
        /// Import request uri
        /// </summary>
        public Uri RequestUri { get; }

        /// <summary>
        /// Input resource file format.
        /// </summary>
        public string InputFormat { get; }

        /// <summary>
        /// Input resource
        /// </summary>
        public Uri InputSource { get; }

        /// <summary>
        /// Input resource list
        /// </summary>
        public IReadOnlyList<InputResource> Input { get; }

        /// <summary>
        /// Storage details
        /// </summary>
        public ImportRequestStorageDetail StorageDetail { get; }

        /// <summary>
        /// Import mode
        /// </summary>
        public ImportMode ImportMode { get; }
    }
}
