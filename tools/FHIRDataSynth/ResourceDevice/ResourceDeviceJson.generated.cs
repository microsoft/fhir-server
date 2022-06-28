using System;

namespace ResourceProcessorNamespace.Device
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Udicarrier[] udiCarrier { get; set; }
        public string status { get; set; }
        public string distinctIdentifier { get; set; }
        public DateTime? manufactureDate { get; set; }
        public DateTime? expirationDate { get; set; }
        public string lotNumber { get; set; }
        public string serialNumber { get; set; }
        public Devicename[] deviceName { get; set; }
        public Type type { get; set; }
        public Patient patient { get; set; }
    }

    public class Type
    {
        public Coding[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Patient
    {
        public string reference { get; set; }
    }

    public class Udicarrier
    {
        public string deviceIdentifier { get; set; }
        public string carrierHRF { get; set; }
    }

    public class Devicename
    {
        public string name { get; set; }
        public string type { get; set; }
    }
}
