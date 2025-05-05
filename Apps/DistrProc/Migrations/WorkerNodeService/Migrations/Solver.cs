using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.WorkerNodeService.Migrations;

[Index("solverName", Name = "UX_Solver_solverName", IsUnique = true)]
public partial class Solver
{
    [Key]
    public Guid solverId { get; set; }

    public long solverOrder { get; set; }

    [StringLength(100)]
    public string solverName { get; set; } = null!;

    [StringLength(2000)]
    public string? description { get; set; }

    public byte[]? solverData { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime createdOn { get; set; }

    public bool isDeployed { get; set; }

    [InverseProperty("solver")]
    public virtual ICollection<RunQueue> RunQueue { get; set; } = new List<RunQueue>();
}
