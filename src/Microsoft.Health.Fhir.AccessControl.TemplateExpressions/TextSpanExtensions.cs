// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Superpower.Model;

namespace Microsoft.Health.Fhir.AccessControl.TemplateExpressions
{
    /// <summary>
    /// Extension methods on <see cref="TextSpan"/>.
    /// </summary>
    internal static class TextSpanExtensions
    {
        /// <summary>
        /// Returns a <see cref="TextSpan"/> that begins at <see cref="start"/> and ends at the end of <see cref="end"/>.
        /// </summary>
        /// <param name="start">The start span.</param>
        /// <param name="end">The end span.</param>
        /// <returns>A span that runs from <see cref="start"/> to <see cref="end"/>.</returns>
        public static TextSpan UntilEndOf(this TextSpan start, TextSpan end)
        {
            return new TextSpan(start.Source, start.Position, end.Position.Absolute + end.Length - start.Position.Absolute);
        }
    }
}
