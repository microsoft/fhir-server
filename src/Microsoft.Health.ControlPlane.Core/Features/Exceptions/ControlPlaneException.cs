// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;

namespace Microsoft.Health.ControlPlane.Core.Features.Exceptions
{
    public abstract class ControlPlaneException : Abstractions.Exceptions.MicrosoftHealthException
    {
        protected ControlPlaneException(string message, IEnumerable<string> issues = null)
            : base(message)
        {
            if (issues != null)
            {
                ((List<string>)Issues).AddRange(issues);
            }
        }

        public ICollection<string> Issues { get; } = new List<string>();

        protected static string ValidateAndFormatMessage(string format, params string[] name)
        {
            EnsureArg.IsNotNullOrWhiteSpace(format, nameof(format));

            return string.Format(format, name);
        }
    }
}
