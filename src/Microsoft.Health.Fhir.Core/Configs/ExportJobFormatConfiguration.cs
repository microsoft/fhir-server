// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class ExportJobFormatConfiguration
    {
        /// <summary>
        /// The name of the format. This is how the format is referenced in the _format parameter when creating a new export job.
        /// The name is used as a unique identifier. Formats should not share names.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The format definition string. An export job's format is used to create the folder stucture inside the container.
        /// The format is defined by tags and characters. Supported tags are defined below. The / character is used to indicate a subfolder.
        /// <timestamp> - Places a timestamp corisponding to the time the export job was enqueued.
        /// <resourcename> - The name of the resource currently being exported.
        /// <id> - The GUID id of the export job.
        /// </summary>
        public string Format { get; set; }

        /// <summary>
        /// Whether the format is the default format for when no format is specified by the user.
        /// </summary>
        public bool Default { get; set; } = false;
    }
}
