// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Tests.E2E.Extensions
{
    public static class ModelExtensions
    {
        public static Device AssignPatient(this Device device, ResourceReference patient)
        {
#if Stu3 || R4
            device.Patient = patient;
#else
            device.Subject = patient;
#endif
            return device;
        }
    }
}
