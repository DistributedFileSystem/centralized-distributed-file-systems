# Automatic stage-gated Backlog → Ready

The **DFS - Task board** advances through milestones **M1 → M2 → M3 → S1 →
S2 → S3** in that order. Only the **first unfinished milestone** is active.
Within that milestone, open issues in Backlog move to Ready automatically
once all of their listed blockers are completed. Issues with **no blockers**
are immediately eligible. No task from a future milestone moves to Ready
until the preceding milestone has zero open issues.

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
4. Open **Actions → Promote unblocked active-stage tasks to Ready → Run workflow**,
   select **develop**, and run it once to update current eligible tasks.
   It runs automatically on issue close, reopen, edit, milestone assignment,
   and every hour afterward.

## Rules

- Read the repository's GitHub milestone issue counts. The active milestone
  is the first one that has open issues (or has no completed issues yet).
  All previous milestones must have **zero open issues** before advancing.
  Missing or duplicate milestone names stop the workflow safely.
- Stage names follow **M1, M2, M3, S1, S2, S3**.
- Only issues in this repository, in the **active milestone**, with issue
  state **Open** and project status **Backlog**, can move.
- An issue with **no listed blockers** can move immediately.
- For issues with a **## Phụ thuộc** or **## Dependencies** section, use bullet
  lines formatted like **- Blocked by #10**. Every listed blocker must be
  closed as completed. A blocker closed as **not planned** does not count.
- No other status is changed: In progress, Testing, In review, Ready, and Done
  retain their status, even if someone had moved a future-stage issue manually.
- The workflow only updates items already on the board. It does not add
  issues to the project or close/open issues.

Current stage: M1 is complete, so M2 is active. Eligible examples include
#17 (blocked by closed #10), #20 (blocked by closed #6 and #8), #21 (blocked
by closed #5), and #25 (blocked by closed #9). Issue #18 remains in Backlog
until #17 is completed. Once **all M2 issues are closed**, the next execution
automatically unlocks M3, and the same rule repeats through S3.

## If the action fails

- Missing DFS_PROJECTS_TOKEN: add the repository secret.
- Cannot read organization Projects: verify the token's organization Projects
  read/write permissions and any required organization approval.
- Project not found: verify the project is titled **DFS - Task board** under
  the DistributedFileSystem organization.
- Missing Status field: project must have a single-select **Status** field
  with **Backlog** and **Ready** options.
- An issue is not moving: check its milestone, whether that milestone is the
  first unfinished stage, its Backlog status, its dependency bullet format,
  and whether every listed blocker is completed.
- Stage doesn't advance: every issue in the current milestone must be closed.
  Issues marked "Done" only on the project board but still **Open** on GitHub
  will prevent the next stage from unlocking.

The script uses the standard GitHub GraphQL and REST APIs with no external
dependencies. It changes only the project Status field, not source code,
issue states, branches, or pull requests.
