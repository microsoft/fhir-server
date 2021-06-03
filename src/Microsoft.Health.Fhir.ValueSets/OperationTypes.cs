﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.ValueSets
{
    public static class OperationTypes
    {
        public const string Validate = "validate";

        public const string ValidateUri = "http://hl7.org/fhir/OperationDefinition/Resource-validate";

        public const string PatientEverything = "patient-everything";

        public const string PatientEverythingUri = "https://www.hl7.org/fhir/patient-operation-everything.html";
    }
}
