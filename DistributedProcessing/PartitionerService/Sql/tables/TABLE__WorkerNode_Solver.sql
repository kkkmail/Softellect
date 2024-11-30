IF OBJECT_ID('dbo.WorkerNodeSolver') IS NULL begin
    print 'Creating table dbo.WorkerNodeSolver ...'

    CREATE TABLE dbo.WorkerNodeSolver(
        workerNodeId uniqueidentifier not null,
        solverId uniqueidentifier not null,
        createdOn datetime not null,
        lastErrorOn datetime null,
        isDeployed bit not null,
        deploymentError nvarchar(max) null,
        CONSTRAINT PK_WorkerNodeSolver PRIMARY KEY CLUSTERED 
    (
        workerNodeId ASC,
        solverId ASC
    ) WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]
    ) ON [PRIMARY]

    ALTER TABLE dbo.WorkerNodeSolver ADD CONSTRAINT DF_WorkerNodeSolver_createdOn DEFAULT (getdate()) FOR createdOn
    ALTER TABLE dbo.WorkerNodeSolver ADD CONSTRAINT DF_WorkerNodeSolver_isDeployed DEFAULT (0) FOR isDeployed

    ALTER TABLE dbo.WorkerNodeSolver  WITH CHECK ADD  CONSTRAINT FK_WorkerNodeSolver_WorkerNode FOREIGN KEY(workerNodeId)
    REFERENCES dbo.WorkerNode (workerNodeId)
    ALTER TABLE dbo.WorkerNodeSolver CHECK CONSTRAINT FK_WorkerNodeSolver_WorkerNode

    ALTER TABLE dbo.WorkerNodeSolver  WITH CHECK ADD  CONSTRAINT FK_WorkerNodeSolver_Solver FOREIGN KEY(solverId)
    REFERENCES dbo.Solver (solverId)
    ALTER TABLE dbo.WorkerNodeSolver CHECK CONSTRAINT FK_WorkerNodeSolver_Solver

end else begin
    print 'Table dbo.WorkerNodeSolver already exists ...'
end
go

