// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import;

public class InMemoryImportErrorStore : IImportErrorStore
{
    string IImportErrorStore.ErrorFileLocation { get; } = nameof(InMemoryImportErrorStore);

    public IList<(int Line, string Outcome)> ImportErrors { get; } = new List<(int Line, string Outcome)>();

    public Task UploadErrorsAsync(string[] importErrors, CancellationToken cancellationToken)
    {
        foreach (var error in importErrors)
        {
            // Extract the line number from the error message
            string pattern = @"at line: (\d+)";
            Match match = Regex.Match(error, pattern);

            ImportErrors.Add(match.Success ?
                (int.Parse(match.Groups[1].Value), error)
                : (0, error));
        }

        return Task.CompletedTask;
    }
}
