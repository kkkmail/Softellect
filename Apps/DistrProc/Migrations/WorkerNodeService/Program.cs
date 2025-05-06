using Softellect.Migrations.Common;

namespace Softellect.Migrations.WorkerNodeService;

public class Program
{
    public static void Main(string[] _) => ProgramBase<WorkerNodeDbContext>.MainImpl();
}
