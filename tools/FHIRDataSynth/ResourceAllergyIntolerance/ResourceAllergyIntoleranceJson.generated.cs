using System;

namespace ResourceProcessorNamespace.AllergyIntolerance
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Clinicalstatus clinicalStatus { get; set; }
        public Verificationstatus verificationStatus { get; set; }
        public string type { get; set; }
        public string[] category { get; set; }
        public string criticality { get; set; }
        public Code code { get; set; }
        public Patient patient { get; set; }
        public DateTime? recordedDate { get; set; }
    }

    public class Clinicalstatus
    {
        public Coding[] coding { get; set; }
    }

    public class Coding
    {
        public string system { get; set; }
        public string code { get; set; }
    }

    public class Verificationstatus
    {
        public Coding1[] coding { get; set; }
    }

    public class Coding1
    {
        public string system { get; set; }
        public string code { get; set; }
    }

    public class Code
    {
        public Coding2[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding2
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Patient
    {
        public string reference { get; set; }
    }
}
