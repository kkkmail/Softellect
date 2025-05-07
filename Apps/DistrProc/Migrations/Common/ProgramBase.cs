using Microsoft.EntityFrameworkCore;

namespace Softellect.Migrations.Common;

public static class ProgramBase<TContext> where TContext : DbContext, IHasServiceName, new()
{
    public static void MainImpl(string[] argv)
    {
        try
        {
            Console.WriteLine($"Starting database migration for {TContext.GetServiceName()}");
            using var context = new TContext();

            // Parse arguments and determine migration action
            var migrationAction = ParseMigrationArguments(argv);

            // Execute the appropriate migration
            ExecuteMigration(context, migrationAction);
        }
        catch (Exception ex)
        {
            HandleMigrationError(ex);
        }
    }

    private static MigrationAction ParseMigrationArguments(string[] argv)
    {
        if (argv.Length == 0)
        {
            Console.WriteLine("Performing UP migration (applying all pending migrations)");
            return new MigrationAction { Type = MigrationType.Up };
        }

        if (argv[0].Equals("down", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Performing DOWN migration (one step)");
            return new MigrationAction { Type = MigrationType.Down };
        }

        if (argv[0].StartsWith("m:", StringComparison.OrdinalIgnoreCase))
        {
            var targetMigration = argv[0].Substring(2);
            Console.WriteLine($"Migrating to specific migration: {targetMigration}");
            return new MigrationAction
            {
                Type = MigrationType.Specific,
                TargetMigration = targetMigration,
            };
        }

        Console.WriteLine($"Invalid argument: {argv[0]}");
        Console.WriteLine("Valid arguments are: no arguments, 'down', or 'm:<MigrationName>'");
        Environment.Exit(1);
        throw new InvalidOperationException(); // Unreachable but required for compilation
    }

    private static void ExecuteMigration(TContext context, MigrationAction action)
    {
        // Get the current database state
        var appliedMigrations = context.Database.GetAppliedMigrations().ToList();

        switch (action.Type)
        {
            case MigrationType.Up:
                MigrateUp(context, appliedMigrations);
                break;

            case MigrationType.Down:
                MigrateDown(context, appliedMigrations);
                break;

            case MigrationType.Specific:
                MigrateToSpecific(context, action.TargetMigration);
                break;
        }
    }

    private static void MigrateUp(TContext context, List<string> appliedMigrations)
    {
        var pendingMigrations = context.Database.GetPendingMigrations().ToList();

        if (pendingMigrations.Count == 0)
        {
            Console.WriteLine("All migrations have already been applied. No changes were made to the database.");
            return;
        }

        Console.WriteLine($"Found {pendingMigrations.Count} pending migrations.");
        Console.WriteLine("Applying pending migrations...");

        // Store initial state to track which migrations are newly applied
        var initialAppliedMigrations = new List<string>(appliedMigrations);

        // Apply all pending migrations
        context.Database.Migrate();

        // Report which migrations were applied
        ReportAppliedMigrations(context, initialAppliedMigrations);
    }

    private static void MigrateDown(TContext context, List<string> appliedMigrations)
    {
        if (appliedMigrations.Count == 0)
        {
            Console.WriteLine("No migrations have been applied. Cannot migrate down.");
            return;
        }

        // Report current migration state.
        var lastAppliedMigration = appliedMigrations.Last();
        Console.WriteLine($"Current migration: {lastAppliedMigration}");

        if (appliedMigrations.Count == 1)
        {
            // If we have only one migration, revert to the initial state.
            Console.WriteLine($"Removing migration: {lastAppliedMigration}");
            context.Database.Migrate(targetMigration: "0");
            Console.WriteLine("Database reverted to initial state (no migrations).");
        }
        else
        {
            // Otherwise, go back one migration.
            var previousMigration = appliedMigrations[appliedMigrations.Count - 2];
            Console.WriteLine($"Migrating down to: {previousMigration}");
            context.Database.Migrate(targetMigration: previousMigration);
            Console.WriteLine($"Successfully migrated down to {previousMigration}");
        }
    }

    private static void MigrateToSpecific(TContext context, string targetMigration)
    {
        // Validate the target migration exists
        var availableMigrations = context.Database.GetMigrations().ToList();
        if (!availableMigrations.Contains(targetMigration))
        {
            Console.WriteLine($"Error: Migration '{targetMigration}' not found. Available migrations:");
            availableMigrations.ForEach(m => Console.WriteLine($"    {m}"));
            Environment.Exit(1);
        }

        // Get and report the current migration state.
        var appliedMigrations = context.Database.GetAppliedMigrations().ToList();
        var currentMigration = appliedMigrations.LastOrDefault() ?? "0";
        Console.WriteLine($"Current migration: {currentMigration}");

        // Migrate to the target.
        Console.WriteLine($"Migrating to: {targetMigration}");
        context.Database.Migrate(targetMigration: targetMigration);
        Console.WriteLine($"Successfully migrated to {targetMigration}");
    }

    private static void ReportAppliedMigrations(TContext context, List<string> initialAppliedMigrations)
    {
        var finalAppliedMigrations = context.Database.GetAppliedMigrations().ToList();
        var newlyAppliedMigrations = finalAppliedMigrations.Except(initialAppliedMigrations).ToList();

        Console.WriteLine("Applied migrations:");
        foreach (var migration in newlyAppliedMigrations)
        {
            Console.WriteLine($"    {migration}");
        }

        Console.WriteLine("Database migration completed successfully.");
    }

    private static void HandleMigrationError(Exception ex)
    {
        Console.WriteLine($"Error during database migration: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
        Environment.Exit(1);
    }

    // Helper classes for migration actions
    private enum MigrationType
    {
        Up,
        Down,
        Specific
    }

    private class MigrationAction
    {
        public MigrationType Type { get; set; }
        public string TargetMigration { get; set; } = "";
    }
}
