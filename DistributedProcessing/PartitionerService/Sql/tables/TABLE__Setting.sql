IF OBJECT_ID('dbo.Setting') IS NULL begin
    print 'Creating table dbo.Setting ...'

    CREATE TABLE dbo.Setting(
        settingName nvarchar(100) NOT NULL,
        settingBool bit NULL,
        settingGuid uniqueidentifier NULL,
        settingLong bigint NULL,
        settingText nvarchar(max) NULL,
        settingBinary varbinary(max) NULL,
        createdOn datetime not null,
    CONSTRAINT PK_Setting PRIMARY KEY CLUSTERED 
    (
        settingName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.Setting ADD DEFAULT (getdate()) FOR createdOn

    CREATE UNIQUE NONCLUSTERED INDEX IX_Setting ON dbo.Setting
    (
        settingName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, IGNORE_DUP_KEY = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
end else begin
	print 'Table dbo.Setting already exists ...'
end
go



