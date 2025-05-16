using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.PartitionerService;

[Table("WorkerNode")]
[Index(nameof(WorkerNodeName), IsUnique = true)]
[Index(nameof(WorkerNodeOrder), IsUnique = true)]
public class WorkerNode
{
    [Key]
    [Column("workerNodeId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid WorkerNodeId { get; set; }

    [Column("workerNodeOrder")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long WorkerNodeOrder { get; set; }

    [Required]
    [Column("workerNodeName")]
    [StringLength(100)]
    public string WorkerNodeName { get; set; } = null!;

    [Column("nodePriority")]
    public int NodePriority { get; set; }

    [Column("numberOfCores")]
    public int NumberOfCores { get; set; }

    [Column("description")]
    [StringLength(1000)]
    public string? Description { get; set; }

    [Column("isInactive")]
    public bool IsInactive { get; set; }

    [Column("workerNodePublicKey")]
    public byte[]? WorkerNodePublicKey { get; set; }

    [Column("createdOn")]
    public DateTime CreatedOn { get; set; }

    [Column("modifiedOn")]
    public DateTime ModifiedOn { get; set; }
}