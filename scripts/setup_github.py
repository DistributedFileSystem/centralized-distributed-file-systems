#!/usr/bin/env python3
"""Tạo labels, milestones, issues và GitHub Project cho repo.

Yêu cầu: GitHub CLI (`gh`) đã đăng nhập bằng tài khoản owner của organization,
có scope `project`:  gh auth refresh -s project

Chạy:
    python scripts/setup_github.py                      # dùng mặc định bên dưới
    python scripts/setup_github.py --dry-run            # chỉ in ra, không tạo gì
    python scripts/setup_github.py --skip-project       # chỉ tạo labels / milestones / issues

Chạy lại nhiều lần được: label / milestone / issue đã có (theo tên) sẽ được bỏ qua.
"""
import argparse
import json
import pathlib
import subprocess
import sys

OWNER = "DistributedFileSystem"
REPO = "centralized-distributed-file-systems"
PROJECT_TITLE = "DFS - Task board"
BACKLOG = pathlib.Path(__file__).with_name("backlog.json")

DRY = False


def gh(*args, capture=True):
    cmd = ["gh", *args]
    if DRY:
        print("[dry-run]", " ".join(cmd))
        return ""
    res = subprocess.run(cmd, capture_output=capture, text=True)
    if res.returncode != 0:
        sys.exit(f"Lỗi khi chạy: {' '.join(cmd)}\n{res.stderr}")
    return res.stdout


def gh_json(*args):
    out = gh(*args)
    return json.loads(out) if out else []


def body_for(issue, numbers):
    lines = ["## Mục tiêu", issue["goal"], "", "## Acceptance criteria"]
    lines += [f"- [ ] {a}" for a in issue["ac"]]
    deps = [f"#{numbers[d]}" for d in issue.get("deps", []) if d in numbers]
    if deps:
        lines += ["", "## Phụ thuộc", "Blocked by " + ", ".join(deps)]
    if issue.get("docs"):
        lines += ["", "## Tài liệu", f"[{issue['docs']}](../blob/develop/{issue['docs']})"]
    return "\n".join(lines)


def main():
    global DRY
    p = argparse.ArgumentParser()
    p.add_argument("--owner", default=OWNER)
    p.add_argument("--repo", default=REPO)
    p.add_argument("--dry-run", action="store_true")
    p.add_argument("--skip-project", action="store_true")
    a = p.parse_args()
    DRY = a.dry_run
    full = f"{a.owner}/{a.repo}"
    data = json.loads(BACKLOG.read_text(encoding="utf-8"))

    print("== Labels")
    for lb in data["labels"]:
        gh("label", "create", lb["name"], "--repo", full, "--color", lb["color"],
           "--description", lb["description"], "--force")

    print("== Milestones")
    existing = {m["title"] for m in gh_json("api", f"repos/{full}/milestones?state=all&per_page=100")}
    titles = {}
    for ms in data["milestones"]:
        titles[ms["key"]] = ms["title"]
        if ms["title"] in existing:
            continue
        gh("api", f"repos/{full}/milestones", "-f", f"title={ms['title']}",
           "-f", f"description={ms['description']}")

    print("== Issues")
    current = {i["title"]: i["number"] for i in
               gh_json("issue", "list", "--repo", full, "--state", "all", "--limit", "500",
                       "--json", "title,number")}
    numbers, urls = {}, []
    for issue in data["issues"]:
        labels = list(issue["labels"])
        if not any(l.startswith("stage:") for l in labels):
            labels.append("stage:1")
        if issue["title"] in current:
            numbers[issue["id"]] = current[issue["title"]]
            continue
        out = gh("issue", "create", "--repo", full, "--title", issue["title"],
                 "--body", body_for(issue, {}), "--milestone", titles[issue["m"]],
                 "--label", ",".join(labels)).strip()
        if out:
            numbers[issue["id"]] = int(out.rstrip("/").split("/")[-1])
            urls.append(out)
        print("  +", issue["title"])

    print("== Điền phụ thuộc (Blocked by #)")
    for issue in data["issues"]:
        if issue.get("deps") and issue["id"] in numbers:
            gh("issue", "edit", str(numbers[issue["id"]]), "--repo", full,
               "--body", body_for(issue, numbers))

    if a.skip_project:
        return
    print("== GitHub Project")
    projects = gh_json("project", "list", "--owner", a.owner, "--format", "json")
    proj = next((x for x in (projects or {}).get("projects", []) if x["title"] == PROJECT_TITLE), None) \
        if isinstance(projects, dict) else None
    if proj is None:
        proj = gh_json("project", "create", "--owner", a.owner, "--title", PROJECT_TITLE,
                       "--format", "json")
    num = str(proj["number"]) if proj else "0"
    gh("project", "link", num, "--owner", a.owner, "--repo", full)
    for n in numbers.values():
        gh("project", "item-add", num, "--owner", a.owner,
           "--url", f"https://github.com/{full}/issues/{n}")
    print(f"Xong. Project #{num}: https://github.com/orgs/{a.owner}/projects/{num}")
    print("Bước tay: trong Project → Settings → Status, đổi cột thành "
          "Backlog, Ready, In progress, In review, Done; bật workflow 'Item closed → Done' "
          "và 'Pull request linked → In review'.")


if __name__ == "__main__":
    main()
