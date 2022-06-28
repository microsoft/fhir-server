// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class VersionInfo
    {
        public VersionInfo(string versionString)
        {
            EnsureArg.IsNotNullOrEmpty(versionString, nameof(versionString));

            Version = new Version(versionString.Split('-').First());
            VersionString = versionString;
        }

        public Version Version { get; set; }

        public string VersionString { get; set; }

        public override string ToString()
        {
            return VersionString;
        }
    }
}
