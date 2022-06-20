using System;

namespace ResourceProcessorNamespace.Immunization
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        public Vaccinecode vaccineCode { get; set; }
        public Patient patient { get; set; }
        public Encounter encounter { get; set; }
        public DateTime? occurrenceDateTime { get; set; }
        public bool? primarySource { get; set; }
    }

    public class Vaccinecode
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

    public class Encounter
    {
        public string reference { get; set; }
    }
}
