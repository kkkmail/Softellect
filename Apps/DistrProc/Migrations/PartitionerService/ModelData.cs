using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Softellect.Migrations.PartitionerService;

[Table("ModelData")]
public class ModelData
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    [Column("runQueueId")]
    public Guid RunQueueId { get; set; }

    /// <summary>
    /// All the initial data that is needed to run the calculation.
    /// It is designed to be huge, and so zipped binary format is used.
    /// </summary>
    [Required]
    [Column("modelData")]
    public byte[] ModelDataBytes { get; set; } = null!;

    // Navigation property
    [ForeignKey(nameof(RunQueueId))]
    public RunQueue RunQueue { get; set; } = null!;
}