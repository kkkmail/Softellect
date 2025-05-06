using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.Common;

public static class ProgramBase<TContext> where TContext : DbContext, IHasServiceName, new()
{
    public static void MainImpl()
    {
        try
        {
            Console.WriteLine($"Starting database migration for {TContext.GetServiceName()}");
            using var context = new TContext();

            var pendingMigrations = context.Database.GetPendingMigrations().ToList();

            if (pendingMigrations.Count > 0)
            {
                Console.WriteLine($"Found {pendingMigrations.Count} pending migrations.");
                Console.WriteLine("Applying pending migrations...");
                var initialAppliedMigrations = context.Database.GetAppliedMigrations().ToList();
                context.Database.Migrate();

                var finalAppliedMigrations = context.Database.GetAppliedMigrations().ToList();
                var newlyAppliedMigrations = finalAppliedMigrations.Except(initialAppliedMigrations).ToList();

                Console.WriteLine("Applied migrations:");

                foreach (var migration in newlyAppliedMigrations)
                {
                    Console.WriteLine($"    {migration}");
                }

                Console.WriteLine("Database migration completed successfully.");
            }
            else
            {
                Console.WriteLine("All migrations have already been applied. No changes were made to the database.");

                // // Optionally list all applied migrations
                // var appliedMigrations = context.Database.GetAppliedMigrations().ToList();
                // Console.WriteLine($"Current database has {appliedMigrations.Count} migrations applied:");
                //
                // foreach (var migration in appliedMigrations)
                // {
                //     Console.WriteLine($"  - {migration}");
                // }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during database migration: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }
}
