// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.FhirPackages
{
    /// <summary>
    /// Limits applied when reading a FHIR package. SearchParameter packages are kilobytes in size, so the
    /// defaults are far below what the archive format allows.
    /// </summary>
    public sealed class FhirPackageReaderOptions
    {
        /// <summary>
        /// Gets or sets the largest compressed tarball accepted, in bytes.
        /// </summary>
        public int MaxCompressedBytes { get; set; } = 10 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the largest decompressed tar stream accepted, in bytes, including headers and padding.
        /// </summary>
        public int MaxExpandedBytes { get; set; } = 50 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the largest single archive entry accepted, in bytes.
        /// </summary>
        public int MaxEntryBytes { get; set; } = 5 * 1024 * 1024;

        /// <summary>
        /// Gets or sets the largest number of physical archive entries accepted.
        /// </summary>
        public int MaxEntries { get; set; } = 1_000;

        /// <summary>
        /// Gets or sets the deepest JSON nesting accepted in any entry.
        /// </summary>
        public int MaxJsonDepth { get; set; } = 64;

        /// <summary>
        /// Gets or sets the largest accepted ratio of decompressed to compressed bytes.
        /// </summary>
        public int MaxExpansionRatio { get; set; } = 100;
    }
}
