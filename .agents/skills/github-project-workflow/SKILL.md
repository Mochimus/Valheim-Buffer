---
name: github-project-workflow
description: Update this Valheim mod repository on GitHub using the main branch, scoped area commits, and version tags. Use when preparing, committing, reverting, or releasing project changes to GitHub.
---

# GitHub Project Workflow

## Repository policy

- Remote: `https://github.com/Mochimus/Valheim-Buffer.git`.
- `main` is the integration branch. Keep project updates on `main`.
- Keep each work item atomic and make separate commits for distinct areas, using scopes such as `classes-combat`, `skills-vfx-animation`, and `xp-balance`. A commit should contain the changes for one scoped task or one agent area so it can be reviewed and reverted independently.
- Do not combine unrelated areas into one commit. If work crosses areas, split it into coordinated commits and record dependencies in the messages or project tracker.
- Use descriptive commit subjects, preferably Conventional Commit style, for example `fix(classes-combat): sync selected specialization in status panel`.
- Agent role names are responsibility boundaries; Git commits are the durable history. Do not create a separate long-lived branch per agent under this main-branch policy.

## Updating main

1. Inspect `git status`, current branch, recent commits, and remotes before editing Git history.
2. Confirm the intended files and area. Preserve unrelated user changes; never stage everything blindly when unrelated or generated files may be present.
3. Review the diff and stage only the scoped files. Keep build outputs, local editor state, logs, credentials, and machine-specific files out of commits.
4. Run only the build or checks requested by the user or required for the scoped change. Report what was and was not verified.
5. Fetch `origin` and inspect divergence before pushing. Commit scoped changes on `main`, then push with a normal fast-forward push.
6. Never force-push, reset shared history, or amend a commit that may already be shared. To undo a published change, use a new `git revert` commit scoped to that area.
7. Preserve existing remote content and history. If the local and remote histories are unrelated, inspect both and connect them with an explicit merge that retains both histories; do not replace remote history.

## Version tags

- Tag each user-designated release with an annotated semantic version tag: `vMAJOR.MINOR.PATCH` (for example, `v0.1.0`).
- A tag marks a release commit on `main`; it does not replace commits or branches. Push the tag after the release commit has been pushed.
- Do not move or reuse a published version tag. If a release needs correction, create a new patch version.
- Do not create release tags for ordinary work-in-progress commits unless the user explicitly identifies them as a version.
- In the release notes, summarize included area-scoped commits and the build/game validation performed.

## Reporting

After an update, report the pushed commit(s), their area scopes, any version tag created, and validation results. Update `PROJECT_STATUS.md` when the work changes goals, priorities, or domain status; update `README.md` when player-facing behavior or the manual test loop changes.
