// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    /// <summary>
    /// Parser for raw data into ImportResource.
    /// </summary>
    public interface IImportResourceParser
    {
        /// <summary>
        /// Parse raw resource data.
        /// </summary>
        /// <param name="index">index of the resource.</param>
        /// <param name="offset">Read stream offset in blob/file.</param>
        /// <param name="length">Json length in bytes includind EOL</param>
        /// <param name="rawContent">raw content in string format.</param>
        /// <returns>ImportResource</returns>
        public ImportResource Parse(long index, long offset, int length, string rawContent);
    }
}
