// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace FhirSchemaManager.Model
{
    public class CurrentVersion
    {
        public CurrentVersion(int id, string status, List<string> servers)
        {
            EnsureArg.IsNotNullOrWhiteSpace(status, nameof(status));
            EnsureArg.IsNotNull(servers, nameof(servers));

            Id = id;
            Status = status;
            Servers = servers;
        }

        public int Id { get; }

        public string Status { get; }

        public List<string> Servers { get; }
    }
}
