using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace BitBucketMigrator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .WriteTo.Console()
                .CreateLogger();
            
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", false, true)
                .Build();
            var configuration = config.Get<MigrationConfiguration>();
            
            Log.Information("Executing migrations");
            var migrator = new Migrator(configuration);
            await migrator.Migrate();
        }

        
    }
}