// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import.Models
{
    public class InputResource
    {
        /// <summary>
        /// Determines the resource type of the input
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// Determines the location of the input data.
        /// Should be a uri pointing to the input data.
        /// </summary>
        public Uri Url { get; set; }

        /// <summary>
        /// Blob length in bytes
        /// </summary>
        public long BlobLength { get; set; }

        /// <summary>
        /// Offset to read input blob/file from
        /// </summary>
        public long Offset { get; set; }

        /// <summary>
        /// Number of bytes to read
        /// </summary>
        public int BytesToRead { get; set; }

        /// <summary>
        /// Determines the etag of resource file.
        /// </summary>
        public string Etag { get; set; }

        public InputResource Clone()
        {
            var res = new InputResource();
            res.Type = Type;
            res.Url = Url;
            res.BlobLength = BlobLength;
            res.Offset = Offset;
            res.BytesToRead = BytesToRead;
            res.Etag = Etag;

            return res;
        }
    }
}
