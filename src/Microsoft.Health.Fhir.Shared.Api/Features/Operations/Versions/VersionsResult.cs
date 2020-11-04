// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Xml.Serialization;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Versions
{
    /// <summary>
    /// Class used to hold data that needs to be returned to the client when the versions operation completes.
    /// </summary>
    [XmlRoot("versions")]
    public class VersionsResult
    {
        public VersionsResult(List<string> versions, string defaultVersion)
        {
            EnsureArg.IsNotNull(versions, nameof(versions));
            EnsureArg.IsNotNullOrWhiteSpace(defaultVersion, nameof(defaultVersion));

            Versions = versions;
            DefaultVersion = defaultVersion;
        }

        public VersionsResult()
        {
        }

        [XmlElement("version")]
        [JsonProperty("versions")]
        public List<string> Versions { get; }

        [XmlElement("default")]
        [JsonProperty("default")]
        public string DefaultVersion { get; set; }
    }
}
