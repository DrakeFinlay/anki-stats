# Repository instructions

Before responding to any task in this repository, read and follow
[the development workflow](.docs/development-workflow.md). It applies to features,
bug fixes, refactors, tests, documentation, and repository configuration changes.
For questions and read-only investigations, read the workflow but do not create a
branch unless changes are needed.

For implementation tasks, create a short-lived `codex/<descriptive-name>` branch
before editing. Continue the existing task branch for follow-ups to the same work.
Never implement directly on `main`. Preserve unrelated user work.

Put all project documentation in `.docs/`, including user guides, design documents,
plans, research notes, and technical explanations. The root `README.md`, `AGENTS.md`,
and third-party license/notice files are exceptions; existing third-party materials
may also remain in `LICENSES/`. Any other documentation file in the repository root
requires explicit user approval before creating or moving it there. When moving
documentation, update relative links and the documented repository structure.

Follow the workflow's validation and pull request steps. Do not merge a pull
request or push directly to `main` without an explicit user instruction. A newer,
explicit user instruction takes precedence over these repository defaults.
