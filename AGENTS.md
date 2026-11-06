# Map Clash project instructions

At the start of each new chat or task, the primary agent reads all Markdown
files recursively under `D:\VIBECODE\GlobalRules` before proceeding. These are
the shared rules for Map Clash; reread them in each new chat so changes to the
shared files take effect. Currently they include `AGENTS.md`, `code-style.md`,
and `README.md`.

When delegation is authorized, follow the lean-orchestration skill. The primary
agent passes each subagent only the relevant rules, specifications, and exact
file boundaries for its task. Subagents do not independently reread the entire
shared rules directory or broad project documentation; they inspect only the
files needed for their assigned work and any instructions they are required to
follow. This exception does not remove the primary agent's obligation above.

Apply the shared rules within the scope and priority of the active system,
developer, and user instructions. Preserve existing project conventions and
any more specific project instructions. If a referenced file or pipeline is
missing or inaccessible, report that when relevant; do not invent its contents.

## Git workflow

The project's GitHub repository is `https://github.com/X-Aler/Map-Clash`.
It was renamed from `X-Aler/My-project` on 2026-11-06; these are the same
repository. Verify the local remote before publishing.

The user authorized autonomous local commits on 2026-11-06. After a coherent
change is implemented and checked, create a focused commit without asking the
user to approve the commit or write its message. Also commit when explicitly
requested. Choose a descriptive conventional commit message and report the
commit hash, scope, and verification results.

Stage only files belonging to the completed change. Preserve unrelated user
work; do not bundle the existing uncommitted project backlog into a new commit
without a separate instruction to capture it.

Before any push, present the commits and their changes for the user's review
and wait for their explicit approval to push. Local commit authorization does
not authorize pushing, force-pushing, or rewriting published history.
