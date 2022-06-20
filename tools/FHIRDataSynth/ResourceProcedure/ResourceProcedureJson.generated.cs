using System;

namespace ResourceProcessorNamespace.Procedure
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        public Code code { get; set; }
        public Subject subject { get; set; }
        public Encounter encounter { get; set; }
        public Performedperiod performedPeriod { get; set; }
    }

    public class Code
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

    public class Performedperiod
    {
        public DateTime? start { get; set; }
        public DateTime? end { get; set; }
    }
}
