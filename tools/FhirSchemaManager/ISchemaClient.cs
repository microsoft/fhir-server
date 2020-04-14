// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading.Tasks;
using FhirSchemaManager.Model;

namespace FhirSchemaManager
{
    public interface ISchemaClient
    {
        Task<List<CurrentVersion>> GetCurrentVersionInformation();
    }
}
