using System;

namespace ResourceProcessorNamespace.CareTeam
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public string status { get; set; }
        public Subject subject { get; set; }
        public Encounter encounter { get; set; }
        public Period period { get; set; }
        public Participant[] participant { get; set; }
        public Reasoncode[] reasonCode { get; set; }
        public Managingorganization[] managingOrganization { get; set; }
    }

    public class Subject
    {
        public string reference { get; set; }
    }

    public class Encounter
    {
        public string reference { get; set; }
    }

    public class Period
    {
        public DateTime? start { get; set; }
    }

    public class Participant
    {
        public Role[] role { get; set; }
        public Member member { get; set; }
    }

    public class Member
    {
        public string reference { get; set; }
        public string display { get; set; }
    }

    public class Role
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

    public class Reasoncode
    {
        public Coding1[] coding { get; set; }
        public string text { get; set; }
    }

    public class Coding1
    {
        public string system { get; set; }
        public string code { get; set; }
        public string display { get; set; }
    }

    public class Managingorganization
    {
        public string reference { get; set; }
        public string display { get; set; }
    }
}
