using System;
using System.IO;

namespace BitBucketMigrator
{
    public class MigrationConfiguration
    {
        public Uri BitbucketRepoUri{ get; set; }
        public string BitbucketUsername { get; set; }
        public string BitbucketPassword { get; set; }
        
        public Uri GithubUri { get; set; }
        public string GithubOrganization { get; set; }
        public string GithubUsername { get; set; }
        public string GithubToken { get; set; }
        public string TempPath { get; set; } = Path.GetTempPath();
    }
}