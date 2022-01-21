using System;

namespace ResourceProcessorNamespace.CarePlan
{
    // Machine generated, modified to have all properties nullable, modified to handle name collisions with CS keywords.
    public class Rootobject
    {
        public string resourceType { get; set; }
        public string id { get; set; }
        public Text text { get; set; }
        public string status { get; set; }
        public string intent { get; set; }
        public Category[] category { get; set; }
        public Subject subject { get; set; }
        public Encounter encounter { get; set; }
        public Period period { get; set; }
        public Careteam[] careTeam { get; set; }
        public Activity[] activity { get; set; }
    }

    public class Text
    {
        public string status { get; set; }
        public string div { get; set; }
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

    public class Category
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

    public class Careteam
    {
        public string reference { get; set; }
    }

    public class Activity
    {
        public Detail detail { get; set; }
    }

    public class Detail
    {
        public Code code { get; set; }
        public string status { get; set; }
        public Location location { get; set; }
    }

    public class Code
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

    public class Location
    {
        public string display { get; set; }
    }
}
