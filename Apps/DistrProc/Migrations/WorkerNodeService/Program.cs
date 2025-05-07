using Softellect.Migrations.Common;

namespace Softellect.Migrations.WorkerNodeService;

public class Program
{
    public static void Main(string[] argv) => ProgramBase<WorkerNodeDbContext>.MainImpl(argv);
}
