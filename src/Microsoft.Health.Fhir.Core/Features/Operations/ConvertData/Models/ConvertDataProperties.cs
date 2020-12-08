// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    /// <summary>
    /// Class for keeping track of the property names of the in/out parameter for each operation.
    /// Some of these will be common across different operations and others might be specific.
    /// </summary>
    public static class ConvertDataProperties
    {
        public const string InputData = "inputData";

        public const string InputDataType = "inputDataType";

        public const string TemplateCollectionReference = "templateCollectionReference";

        public const string RootTemplate = "rootTemplate";
    }
}
