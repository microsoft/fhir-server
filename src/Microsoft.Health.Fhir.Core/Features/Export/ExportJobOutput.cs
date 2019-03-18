// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Features.Export
{
    public class ExportJobOutput
    {
        public ExportJobOutput()
            : this(new List<ExportJobOutputComponent>(), new List<ExportJobOutputComponent>())
        {
        }

        public ExportJobOutput(List<ExportJobOutputComponent> errors, List<ExportJobOutputComponent> results)
        {
            EnsureArg.IsNotNull(errors, nameof(errors));
            EnsureArg.IsNotNull(results, nameof(results));

            Errors = errors;
            Results = results;
        }

        public List<ExportJobOutputComponent> Errors { get; }

        public List<ExportJobOutputComponent> Results { get; }

        public void AddError(ExportJobOutputComponent error)
        {
            EnsureArg.IsNotNull(error, nameof(error));

            Errors.Add(error);
        }

        public void AddResult(ExportJobOutputComponent result)
        {
            EnsureArg.IsNotNull(result, nameof(result));

            Results.Add(result);
        }
    }
}
