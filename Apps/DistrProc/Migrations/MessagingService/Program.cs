using Softellect.Migrations.Common;

namespace Softellect.Migrations.MessagingService;

public class Program
{
    public static void Main(string[] _) => ProgramBase<MessagingDbContext>.MainImpl();
}
