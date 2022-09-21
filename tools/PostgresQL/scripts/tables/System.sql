CREATE TABLE system
(
    SystemId int NOT NULL,
    Value varchar(256) NOT NULL
);

SELECT create_reference_table('system');

