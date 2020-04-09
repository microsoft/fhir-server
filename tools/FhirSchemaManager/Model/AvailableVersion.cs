// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace FhirSchemaManager.Model
{
    public class AvailableVersion
    {
        public AvailableVersion(int version, Uri scriptUri)
        {
            EnsureArg.IsNotNull<int>(version, nameof(version));
            EnsureArg.IsNotNull(scriptUri, nameof(scriptUri));

            Version = version;
            ScriptUri = scriptUri;
        }

        public int Version { get; }

        public Uri ScriptUri { get; }
    }
}
