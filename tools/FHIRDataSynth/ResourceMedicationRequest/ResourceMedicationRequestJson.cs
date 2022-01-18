using System;

namespace ResourceProcessorNamespace.MedicationRequest
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        public string intent { get; set; }
        public Medicationcodeableconcept medicationCodeableConcept { get; set; }
        public Subject subject { get; set; }
        public Encounter encounter { get; set; }
        public DateTime? authoredOn { get; set; }
        public Requester requester { get; set; }
        public Dosageinstruction[] dosageInstruction { get; set; }
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

    public class Encounter
    {
        public string reference { get; set; }
    }

    public class Requester
    {
        public string reference { get; set; }
        public string display { get; set; }
    }

    public class Dosageinstruction
    {
        public int? sequence { get; set; }
        public string text { get; set; }
        public bool? asNeededBoolean { get; set; }
    }
}
