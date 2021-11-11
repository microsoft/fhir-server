/*************************************************************
    Sequence for generating unique 12.5ns "tick" components that are added
    to a base ID based on the timestamp to form a unique resource surrogate ID
**************************************************************/
GO

CREATE SEQUENCE dbo.ResourceSurrogateIdUniquifierSequence
        AS int
        START WITH 0
        INCREMENT BY 1
        MINVALUE 0
        MAXVALUE 79999
        CYCLE
        CACHE 1000000
GO
