// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Tests.E2E.Extensions
{
    public static class ModelExtensions
    {
#if Stu3 || R4 || R4B
        public static Device AssignPatient(this Device device, ResourceReference patient)
        {
#if Stu3 || R4
            device.Patient = patient;
#else
            device.Subject = patient;
#endif
            return device;
        }
#else
        public static DeviceAssociation AssignPatient(this Device device, ResourceReference patient)
        {
            return new DeviceAssociation
            {
                Device = new ResourceReference($"Device/{device.Id}"),
                Subject = patient,
                Status = new CodeableConcept("http://hl7.org/fhir/deviceassociation-status", "implanted"),
            };
        }
#endif
    }
}
