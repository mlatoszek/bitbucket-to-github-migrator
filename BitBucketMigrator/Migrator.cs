using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Atlassian.Stash;
using LibGit2Sharp;
using Octokit;
using Polly;
using Serilog;

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
            var allrepositoryNames = new HashSet<string>();
            
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
                foreach (var repository in repositories.Values)
                {
                    if (allrepositoryNames.Contains(repository.Name))
                    {
                        Log.Warning("Repository {repositoryName} from project {projectName} has non unique name and it needs to be migrated manually", repository.Name, project.Name);
                        continue;
                    }

                    allrepositoryNames.Add(repository.Name);
                }
            }
        }

        private async Task MigrateProject(MigrateProjectParameters migrateProjectParameters)
        {
            var githubClient = _clientFactory.GetOrCreateGithubClient();
            var existingRepositories = await githubClient.Repository.GetAllForOrg(_configuration.GithubOrganization);
            var repositoriesDict = existingRepositories.ToDictionary(x => x.Name, x => x);
            
            foreach (var repository in migrateProjectParameters.Repositories)
            {
                if (repositoriesDict.ContainsKey(repository.RepositoryName))
                {
                    var githubRepo =
                        repositoriesDict[repository.RepositoryName];
                    
                    if (githubRepo.Size > 0)
                    {
                        Log.Information(
                            "Skipping repository {repositoryName} from project {projectName} as it already exist and contains code",
                            repository.RepositoryName, migrateProjectParameters.ProjectName);
                        continue;
                    }
                    
                    Log.Information(
                        "Migrating to existing repository {repositoryName} from project {projectName}",
                        repository.RepositoryName, migrateProjectParameters.ProjectName);
                    
                    MigrateRepository(repository, githubRepo);
                }
                else
                {
                    try
                    {
                        var githubRepo =
                            await CreateGithubRepository(migrateProjectParameters, githubClient, repository);
                        MigrateRepository(repository, githubRepo);
                    }
                    catch (RepositoryExistsException)
                    {
                        Log.Information(
                            "Skipping repository {repositoryName} from project {projectName} as it already exist",
                            repository.RepositoryName, migrateProjectParameters.ProjectName);
                    }
                }
            }
        }

        private async Task<Octokit.Repository> CreateGithubRepository(
            MigrateProjectParameters migrateProjectParameters, 
            GitHubClient githubClient,
            Repository repository)
        {
            Log.Information("Migrating repository {repositoryName} from project {projectName} to github", repository.RepositoryName, migrateProjectParameters.ProjectName);
            var githubRepo = await githubClient.Repository.Create(_configuration.GithubOrganization,
                new NewRepository(repository.RepositoryName)
                {
                    Description = $"Migrated from {migrateProjectParameters.ProjectName} - {repository.RepositoryName}.",
                    Private = false
                });
            return githubRepo;
        }

        private void MigrateRepository(Repository repository, Octokit.Repository githubRepo)
        {
            var clonePath = Path.Combine(_configuration.TempPath, repository.RepositoryName);

            if (Directory.Exists(clonePath) && (Directory.GetFiles(clonePath).Length > 0 ||
                                                Directory.GetDirectories(clonePath).Length > 0))
            {
                using var localRepo = new LibGit2Sharp.Repository(clonePath);
                Policy
                    .Handle<LibGit2SharpException>(exception => exception.Message.Contains("Secure"))
                    .WaitAndRetry(2, retryAttempt => 
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) 
                    )
                    .Execute(() =>
                    {
                        Log.Information("Pushing to github");
                        localRepo.Network.Push(localRepo.Branches, new PushOptions
                        {
                            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                            {
                                Username = _configuration.GithubUsername,
                                Password = _configuration.GithubToken
                            },
                        });
                    });
            }
            else
            {
                var cloneOptions = new CloneOptions
                {
                    CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                    {
                        Username = _configuration.BitbucketUsername,
                        Password = _configuration.BitbucketPassword
                    },
                };

                Policy
                    .Handle<LibGit2SharpException>(exception => exception.Message.Contains("Secure"))
                    .WaitAndRetry(5, retryAttempt =>
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                    ).Execute(() =>
                    {
                        Log.Information("Cloning from Bitbucket");
                        return LibGit2Sharp.Repository.Clone(repository.CloneUrl.ToString(), clonePath, cloneOptions);
                    });
                
                using var localRepo = new LibGit2Sharp.Repository(clonePath);
                localRepo.Network.Remotes.Remove("origin");
                var newRemote = localRepo.Network.Remotes.Add("origin", githubRepo.CloneUrl);

                foreach (var localRepoBranch in localRepo.Branches)
                {
                    localRepo.Branches.Update(localRepoBranch,
                        b => b.Remote = newRemote.Name,
                        b => b.UpstreamBranch = localRepoBranch.CanonicalName);
                }

                Policy
                    .Handle<LibGit2SharpException>(exception => exception.Message.Contains("Secure"))
                    .WaitAndRetry(5, retryAttempt => 
                        TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) 
                    )
                    .Execute(() =>
                    {
                        Log.Information("Pushing to github");
                        localRepo.Network.Push(localRepo.Branches, new PushOptions
                        {
                            CredentialsProvider = (url, user, cred) => new UsernamePasswordCredentials
                            {
                                Username = _configuration.GithubUsername,
                                Password = _configuration.GithubToken
                            },
                        });
                    });
            }

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