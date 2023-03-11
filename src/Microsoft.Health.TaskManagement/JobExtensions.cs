// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.JobManagement;

public static class JobExtensions
{
    public static string SerializedResult(object result)
    {
        return JsonConvert.SerializeObject(result);
    }
}
