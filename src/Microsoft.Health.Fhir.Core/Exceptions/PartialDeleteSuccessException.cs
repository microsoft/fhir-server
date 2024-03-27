// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Abstractions.Exceptions;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    public class PartialDeleteSuccessException : MicrosoftHealthException
    {
        public PartialDeleteSuccessException(int numberOfResourceVersionsDeleted)
            : base(Resources.PartialDeleteSuccess.Replace("{0}", numberOfResourceVersionsDeleted.ToString(), StringComparison.Ordinal))
        {
        }
    }
}
