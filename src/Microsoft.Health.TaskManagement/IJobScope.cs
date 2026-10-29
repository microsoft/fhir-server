// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.JobManagement;

/// <summary>
/// Owns the lifetime of a job and its execution dependencies.
/// The caller must dispose the scope after execution completes.
/// </summary>
public interface IJobScope : IDisposable
{
    /// <summary>
    /// Gets the job to execute.
    /// </summary>
    IJob Job { get; }
}
