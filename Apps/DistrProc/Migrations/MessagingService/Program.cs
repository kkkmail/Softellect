using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.MessagingService;

public class Program
{
    public static void Main(string[] _)
    {
        try
        {
            Console.WriteLine($"Starting database migration for {MessagingDbContext.GetServiceName()}");
            using var context = new MessagingDbContext();
            Console.WriteLine("Applying pending migrations...");
            context.Database.Migrate();
            Console.WriteLine("Database migration completed successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during database migration: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}
