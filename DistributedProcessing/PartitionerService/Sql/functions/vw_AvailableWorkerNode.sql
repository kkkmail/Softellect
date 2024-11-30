drop view if exists vw_AvailableWorkerNode
go


create view vw_AvailableWorkerNode
as
with a as
(
select
    w.workerNodeId
    ,s.solverId
    ,nodePriority
    ,isnull(cast(
        case
            when numberOfCores <= 0 then 1
            else (select count(1) as runningModels from RunQueue where workerNodeId = w.workerNodeId and runQueueStatusId in (2, 5, 7)) / (cast(numberOfCores as money))
        end as money), 0) as workLoad
    ,case when isnull(s.lastErrorOn, w.lastErrorOn) is null then null else datediff(minute, getdate(), isnull(s.lastErrorOn, w.lastErrorOn)) end as lastErrMinAgo
from WorkerNode w
inner join WorkerNodeSolver s on w.workerNodeId = s.workerNodeId
where isInactive = 0
)
select
    a.*, 
    c.new_id as OrderId
    from a
    cross apply (select new_id from vw_newid) c

go

