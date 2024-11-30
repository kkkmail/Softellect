IF OBJECT_ID('dbo.WorkerNode_Solver') IS NULL begin
    print 'Creating table dbo.WorkerNode_Solver ...'

    CREATE TABLE dbo.WorkerNode_Solver(
        workerNodeId uniqueidentifier not null,
        solverId uniqueidentifier not null,
        createdOn datetime not null,
        lastErrorOn datetime null,
        isDeployed bit not null,
        deploymentError nvarchar(max) null,
        CONSTRAINT PK_WorkerNode_Solver PRIMARY KEY CLUSTERED 
    (
        workerNodeId ASC,
        solverId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.WorkerNode_Solver ADD CONSTRAINT DF_WorkerNode_Solver_createdOn DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.WorkerNode_Solver ADD CONSTRAINT DF_WorkerNode_Solver_isDeployed DEFAULT (0) FOR isDeployed

    ALTER TABLE dbo.WorkerNode_Solver  WITH CHECK ADD  CONSTRAINT FK_WorkerNode_Solver_WorkerNode FOREIGN KEY(workerNodeId)
    REFERENCES dbo.WorkerNode (workerNodeId)
    ALTER TABLE dbo.WorkerNode_Solver CHECK CONSTRAINT FK_WorkerNode_Solver_WorkerNode

    ALTER TABLE dbo.WorkerNode_Solver  WITH CHECK ADD  CONSTRAINT FK_WorkerNode_Solver_Solver FOREIGN KEY(solverId)
    REFERENCES dbo.Solver (solverId)
    ALTER TABLE dbo.WorkerNode_Solver CHECK CONSTRAINT FK_WorkerNode_Solver_Solver

end else begin
    print 'Table dbo.WorkerNode already exists ...'
end
go

