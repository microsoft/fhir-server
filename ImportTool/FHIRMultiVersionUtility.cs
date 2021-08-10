// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using Hl7.Fhir.Model;

namespace ImportTool
{
    public enum FHIRVersion
    {
        STU3,
        R4,
        R5,
    }

    public static class FHIRMultiVersionUtility
    {
        private static Dictionary<FHIRVersion, Assembly> fhirAssemblyDic = new Dictionary<FHIRVersion, Assembly>();

        static FHIRMultiVersionUtility()
        {
            fhirAssemblyDic.Add(FHIRVersion.R4, Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "r4", "Hl7.Fhir.R4.Core.dll")));

            fhirAssemblyDic.Add(FHIRVersion.STU3, Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "stu3", "Hl7.Fhir.STU3.Core.dll")));

            fhirAssemblyDic.Add(FHIRVersion.R5, Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lib", "r5", "Hl7.Fhir.R5.Core.dll")));
        }

        public static string SerializeToString(Parameters parameters, FHIRVersion version)
        {
            string fhirString = null;
            var assembly = fhirAssemblyDic[version];
            var serializerType = assembly.GetType($"Hl7.Fhir.Serialization.FhirJsonSerializer");
            var serializer = Activator.CreateInstance(serializerType, new object[] { null });

            try
            {
                fhirString = (string)serializerType.GetMethod("SerializeToString").Invoke(serializer, new object[] { parameters });
            }
            catch
            {
                throw new SerializationException();
            }

            return fhirString;
        }
    }
}
