// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace ImportTool
{
    public class Config
    {
        /// <summary>
        /// Azure storage connection string.
        /// /// </summary>
        public string StorageConnectionString { get; set; }

        /// <summary>
        /// Determines the size of each splited file.
        /// /// </summary>
        public long SplitSizeInBytes { get; set; }

        /// <summary>
        /// Determines the max number of input or splited file.
        /// /// </summary>
        public int MaxFileNumber { get; set; }
    }
}
