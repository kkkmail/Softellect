using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService;

[Table("Solver")]
[Index(nameof(SolverName), IsUnique = true)]
public class Solver
{
    [Key]
    [Column("solverId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid SolverId { get; set; }

    [Column("solverOrder")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long SolverOrder { get; set; }

    [Required]
    [Column("solverName")]
    [StringLength(100)]
    public string SolverName { get; set; } = null!;

    [Column("description")]
    [StringLength(2000)]
    public string? Description { get; set; }

    [Column("solverData")]
    public byte[]? SolverData { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("isDeployed")]
    public bool IsDeployed { get; set; }

    // public ICollection<RunQueue> RunQueues { get; set; } = new List<RunQueue>();
}