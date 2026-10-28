# GreenHedges.HomeServer

The services that run on my always-on home server, in one public repository and one .NET 10 solution
(`GreenHedges.HomeServer.slnx`): the Libre and Weather collectors (`src/Collectors/`), their shared code
(`src/Common/`), and the web apps Vehicle Manager (`src/VehicleManager/`) and Pay & Pensions (`src/PayAndPensions/`,
being built), each with its own `CLAUDE.md` for working on it. What each does and how it's set up: `README.md`,
`docs/<service>/`, `deploy/<service>/`.

## Build and test

```bash
dotnet build                 # warnings are errors (Directory.Build.props)
dotnet test                  # every project's tests; all must pass
docker compose up -d --wait  # local SQL Server (compose.yaml) for the web apps' database and page tests
```

- Package versions live in `Directory.Packages.props` (central package management); project files have no versions.
- CI (`.github/workflows/ci.yml`) builds and runs every test, with its own SQL Server, on each PR and on `main`.
- When fixing a bug, add a test that fails without the fix.

## Git and pull requests

- Never commit to `main`: one branch and PR per change. Merge only on green CI, with a merge commit, then delete the
  branch (GitHub deletes it on merge; delete the local copy too). I usually review the PR first.
- Commit messages and PRs end with Claude's attribution lines.

## Public repository, private notes

- This repository is public. Nothing personal goes in files, commit messages, PRs or issues: names, hosts, IPs,
  usernames, locations, sensor or station ids, registrations, account or health data, ping URLs, keys or passwords.
- Personal details live in the private notes repo `GreenHedges.HomeServer.Notes`, cloned into `notes/` (git-ignored);
  it commits straight to `main`. Script defaults go in the git-ignored `deploy/local.env`; secrets go in user secrets
  locally and `/etc/<service>/env` on the server, never in the repository.

## Editing files

- When suggesting a command that edits a file, on the Mac or the server, use `ttt` (never `nano`, `vim` or another
  editor). For root-owned files on the server use `sudoedit <file>` (never `sudo ttt` or `sudo nano`); it opens ttt
  through `SUDO_EDITOR`. Setup: README.md, "Editing files".

## The server

- Read `notes/server/SERVER.md` before planning or changing anything that runs on the server, and update it (and push
  the notes) when a service, port, user or folder is added or changed.
- SSH to the server is read-only for Claude: give me the commands for anything needing sudo or that changes the server.
- Never run a deploy script (`deploy/**/*.sh`) to test it, not even `--help`: they really deploy. Check them with
  `bash -n` and by reading.
