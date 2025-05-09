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

            // Parse arguments and execute the appropriate migration action
            ExecuteMigrationCommand(context, argv);
        }
        catch (Exception ex)
        {
            HandleMigrationError(ex);
        }
    }

    private static void ExecuteMigrationCommand(TContext context, string[] argv)
    {
        if (argv.Length == 0 || argv.Length > 1)
        {
            PrintInvalidArgumentMessage(string.Join(" ", argv));
            return;
        }

        var arg = argv[0];
        var command = arg.Split(':', 2)[0].ToLowerInvariant();
        var parameter = arg.Contains(':') ? arg.Substring(arg.IndexOf(':') + 1) : string.Empty;

        var availableMigrations = context.Database.GetMigrations().ToList();

        switch (command)
        {
            case "up":
                PerformUpMigration(context);
                break;

            case "down" when !string.IsNullOrEmpty(parameter):
                PerformDownMigration(context, parameter, availableMigrations);
                break;

            case "downfile" when !string.IsNullOrEmpty(parameter):
                PerformDownMigrationFromFile(context, parameter, availableMigrations);
                break;

            case "verify" when !string.IsNullOrEmpty(parameter):
                VerifyMigration(parameter, availableMigrations);
                break;

            case "verifyfile" when !string.IsNullOrEmpty(parameter):
                VerifyMigrationFromFile(parameter, availableMigrations);
                break;

            case "extract" when !string.IsNullOrEmpty(parameter):
                ExtractMigrationState(context, parameter);
                break;

            default:
                PrintInvalidArgumentMessage(arg);
                break;
        }
    }

    private static void PerformUpMigration(TContext context)
    {
        Console.WriteLine("Performing UP migration (applying all pending migrations)");
        MigrateUp(context);
    }

    private static void PerformDownMigration(TContext context, string targetMigration, List<string> availableMigrations)
    {
        Console.WriteLine($"Performing DOWN migration to {targetMigration}");

        if (ValidateMigration(availableMigrations, targetMigration))
        {
            MigrateDown(context, targetMigration);
        }
    }

    private static void PerformDownMigrationFromFile(TContext context, string fileName,
        List<string> availableMigrations)
    {
        Console.WriteLine($"Performing DOWN migration using target migration from file: {fileName}");

        string targetMigration = ReadMigrationFromFile(fileName);
        if (!string.IsNullOrEmpty(targetMigration) && ValidateMigration(availableMigrations, targetMigration))
        {
            MigrateDown(context, targetMigration);
        }
    }

    private static void VerifyMigration(string migrationName, List<string> availableMigrations)
    {
        Console.WriteLine($"Verifying migration: {migrationName}");
        ValidateMigration(availableMigrations, migrationName);
    }

    private static void VerifyMigrationFromFile(string fileName, List<string> availableMigrations)
    {
        Console.WriteLine($"Verifying migration from file: {fileName}");

        string migrationToVerify = ReadMigrationFromFile(fileName);
        if (!string.IsNullOrEmpty(migrationToVerify))
        {
            ValidateMigration(availableMigrations, migrationToVerify);
        }
    }

    private static void PrintInvalidArgumentMessage(string arg)
    {
        Console.WriteLine($"Invalid argument: '{arg}'.");
        Console.WriteLine("Valid arguments are: 'up', 'down:<MigrationName>', 'downFile:<FileName>', " +
                          "'verify:<MigrationName>', 'verifyFile:<FileName>', or 'extract:<FileName>'.");
        Environment.Exit(1);
    }

    private static string ReadMigrationFromFile(string fileName)
    {
        try
        {
            if (!File.Exists(fileName))
            {
                Console.WriteLine($"Error: File '{fileName}' does not exist.");
                Environment.Exit(1);
                return string.Empty; // Unreachable but required for compilation
            }

            string migration = File.ReadAllText(fileName).Trim();

            if (string.IsNullOrWhiteSpace(migration))
            {
                Console.WriteLine($"Error: File '{fileName}' is empty or contains only whitespace.");
                Environment.Exit(1);
                return string.Empty; // Unreachable but required for compilation
            }

            Console.WriteLine($"Read migration '{migration}' from file {fileName}");
            return migration;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error reading migration from file: {ex.Message}");
            Environment.Exit(1);
            return string.Empty; // Unreachable but required for compilation
        }
    }

    private static bool ValidateMigration(List<string> availableMigrations, string migrationName)
    {
        if (!availableMigrations.Contains(migrationName))
        {
            Console.WriteLine($"Error: Migration '{migrationName}' not found. Available migrations:");
            availableMigrations.ForEach(m => Console.WriteLine($"    {m}"));
            Environment.Exit(1);
            return false; // Unreachable but required for compilation
        }

        Console.WriteLine($"Migration '{migrationName}' is valid.");
        return true;
    }

    private static void MigrateUp(TContext context)
    {
        var appliedMigrations = context.Database.GetAppliedMigrations().ToList();
        var pendingMigrations = context.Database.GetPendingMigrations().ToList();

        if (pendingMigrations.Count == 0)
        {
            Console.WriteLine("All migrations have already been applied. No changes were made to the database.");
            return;
        }

        Console.WriteLine($"Found {pendingMigrations.Count} pending migrations:");
        pendingMigrations.ForEach(m => Console.WriteLine($"    {m}"));
        Console.WriteLine("Applying pending migrations...");

        // Store initial state to track which migrations are newly applied
        var initialAppliedMigrations = new List<string>(appliedMigrations);

        // Apply all pending migrations
        context.Database.Migrate();

        // Report which migrations were applied
        ReportAppliedMigrations(context, initialAppliedMigrations);
    }

    private static void MigrateDown(TContext context, string targetMigration)
    {
        // Get and report the current migration state
        var appliedMigrations = context.Database.GetAppliedMigrations().ToList();

        if (appliedMigrations.Count == 0)
        {
            Console.WriteLine("No migrations have been applied. Cannot migrate down.");
            return;
        }

        var currentMigration = appliedMigrations.Last();
        Console.WriteLine($"Current migration: {currentMigration}");

        // Check if we're already at or before the target migration
        int targetIndex = appliedMigrations.IndexOf(targetMigration);
        if (targetIndex == -1)
        {
            Console.WriteLine($"Target migration '{targetMigration}' has not been applied yet.");
            Console.WriteLine("Applied migrations:");
            appliedMigrations.ForEach(m => Console.WriteLine($"    {m}"));
            return;
        }

        if (currentMigration == targetMigration)
        {
            Console.WriteLine($"Database is already at migration '{targetMigration}'. No changes needed.");
            return;
        }

        Console.WriteLine($"Migrating down to: {targetMigration}");
        context.Database.Migrate(targetMigration: targetMigration);
        Console.WriteLine($"Successfully migrated down to {targetMigration}");
    }

    private static void ExtractMigrationState(TContext context, string fileName)
    {
        var appliedMigrations = context.Database.GetAppliedMigrations().ToList();
        var currentMigration = appliedMigrations.LastOrDefault() ?? "0";

        try
        {
            File.WriteAllText(fileName, currentMigration);
            Console.WriteLine($"Current migration '{currentMigration}' extracted to {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error extracting migration state: {ex.Message}");
            Environment.Exit(1);
        }
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
}
