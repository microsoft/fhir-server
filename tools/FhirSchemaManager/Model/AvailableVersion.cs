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
        public AvailableVersion(int id, Uri script)
        {
            EnsureArg.IsNotNull(script, nameof(script));

            Id = id;
            Script = script;
        }

        public int Id { get; }

        public Uri Script { get; }
    }
}
