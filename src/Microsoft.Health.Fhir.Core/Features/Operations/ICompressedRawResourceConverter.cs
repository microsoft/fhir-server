// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Handles converting raw resource strings and compressed streams.
    /// </summary>
    public interface ICompressedRawResourceConverter
    {
        /// <summary>
        /// Read from compressed resource stream to string
        /// </summary>
        /// <param name="compressedResourceStream">Compressed resource stream</param>
        public string ReadCompressedRawResource(Stream compressedResourceStream);

        /// <summary>
        /// Convert rawResource string to compressed stream
        /// </summary>
        /// <param name="outputStream">Output steam for compressed data.</param>
        /// <param name="rawResource">Input raw resource string.</param>
        public void WriteCompressedRawResource(Stream outputStream, string rawResource);
    }
}
