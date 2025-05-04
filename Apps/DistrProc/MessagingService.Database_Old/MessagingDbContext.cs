using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;

namespace MessagingService.Database;

public class MessagingDbContext : DbContext
{
    public MessagingDbContext() : base(GetDesignTimeOptions())
    {
    }

    public MessagingDbContext(DbContextOptions<MessagingDbContext> options) : base(options)
    {
    }

    private static DbContextOptions<MessagingDbContext> GetDesignTimeOptions()
    {
        Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
        Console.WriteLine($"OS: {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
        Console.WriteLine($"Process Architecture: {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Console.WriteLine($"OS Architecture: {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}");
        Console.WriteLine($"Is 64-bit process: {Environment.Is64BitProcess}");

        var optionsBuilder = new DbContextOptionsBuilder<MessagingDbContext>();
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        Console.WriteLine($"assemblyLocation: '{assemblyLocation}'.");
        var directoryName = Path.GetDirectoryName(assemblyLocation);
        Console.WriteLine($"directoryName: '{directoryName}'.");
        var basePath = new Uri(directoryName!).LocalPath;
        Console.WriteLine($"basePath: '{basePath}'.");
        var appSettingsPath = Path.Combine(basePath, "appsettings.json");
        Console.WriteLine($"appSettingsPath: '{appSettingsPath}'.");
        var json = File.ReadAllText(appSettingsPath);
        Console.WriteLine($"json: '{json}'.");
        var appSettings = JObject.Parse(json);
        Console.WriteLine($"appSettings: '{appSettings}'.");
        var connectionString = appSettings["connectionStrings"]!["MessagingService"]!.ToString();
        Console.WriteLine($"connectionString: '{connectionString}'.");
        optionsBuilder.UseSqlServer(connectionString);
        Console.WriteLine($"optionsBuilder: '{optionsBuilder}'.");
        return optionsBuilder.Options;
    }

    public DbSet<DeliveryType> DeliveryTypes { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
}
