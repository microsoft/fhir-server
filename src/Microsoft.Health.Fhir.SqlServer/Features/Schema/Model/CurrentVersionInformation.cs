// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema.Model
{
    public class CurrentVersionInformation
    {
        public CurrentVersionInformation(int id, string status, IList<string> servers)
        {
            EnsureArg.IsNotNull(status, nameof(status));

            Id = id;
            Status = status;
            Servers = servers;
        }

        public int Id { get; }

        public string Status { get; }

        public IList<string> Servers { get; }
    }
}
