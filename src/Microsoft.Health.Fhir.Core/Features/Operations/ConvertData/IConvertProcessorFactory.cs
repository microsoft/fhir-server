// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Health.Fhir.Liquid.Converter.Models;
using Microsoft.Health.Fhir.Liquid.Converter.Processors;

namespace Microsoft.Health.Fhir.Core.Features.Operations.ConvertData
{
    public interface IConvertProcessorFactory
    {
        public IFhirConverter GetProcessor(DataType inputDataType);
    }
}
