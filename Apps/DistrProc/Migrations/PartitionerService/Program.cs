using Softellect.Migrations.Common;

namespace Softellect.Migrations.PartitionerService;

public class Program
{
    public static void Main(string[] _) => ProgramBase<PartitionerDbContext>.MainImpl();
}
