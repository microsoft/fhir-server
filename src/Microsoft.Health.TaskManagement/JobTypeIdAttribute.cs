// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.JobManagement;

[AttributeUsage(AttributeTargets.Class)]
public sealed class JobTypeIdAttribute : Attribute
{
    public JobTypeIdAttribute(int jobTypeId)
    {
        JobTypeId = jobTypeId;
    }

    public int JobTypeId { get; }
}
