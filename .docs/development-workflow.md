# Development workflow

This personal project uses short-lived branches and GitHub pull requests to keep
changes understandable, make self-review routine, and record decisions for future
maintenance and portfolio discussions. The default branch is `main`.

## Instructions for coding agents

Read this document at the beginning of every repository task. The root
`AGENTS.md` is the entry point that makes this requirement discoverable to agents.
Apply these defaults automatically when asked to implement a feature or fix;
the user should not have to restate the Git workflow. Explicit user instructions
override these defaults.

## 1. Inspect and choose a branch

- Check the current branch, working tree, and applicable repository instructions
  before editing. Read relevant product and architecture documentation.
- For a new change, create `codex/<short-description>` from an up-to-date `main`.
  Fetch `origin` when available; if local `main` is behind, update it using a
  fast-forward only. If local and remote `main` have diverged, preserve both and
  resolve the situation before choosing a base. Never reset away commits.
- Continue the existing branch when the request is a follow-up to the same
  unmerged change. A new task after a squash merge gets a new branch from `main`.
- Preserve uncommitted user changes. Do not stash, discard, commit, or carry
  unrelated work into the new task without understanding its ownership. If that
  prevents safe progress, explain the specific conflict and ask for direction.
- Questions and read-only investigations do not require a branch. All edits,
  including documentation and small fixes, use branches by default.

## 2. Implement one coherent change

Keep each branch and PR focused on one feature, fix, or closely related set of
changes. Update relevant documentation when behavior or development instructions
change. Use descriptive commits such as `Fix review counts at day boundaries`.
Temporary exploratory commits are acceptable on the task branch.

Do not commit Anki collections, private settings, exports, personal data, secrets,
or generated build output. Respect the read-only importer guarantees documented
in `README.md` and `.docs/DESIGN.md`.

Place project documentation in `.docs/`. Root exceptions and approval requirements
are defined in [AGENTS.md](../AGENTS.md); update links when relocating documents.

## 3. Validate and self-review

For application code changes, run the relevant checks and normally run:

```powershell
dotnet build src/AnkiStats.App -c Release
dotnet run --project tests/AnkiStats.Checks -c Release
```

Use `.tools/dotnet/dotnet.exe` if the project-local SDK is needed. Add meaningful
regression coverage for behavior changes when appropriate. Check visible UI
changes in an isolated session using the README's data-safety instructions.
Publish a build only when packaging is affected or a build is requested.

Documentation-only changes need a diff review and checks of links, commands, and
consistency; they do not require an application build. Inspect the final diff
and run `git diff --check`. Report checks that could not run and why; never claim
an unperformed check passed.

## 4. Deliver through a pull request

The normal delivery for an implementation task is to commit the task's changes,
push its branch to `origin`, and open a GitHub pull request targeting `main`.
These are standing workflow defaults unless the user requests local-only work
or another delivery method. Follow environment permission requirements; these
defaults do not bypass tool approvals. Never include unrelated user changes.

Use an available GitHub integration or authenticated GitHub CLI. If publishing
is unavailable, finish all local work and report the branch, validation results,
and the specific remaining publishing step. Do not silently substitute a direct
push to `main`.

Give the PR a descriptive title and a concise description covering:

- The problem or feature and resulting behavior.
- Significant decisions or limitations that help a reviewer assess the change.
- Validation performed and results; screenshots for visible UI changes when useful.

Self-review the complete diff and inspect configured CI results. Use a draft PR
when work is incomplete. Issues are optional: create or link one when it helps
track a bug, larger feature, or deferred work. No permanent `develop` branch or
mandatory external reviewer is required for this solo project.

## 5. Merge and clean up

Leave the completed PR open for the user's review. Only merge when the user
explicitly requests it; implementing a feature does not itself authorize merging.
The sole maintainer can merge their own PR without a separate reviewer.

Default to **Squash and merge**, with a useful final commit title, so each focused
PR becomes one readable change on `main`. Preserve individual commits with a
regular merge when the user requests it or their history has a clear purpose.
Do not merge with failed or unexamined required checks.

After an authorized merge, delete the merged remote task branch, switch to local
`main`, and update it with a fast-forward pull. Delete the local task branch only
after confirming its work was incorporated and it contains no additional work;
squash merges require checking the PR's merged state because commit ancestry
alone will not show that. Preserve any uncommitted changes.

## Completion report

State what changed, what validation ran, the branch and PR link when available,
and any remaining limitation. Distinguish local completion, an open PR, and a
merged change accurately.
