# Automatically move unblocked M2 tasks to Ready

This GitHub Actions workflow updates the **DFS - Task board** when an M2
task becomes unblocked. It is restricted to open M2 issues currently in
**Backlog**.

## One-time setup

1. Create a **fine-grained personal access token** with resource owner
   **DistributedFileSystem**. Grant organization permission **Projects:
   Read and write** and repository permission **Issues: Read** for
   **centralized-distributed-file-systems**. Your GitHub organization may need
   to approve the token. Do not post it in a PR or issue.
2. In repository **Settings → Secrets and variables → Actions**, create a
   **repository secret** named **DFS_PROJECTS_TOKEN** with that token as its
   value.
3. Merge the automation PR into the default branch **develop** after CI and
   review.
4. Open **Actions → Promote unblocked M2 tasks to Ready → Run workflow**,
   select **develop**, and run it once to update current eligible tasks.
   It runs automatically on issue close, reopen, edit, milestone assignment,
   and every hour afterward.

## Rules

- Only issues in this repository, with milestone **M2**, issue state **Open**,
  and project status **Backlog** can move.
- An issue must have a **## Phụ thuộc** or **## Dependencies** section containing
  one or more bullet lines formatted like **- Blocked by #10**.
- Every explicitly listed blocker must be closed as completed. A blocker
  closed as **not planned** does not count.
- No other status is changed. In progress, Testing, In review, Ready, Done,
  and all M1 / M3 / Stage 2 tasks are never touched.
- Issues without an explicit dependency list remain in Backlog for manual
  triage. The workflow does not add missing issues to the project.

Initially eligible examples: #17 (blocked by closed #10), #20 (blocked by
closed #6 and #8), #21 (blocked by closed #5), and #25 (blocked by closed
#9). Issue #18 remains in Backlog until #17 is completed.

## If the action fails

- Missing DFS_PROJECTS_TOKEN: add the repository secret.
- Cannot read organization Projects: verify the token's organization Projects
  read/write permissions and any required organization approval.
- Project not found: verify the project is titled **DFS - Task board** under
  the DistributedFileSystem organization.
- Missing Status field: project must have a single-select **Status** field
  with **Backlog** and **Ready** options.
- An issue is not moving: check its milestone, Backlog status, dependency
  bullet format, and whether every blocker is completed.

The script uses the standard GitHub GraphQL and REST APIs with no external
dependencies. It changes only the project Status field, not source code,
issue states, branches, or pull requests.
