IF OBJECT_ID('dbo.Solver') IS NULL begin
    print 'Creating table dbo.Solver ...'

    CREATE TABLE dbo.Solver(
        solverId uniqueidentifier not null,
        solverOrder bigint identity(1,1) not null,
        solverName nvarchar(100) not null,
        description nvarchar(2000) null, 
        solverData varbinary(max) null,
        createdOn datetime not null,
        isDeployed bit not null,
    CONSTRAINT PK_Solver PRIMARY KEY CLUSTERED 
    (
        solverId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]

    ALTER TABLE dbo.Solver ADD CONSTRAINT DF_Solver_createdOn DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.Solver ADD CONSTRAINT DF_Solver_isDeployed DEFAULT (0) FOR isDeployed

    CREATE UNIQUE NONCLUSTERED INDEX UX_Solver_solverName ON dbo.Solver
    (
        solverName ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, SORT_IN_TEMPDB = OFF, DROP_EXISTING = OFF, ONLINE = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]

end else begin
	print 'Table dbo.Solver already exists ...'
end
go

