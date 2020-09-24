using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlassian.Stash;
using LibGit2Sharp;
using Octokit;

namespace BitBucketMigrator
{
    public class Migrator
    {
        private readonly MigrationConfiguration _configuration;
        private readonly GithubClientFactory _clientFactory;

        public Migrator(MigrationConfiguration configuration)
        {
            _configuration = configuration;
            _clientFactory = new GithubClientFactory(configuration);
        }

        public async Task Migrate()
        {
            var client = new StashClient(
                _configuration.BitbucketRepoUri.ToString(),
                _configuration.BitbucketUsername,
                _configuration.BitbucketPassword);

            var projects = await client.Projects.Get();
            foreach (var project in projects.Values)
            {
                var repositories = await client.Repositories.Get(project.Key);

                await MigrateProject(new MigrateProjectParameters(
                    projectName: project.Name,
                    description: project.Description,
                    repositories: repositories.Values.Select(x => new Repository
                    (
                        repositoryName: x.Name,
                        cloneUrl: x.Links.Clone
                            .Where(l => l.Name == "http")
                            .Select(c => c.Href).First(),
                        slug: x.Slug
                    )).ToList()
                ));
            }
        }

        private async Task MigrateProject(MigrateProjectParameters migrateProjectParameters)
        {
            var githubClient = _clientFactory.GetOrCreateGithubClient();
            var existingRepositories = await githubClient.Repository.GetAllForOrg(_configuration.GithubOrganization);
            var repoNamesSet = existingRepositories.Select(x => x.Name).ToHashSet();
            foreach (var repository in migrateProjectParameters.Repositories.Where(x =>
                !repoNamesSet.Contains(x.RepositoryName)))
            {
                var githubRepo = await CreateGithubRepository(migrateProjectParameters, githubClient, repository);
                MigrateRepository(repository, githubRepo);
                repoNamesSet.Add(repository.RepositoryName);
            }
        }

        private async Task<Octokit.Repository> CreateGithubRepository(
            MigrateProjectParameters migrateProjectParameters, 
            GitHubClient githubClient,
            Repository repository)
        {
            var githubRepo = await githubClient.Repository.Create(_configuration.GithubOrganization,
                new NewRepository(repository.RepositoryName)
                {
                    Description = $"Migrated from {migrateProjectParameters.ProjectName} - {repository.RepositoryName}.\n" +
                                  $"{migrateProjectParameters.Description}",
                    Private = false
                });
            return githubRepo;
        }

        private void MigrateRepository(Repository repository, Octokit.Repository githubRepo)
        {
            var clonePath = Path.Combine(_configuration.TempPath, repository.RepositoryName);
            var cloneOptions = new CloneOptions
            {
                CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                {
                    Username = _configuration.BitbucketUsername,
                    Password = _configuration.BitbucketPassword
                },
            };
            LibGit2Sharp.Repository.Clone(repository.CloneUrl.ToString(), clonePath, cloneOptions);

            using var localRepo = new LibGit2Sharp.Repository(clonePath);
            localRepo.Network.Remotes.Remove("origin");
            var newRemote = localRepo.Network.Remotes.Add("origin", githubRepo.CloneUrl);

            foreach (var localRepoBranch in localRepo.Branches)
            {
                localRepo.Branches.Update(localRepoBranch,
                    b => b.Remote = newRemote.Name,
                    b => b.UpstreamBranch = localRepoBranch.CanonicalName);
            }

            localRepo.Network.Push(localRepo.Branches, new PushOptions
            {
                CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                {
                    Username = _configuration.GithubUsername,
                    Password = _configuration.GithubToken
                },
            });
        }

        class MigrateProjectParameters
        {
            public string ProjectName { get; }
            public string Description { get; }
            public IList<Repository> Repositories { get; }

            public MigrateProjectParameters(string projectName, string description, IList<Repository> repositories)
            {
                ProjectName = projectName;
                Description = description;
                Repositories = repositories;
            }
        }

        class Repository
        {
            public string RepositoryName { get; }
            public Uri CloneUrl { get; }
            public string Slug { get; }

            public Repository(string repositoryName, Uri cloneUrl, string slug)
            {
                RepositoryName = repositoryName;
                CloneUrl = cloneUrl;
                Slug = slug;
            }
        }
    }
}