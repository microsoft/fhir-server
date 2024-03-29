﻿// -------------------------------------------------------------------------------------------------
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
    public class IncompleteDeleteException : RequestTooCostlyException
    {
        public IncompleteDeleteException(int numberOfResourceVersionsDeleted)
            : base(message: string.Format(Resources.PartialDeleteSuccess, numberOfResourceVersionsDeleted, StringComparison.Ordinal))
        {
        }
    }
}
