using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.Common;

[Table("RunQueueStatus")]
[Index(nameof(RunQueueStatusName), IsUnique = true)]
public class RunQueueStatus
{
    [Key]
    [Column("runQueueStatusId")]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int RunQueueStatusId { get; set; }

    [Required]
    [Column("runQueueStatusName")]
    [StringLength(50)]
    public string RunQueueStatusName { get; set; } = null!;
}