﻿// -------------------------------------------------------------------------------------------------
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
            ImportMode importMode,
            bool allowNegativeVersions = false)
        {
            EnsureArg.IsNotNull(requestUri, nameof(requestUri));

            RequestUri = requestUri;
            InputFormat = inputFormat;
            InputSource = inputSource;
            Input = input;
            StorageDetail = storageDetail;
            ImportMode = importMode;
            AllowNegativeVersions = allowNegativeVersions;
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

        /// <summary>
        /// Flag indicating how late arivals are handled.
        /// Late arrival is a resource with explicit last updated and no explicit version. Its last updated is less than last updated on current version in the database.
        /// If late arrival conflicts with exting resource versions in the database, it is currently marked as a conflict and not ingested.
        /// With this flag set to true, it can be ingested with negative version value.
        /// </summary>
        public bool AllowNegativeVersions { get; set; }
    }
}
