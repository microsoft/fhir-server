using System;

namespace ResourceProcessorNamespace.MedicationAdministration
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        public Medicationcodeableconcept medicationCodeableConcept { get; set; }
        public Subject subject { get; set; }
        public Context context { get; set; }
        public DateTime? effectiveDateTime { get; set; }
    }

    public class Medicationcodeableconcept
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

    public class Subject
    {
        public string reference { get; set; }
    }

    public class Context
    {
        public string reference { get; set; }
    }
}
