using System;
using Octokit;

namespace BitBucketMigrator
{
    public class GithubClientFactory
    {
        private GitHubClient _githubClient;
        private readonly MigrationConfiguration _configuration;

        public GithubClientFactory(MigrationConfiguration configuration)
        {
            _configuration = configuration;
        }

        public GitHubClient GetOrCreateGithubClient()
        {
            if (_githubClient != null)
            {
                return _githubClient;
            }

            _githubClient = new GitHubClient(
                new ProductHeaderValue(_configuration.GithubOrganization), 
                _configuration.GithubUri);
            
            _githubClient.Credentials = new Credentials(_configuration.GithubToken);
            return _githubClient;
        }
    }
}