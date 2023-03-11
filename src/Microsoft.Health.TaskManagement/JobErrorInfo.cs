// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.JobManagement;

public class JobErrorInfo
{
    public JobErrorInfo(string message, string stackTrace)
    {
        Message = message;
        StackTrace = stackTrace;
    }

    [JsonConstructor]
    public JobErrorInfo()
    {
    }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("stackTrace")]
    public string StackTrace { get; set; }
}
