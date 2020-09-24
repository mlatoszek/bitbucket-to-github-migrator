# Migrates repositories from BitBucket/Stash to GitHubEnterprise
## What does it do?
1. Gets all the projects and repositories from Bitbucket/Stash
2. Foreach repository in project
  2.1 Creates a repository in Github Enterpise organization space
  2.2 Clones repository from BitBucket
  2.3 Pushes repository back to Github (all branches)
  
## How to start
Fill the appsettings.json file with your own configuration.

```json
{
  "BitbucketRepoUri": "-- Bitbucket repository url --",
  "BitbucketUsername": "-- Bitbucket username --",
  "BitbucketPassword": "-- Bitbucket password --",

  "GithubUri": "-- Github Uri --",
  "GithubOrganization": "-- Github organization --",
  "GithubUsername": "-- Github username --",
  "GithubToken": "-- Github Token --",
  
  "TempPath": "-- Temp Path --"
}
```

Execute using dotnet run
