using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Atlassian.Stash;
using Atlassian.Stash.Entities;
using LibGit2Sharp;
using LibGit2Sharp.Handlers;
using Microsoft.Alm.Authentication;
using Microsoft.Extensions.Configuration;
using Octokit;
using Credentials = Octokit.Credentials;

namespace BitBucketMigrator
{
    class Program
    {
        static async Task Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", false, true)
                .Build();
            var configuration = config.Get<MigrationConfiguration>();
            
            var migrator = new Migrator(configuration);
            await migrator.Migrate();
        }

        
    }
}