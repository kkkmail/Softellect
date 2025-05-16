using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.PartitionerService;

[Table("WorkerNodeSolver")]
[PrimaryKey(nameof(WorkerNodeId), nameof(SolverId))]
public class WorkerNodeSolver
{
    [Column("workerNodeId")]
    public Guid WorkerNodeId { get; set; }

    [Column("solverId")]
    public Guid SolverId { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("isDeployed")]
    public bool IsDeployed { get; set; }

    [Column("deploymentError")]
    [MaxLength]
    public string? DeploymentError { get; set; }

    // Navigation properties
    [ForeignKey(nameof(WorkerNodeId))]
    public WorkerNode WorkerNode { get; set; } = null!;

    [ForeignKey(nameof(SolverId))]
    public Solver Solver { get; set; } = null!;
}