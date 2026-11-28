"""Tests for Issue #35 ([M3-02] Deploy 5 laptop: node.yml, env, firewall, smoke test).

Standard-library unittest suite covering Acceptance Criteria AC-1 through AC-6.
"""

from __future__ import annotations

import os
import re
import socket
import subprocess
import threading
import unittest
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parents[2]
DEPLOY_DIR = REPO_ROOT / "deploy"
NODE_YML = DEPLOY_DIR / "node.yml"
ENV_DIR = DEPLOY_DIR / "env"
SCRIPTS_DIR = DEPLOY_DIR / "scripts"
OPEN_FW_SH = SCRIPTS_DIR / "open-firewall.sh"
OPEN_FW_PS1 = SCRIPTS_DIR / "open-firewall.ps1"
SMOKE_SH = SCRIPTS_DIR / "smoke-test.sh"
SMOKE_PS1 = SCRIPTS_DIR / "smoke-test.ps1"
DEPLOYMENT_DOC = REPO_ROOT / "docs" / "deployment.md"

EXPECTED_STAGE1_ENDPOINTS = [
    "192.168.10.11:7000",
    "192.168.10.11:7001",
    "192.168.10.12:7001",
    "192.168.10.13:7001",
    "192.168.10.14:7001",
    "192.168.10.15:7001",
]


def _strip_yaml_comment(line: str) -> str:
    """Remove trailing YAML comments outside single/double quotes."""
    in_single = False
    in_double = False
    for idx, ch in enumerate(line):
        if ch == "'" and not in_double:
            in_single = not in_single
        elif ch == '"' and not in_single:
            in_double = not in_double
        elif ch == "#" and not in_single and not in_double:
            if idx == 0 or line[idx - 1].isspace():
                return line[:idx].rstrip()
    return line.rstrip()


def _parse_yaml_scalar(raw: str) -> Any:
    val = raw.strip()
    if val == "":
        return None
    if val.startswith("[") and val.endswith("]"):
        inner = val[1:-1].strip()
        if not inner:
            return []
        items: list[Any] = []
        current: list[str] = []
        in_s = False
        in_d = False
        for ch in inner:
            if ch == "'" and not in_d:
                in_s = not in_s
                current.append(ch)
            elif ch == '"' and not in_s:
                in_d = not in_d
                current.append(ch)
            elif ch == "," and not in_s and not in_d:
                items.append(_parse_yaml_scalar("".join(current)))
                current = []
            else:
                current.append(ch)
        if current:
            items.append(_parse_yaml_scalar("".join(current)))
        return items
    if (val.startswith('"') and val.endswith('"')) or (
        val.startswith("'") and val.endswith("'")
    ):
        return val[1:-1]
    if val.lower() == "true":
        return True
    if val.lower() == "false":
        return False
    return val


def _simple_yaml_load(text: str) -> dict[str, Any]:
    """Minimal stdlib YAML loader for Docker Compose files (mappings, lists, anchors/merges)."""
    raw_lines: list[tuple[int, str]] = []
    for raw_line in text.splitlines():
        cleaned = _strip_yaml_comment(raw_line)
        if not cleaned.strip():
            continue
        indent = len(cleaned) - len(cleaned.lstrip(" "))
        raw_lines.append((indent, cleaned.strip()))

    anchors: dict[str, Any] = {}

    def parse_block(idx: int, base_indent: int) -> tuple[Any, int]:
        if idx >= len(raw_lines):
            return {}, idx
        _, content = raw_lines[idx]
        if content.startswith("- ") or content == "-":
            result_list: list[Any] = []
            while idx < len(raw_lines):
                c_indent, c_content = raw_lines[idx]
                if c_indent < base_indent:
                    break
                if c_indent > base_indent or not (
                    c_content.startswith("- ") or c_content == "-"
                ):
                    break
                item_text = c_content[2:].strip() if c_content.startswith("- ") else ""
                idx += 1
                if not item_text:
                    if idx < len(raw_lines) and raw_lines[idx][0] > c_indent:
                        nested, idx = parse_block(idx, raw_lines[idx][0])
                        result_list.append(nested)
                    else:
                        result_list.append(None)
                else:
                    result_list.append(_parse_yaml_scalar(item_text))
            return result_list, idx

        result_dict: dict[str, Any] = {}
        while idx < len(raw_lines):
            c_indent, c_content = raw_lines[idx]
            if c_indent != base_indent or c_content.startswith("- "):
                break
            if ":" not in c_content:
                idx += 1
                continue
            key, rest = c_content.split(":", 1)
            key = key.strip().strip('"').strip("'")
            rest = rest.strip()
            idx += 1

            anchor_name: str | None = None
            if rest.startswith("&"):
                parts = rest.split(None, 1)
                anchor_name = parts[0][1:]
                rest = parts[1].strip() if len(parts) > 1 else ""

            if key == "<<" and rest.startswith("*"):
                ref_name = rest[1:].strip()
                ref_val = anchors.get(ref_name, {})
                if isinstance(ref_val, dict):
                    for rk, rv in ref_val.items():
                        result_dict.setdefault(rk, rv)
                continue

            if rest == "":
                if idx < len(raw_lines) and raw_lines[idx][0] > c_indent:
                    val, idx = parse_block(idx, raw_lines[idx][0])
                else:
                    val = None
            elif rest.startswith("*"):
                val = anchors.get(rest[1:].strip())
            else:
                val = _parse_yaml_scalar(rest)

            if anchor_name is not None:
                anchors[anchor_name] = val
            result_dict[key] = val
        return result_dict, idx

    parsed, _ = parse_block(0, raw_lines[0][0] if raw_lines else 0)
    if not isinstance(parsed, dict):
        raise ValueError("Top-level YAML document must be a mapping")
    return parsed


def load_yaml_file(path: Path) -> dict[str, Any]:
    text = path.read_text(encoding="utf-8")
    try:
        import yaml  # type: ignore

        data = yaml.safe_load(text)
        if not isinstance(data, dict):
            raise ValueError("YAML root must be a mapping")
        return data
    except ImportError:
        return _simple_yaml_load(text)


def parse_env_file(path: Path) -> dict[str, str]:
    env: dict[str, str] = {}
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, val = line.split("=", 1)
        key = key.strip()
        val = val.strip()
        if (val.startswith('"') and val.endswith('"')) or (
            val.startswith("'") and val.endswith("'")
        ):
            val = val[1:-1]
        env[key] = val
    return env


def normalize_compose_env(env_block: Any) -> dict[str, str]:
    if isinstance(env_block, dict):
        return {str(k): "" if v is None else str(v) for k, v in env_block.items()}
    if isinstance(env_block, list):
        out: dict[str, str] = {}
        for item in env_block:
            s = str(item)
            if "=" in s:
                k, v = s.split("=", 1)
                out[k.strip()] = v.strip()
            else:
                out[s.strip()] = ""
        return out
    return {}


def is_placeholder_secret(value: str) -> bool:
    lower = value.lower()
    markers = ("replace", "change", "placeholder", "example", "your-", "your_", "todo", "<")
    return bool(value) and any(m in lower for m in markers)


class _TempTcpServer:
    """Ephemeral localhost TCP server for live smoke-test verification."""

    def __init__(self) -> None:
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self._sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self._sock.bind(("127.0.0.1", 0))
        self._sock.listen(5)
        self._sock.settimeout(0.2)
        self.port: int = self._sock.getsockname()[1]
        self._stop = threading.Event()
        self._thread = threading.Thread(target=self._serve, daemon=True)
        self._thread.start()

    def _serve(self) -> None:
        while not self._stop.is_set():
            try:
                conn, _ = self._sock.accept()
                conn.close()
            except socket.timeout:
                continue
            except OSError:
                break

    def close(self) -> None:
        self._stop.set()
        try:
            self._sock.close()
        except OSError:
            pass
        self._thread.join(timeout=1.0)


def _get_closed_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.bind(("127.0.0.1", 0))
        return s.getsockname()[1]


class TestNodeComposeFile(unittest.TestCase):
    """AC-1: deploy/node.yml structure, local build, ports, security, env, volumes, healthchecks."""

    def setUp(self) -> None:
        if not NODE_YML.is_file():
            self.fail(f"Missing required compose file: {NODE_YML}")
        self.raw_text = NODE_YML.read_text(encoding="utf-8")
        self.doc = load_yaml_file(NODE_YML)
        self.services = self.doc.get("services") or {}

    def test_services_metadata_and_data_exist_with_profiles(self) -> None:
        self.assertIn("metadata", self.services, "Service 'metadata' must be defined")
        self.assertIn("data", self.services, "Service 'data' must be defined")
        self.assertIn("metadata", self.services["metadata"].get("profiles") or [])
        self.assertIn("data", self.services["data"].get("profiles") or [])

    def test_local_build_config_and_no_prebuilt_image(self) -> None:
        for name in ("metadata", "data"):
            svc = self.services[name]
            self.assertNotIn("image", svc, f"Service '{name}' must build locally, not pull a prebuilt image")
            build = svc.get("build")
            self.assertIsInstance(build, dict, f"Service '{name}' must define a 'build' mapping")
            self.assertEqual(build.get("context"), "..", f"Service '{name}' build.context must be '..'")
            self.assertEqual(
                build.get("dockerfile"),
                "deploy/Dockerfile",
                f"Service '{name}' build.dockerfile must be 'deploy/Dockerfile'",
            )

    def test_ports_published_and_no_host_network_mode(self) -> None:
        self.assertNotRegex(
            self.raw_text,
            r'network_mode\s*:\s*["\']?host["\']?',
            "deploy/node.yml must NOT use network_mode: host (Windows Docker Desktop rule)",
        )
        for name in ("metadata", "data"):
            svc = self.services[name]
            self.assertNotEqual(svc.get("network_mode"), "host")

        meta_ports = [str(p) for p in (self.services["metadata"].get("ports") or [])]
        data_ports = [str(p) for p in (self.services["data"].get("ports") or [])]
        self.assertTrue(
            any(
                "BIND_HOST:-0.0.0.0" in p and "METADATA_LISTEN_PORT:-7000" in p
                for p in meta_ports
            ),
            f"metadata ports must publish BIND_HOST:-0.0.0.0 and METADATA_LISTEN_PORT:-7000, got: {meta_ports}",
        )
        self.assertTrue(
            any(
                "BIND_HOST:-0.0.0.0" in p and "DATA_LISTEN_PORT:-7001" in p
                for p in data_ports
            ),
            f"data ports must publish BIND_HOST:-0.0.0.0 and DATA_LISTEN_PORT:-7001, got: {data_ports}",
        )

    def test_restart_policy_volumes_and_healthchecks(self) -> None:
        top_volumes = self.doc.get("volumes") or {}
        self.assertIn("metadata-data", top_volumes)
        self.assertIn("data-data", top_volumes)

        expected_vol = {"metadata": "metadata-data:/data", "data": "data-data:/data"}
        for name, mount in expected_vol.items():
            svc = self.services[name]
            self.assertEqual(
                svc.get("restart"),
                "unless-stopped",
                f"Service '{name}' must set restart: unless-stopped",
            )
            vols = [str(v) for v in (svc.get("volumes") or [])]
            self.assertIn(mount, vols, f"Service '{name}' must mount '{mount}'")
            hc = svc.get("healthcheck")
            self.assertIsInstance(hc, dict, f"Service '{name}' must define a healthcheck")
            hc_test = hc.get("test")
            self.assertTrue(hc_test, f"Service '{name}' healthcheck must define 'test'")
            hc_str = " ".join(str(x) for x in hc_test) if isinstance(hc_test, list) else str(hc_test)
            self.assertIn("/dev/tcp/", hc_str, f"Service '{name}' healthcheck should perform a TCP check")

    def test_metadata_environment_variables(self) -> None:
        env = normalize_compose_env(self.services["metadata"].get("environment"))
        self.assertEqual(env.get("NODE_ROLE"), "metadata")
        self.assertIn("METADATA_NODE_ID:-m1", env.get("NODE_ID", ""))
        self.assertIn("METADATA_LISTEN_PORT:-7000", env.get("LISTEN_PORT", ""))
        self.assertEqual(env.get("DATA_DIR"), "/data")

        for req_var in ("ADVERTISE_HOST", "METADATA_ENDPOINTS", "TOKEN_KEY"):
            self.assertIn(req_var, env, f"metadata env must define {req_var}")
            pattern = re.escape("${" + req_var) + r":?\?"
            self.assertRegex(
                env[req_var],
                pattern,
                f"metadata {req_var} must be required, got: {env[req_var]!r}",
            )

        for opt_var in ("ADMIN_USER", "ADMIN_PASSWORD", "KEEP_VERSIONS", "SNAPSHOT_INTERVAL_OPS"):
            self.assertIn(opt_var, env, f"metadata env must define {opt_var}")

    def test_data_environment_variables(self) -> None:
        env = normalize_compose_env(self.services["data"].get("environment"))
        self.assertEqual(env.get("NODE_ROLE"), "data")
        self.assertIn("DATA_NODE_ID:-ds-1", env.get("NODE_ID", ""))
        self.assertIn("DATA_LISTEN_PORT:-7001", env.get("LISTEN_PORT", ""))
        self.assertEqual(env.get("DATA_DIR"), "/data")

        for req_var in ("ADVERTISE_HOST", "METADATA_ENDPOINTS", "TOKEN_KEY"):
            self.assertIn(req_var, env, f"data env must define {req_var}")
            pattern = re.escape("${" + req_var) + r":?\?"
            self.assertRegex(
                env[req_var],
                pattern,
                f"data {req_var} must be required, got: {env[req_var]!r}",
            )

        self.assertIn("CAPACITY_BYTES", env, "data env must define CAPACITY_BYTES")


class TestLaptopExampleEnvFiles(unittest.TestCase):
    """AC-2: deploy/env/laptop1..5.example.env values, uniqueness, no real secrets, .gitignore."""

    def _load_all_envs(self) -> dict[int, dict[str, str]]:
        envs: dict[int, dict[str, str]] = {}
        for idx in range(1, 6):
            path = ENV_DIR / f"laptop{idx}.example.env"
            self.assertTrue(path.is_file(), f"Missing env template: {path}")
            envs[idx] = parse_env_file(path)
        return envs

    def test_laptop1_example_env(self) -> None:
        env = self._load_all_envs()[1]
        profiles = {p.strip() for p in env.get("COMPOSE_PROFILES", "").split(",") if p.strip()}
        self.assertEqual(profiles, {"metadata", "data"})
        self.assertEqual(env.get("ADVERTISE_HOST"), "192.168.10.11")
        self.assertEqual(env.get("METADATA_NODE_ID"), "m1")
        self.assertEqual(env.get("DATA_NODE_ID"), "ds-1")
        self.assertEqual(env.get("METADATA_ENDPOINTS"), "192.168.10.11:7000")
        self.assertEqual(env.get("ADMIN_USER"), "admin")
        self.assertEqual(env.get("KEEP_VERSIONS"), "5")
        self.assertEqual(env.get("SNAPSHOT_INTERVAL_OPS"), "100")
        self.assertTrue(
            is_placeholder_secret(env.get("TOKEN_KEY", "")),
            f"laptop1 TOKEN_KEY must be a non-secret placeholder, got: {env.get('TOKEN_KEY')!r}",
        )
        self.assertTrue(
            is_placeholder_secret(env.get("ADMIN_PASSWORD", "")),
            f"laptop1 ADMIN_PASSWORD must be a non-secret placeholder, got: {env.get('ADMIN_PASSWORD')!r}",
        )

    def test_laptop2_to_laptop5_example_envs(self) -> None:
        envs = self._load_all_envs()
        for idx in range(2, 6):
            env = envs[idx]
            profiles = {p.strip() for p in env.get("COMPOSE_PROFILES", "").split(",") if p.strip()}
            self.assertEqual(profiles, {"data"}, f"laptop{idx} must only enable 'data' profile")
            self.assertEqual(env.get("ADVERTISE_HOST"), f"192.168.10.1{idx}")
            self.assertEqual(env.get("DATA_NODE_ID"), f"ds-{idx}")
            self.assertEqual(env.get("METADATA_ENDPOINTS"), "192.168.10.11:7000")
            self.assertTrue(
                is_placeholder_secret(env.get("TOKEN_KEY", "")),
                f"laptop{idx} TOKEN_KEY must be a non-secret placeholder, got: {env.get('TOKEN_KEY')!r}",
            )

    def test_unique_node_ids_and_advertise_hosts(self) -> None:
        envs = self._load_all_envs()
        data_ids = [envs[i].get("DATA_NODE_ID") for i in range(1, 6)]
        hosts = [envs[i].get("ADVERTISE_HOST") for i in range(1, 6)]
        self.assertEqual(len(set(data_ids)), 5, f"DATA_NODE_ID values must be unique: {data_ids}")
        self.assertEqual(len(set(hosts)), 5, f"ADVERTISE_HOST values must be unique: {hosts}")

    def test_gitignore_ignores_real_env_and_allows_example_env(self) -> None:
        for idx in range(1, 6):
            real_rel = f"deploy/env/laptop{idx}.env"
            example_rel = f"deploy/env/laptop{idx}.example.env"
            res_real = subprocess.run(
                ["git", "check-ignore", real_rel],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
            )
            self.assertEqual(res_real.returncode, 0, f"{real_rel} must be ignored by .gitignore")

            res_example = subprocess.run(
                ["git", "check-ignore", example_rel],
                cwd=REPO_ROOT,
                capture_output=True,
                text=True,
            )
            self.assertNotEqual(res_example.returncode, 0, f"{example_rel} must NOT be ignored by .gitignore")


class TestOpenFirewallScripts(unittest.TestCase):
    """AC-3 & AC-4: deploy/scripts/open-firewall.sh and open-firewall.ps1."""

    def test_open_firewall_sh_syntax_and_executable(self) -> None:
        self.assertTrue(OPEN_FW_SH.is_file(), f"Missing script: {OPEN_FW_SH}")
        self.assertTrue(os.access(OPEN_FW_SH, os.X_OK), f"{OPEN_FW_SH} must be executable")
        res = subprocess.run(["bash", "-n", str(OPEN_FW_SH)], capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, f"bash -n failed: {res.stderr}")

    def test_open_firewall_sh_help_and_dry_run_modes(self) -> None:
        self.assertTrue(OPEN_FW_SH.is_file(), f"Missing script: {OPEN_FW_SH}")

        res_help = subprocess.run([str(OPEN_FW_SH), "--help"], capture_output=True, text=True)
        self.assertEqual(res_help.returncode, 0, f"--help failed: {res_help.stderr}")
        self.assertIn("--ports", res_help.stdout + res_help.stderr)

        res_default = subprocess.run([str(OPEN_FW_SH), "--dry-run"], capture_output=True, text=True)
        self.assertEqual(res_default.returncode, 0, f"--dry-run failed: {res_default.stderr}")
        out_default = res_default.stdout + res_default.stderr
        self.assertIn("7000", out_default)
        self.assertIn("7001", out_default)
        self.assertIn("ufw", out_default.lower())

        res_custom = subprocess.run(
            [str(OPEN_FW_SH), "--dry-run", "--ports", "7000,7001,7002"],
            capture_output=True,
            text=True,
        )
        self.assertEqual(res_custom.returncode, 0, f"--dry-run --ports failed: {res_custom.stderr}")
        out_custom = res_custom.stdout + res_custom.stderr
        for port in ("7000", "7001", "7002"):
            self.assertIn(port, out_custom)

        res_remove = subprocess.run(
            [str(OPEN_FW_SH), "--dry-run", "--remove"],
            capture_output=True,
            text=True,
        )
        self.assertEqual(res_remove.returncode, 0, f"--dry-run --remove failed: {res_remove.stderr}")
        out_remove = (res_remove.stdout + res_remove.stderr).lower()
        self.assertTrue(
            "delete" in out_remove or "remove" in out_remove,
            f"Expected delete/remove in output: {out_remove}",
        )
        self.assertIn("7000", out_remove)
        self.assertIn("7001", out_remove)

    def test_open_firewall_sh_rejects_invalid_args_and_ports(self) -> None:
        self.assertTrue(OPEN_FW_SH.is_file(), f"Missing script: {OPEN_FW_SH}")

        res_flag = subprocess.run([str(OPEN_FW_SH), "--invalid-option"], capture_output=True, text=True)
        self.assertNotEqual(res_flag.returncode, 0, "Unknown flag must exit non-zero")

        res_port = subprocess.run(
            [str(OPEN_FW_SH), "--dry-run", "--ports", "not-a-port"],
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(res_port.returncode, 0, "Invalid port must exit non-zero")

    def test_open_firewall_ps1_content(self) -> None:
        self.assertTrue(OPEN_FW_PS1.is_file(), f"Missing script: {OPEN_FW_PS1}")
        content = OPEN_FW_PS1.read_text(encoding="utf-8")
        self.assertIn("SupportsShouldProcess", content)
        self.assertIn("New-NetFirewallRule", content)
        self.assertRegex(content, r'["\']DFS["\']')
        self.assertIn("7000", content)
        self.assertIn("7001", content)
        self.assertRegex(content, r"\$Ports\b")
        self.assertRegex(content, r"\$Remove\b")
        self.assertRegex(
            content,
            r"(Administrator|RunAsAdministrator|WindowsPrincipal|IsInRole)",
            "open-firewall.ps1 must check for Administrator privileges",
        )


class TestSmokeTestScripts(unittest.TestCase):
    """AC-5: deploy/scripts/smoke-test.sh and smoke-test.ps1."""

    def test_smoke_test_sh_syntax_executable_and_defaults(self) -> None:
        self.assertTrue(SMOKE_SH.is_file(), f"Missing script: {SMOKE_SH}")
        self.assertTrue(os.access(SMOKE_SH, os.X_OK), f"{SMOKE_SH} must be executable")
        res = subprocess.run(["bash", "-n", str(SMOKE_SH)], capture_output=True, text=True)
        self.assertEqual(res.returncode, 0, f"bash -n failed: {res.stderr}")

        content = SMOKE_SH.read_text(encoding="utf-8")
        for endpoint in EXPECTED_STAGE1_ENDPOINTS:
            self.assertIn(endpoint, content, f"smoke-test.sh should include default Stage 1 endpoint {endpoint}")

        res_help = subprocess.run([str(SMOKE_SH), "--help"], capture_output=True, text=True)
        self.assertEqual(res_help.returncode, 0, f"--help failed: {res_help.stderr}")

    def test_smoke_test_sh_live_tcp_all_up_and_one_down(self) -> None:
        self.assertTrue(SMOKE_SH.is_file(), f"Missing script: {SMOKE_SH}")
        srv1 = _TempTcpServer()
        srv2 = _TempTcpServer()
        try:
            targets_up = f"127.0.0.1:{srv1.port},127.0.0.1:{srv2.port}"
            res_up = subprocess.run(
                [str(SMOKE_SH), "--targets", targets_up, "--timeout", "2"],
                capture_output=True,
                text=True,
                timeout=15,
            )
            combined_up = res_up.stdout + res_up.stderr
            self.assertEqual(res_up.returncode, 0, f"Expected exit 0 when all targets are up; output:\n{combined_up}")
            self.assertIn("PASS", combined_up)
            self.assertNotIn("FAIL", combined_up)

            closed_port = _get_closed_port()
            targets_partial = f"127.0.0.1:{srv1.port},127.0.0.1:{closed_port}"
            res_down = subprocess.run(
                [str(SMOKE_SH), "--targets", targets_partial, "--timeout", "1"],
                capture_output=True,
                text=True,
                timeout=15,
            )
            combined_down = res_down.stdout + res_down.stderr
            self.assertEqual(res_down.returncode, 1, f"Expected exit 1 when a target is down; output:\n{combined_down}")
            self.assertIn("PASS", combined_down)
            self.assertIn("FAIL", combined_down)
        finally:
            srv1.close()
            srv2.close()

    def test_smoke_test_sh_rejects_invalid_args(self) -> None:
        self.assertTrue(SMOKE_SH.is_file(), f"Missing script: {SMOKE_SH}")
        res = subprocess.run([str(SMOKE_SH), "--unknown-flag"], capture_output=True, text=True)
        self.assertNotEqual(res.returncode, 0, "Invalid argument must exit non-zero")

    def test_smoke_test_ps1_content(self) -> None:
        self.assertTrue(SMOKE_PS1.is_file(), f"Missing script: {SMOKE_PS1}")
        content = SMOKE_PS1.read_text(encoding="utf-8")
        self.assertIn("TcpClient", content)
        self.assertRegex(content, r"\$Targets\b")
        self.assertRegex(content, r"\$TimeoutSec\b")
        for endpoint in EXPECTED_STAGE1_ENDPOINTS:
            self.assertIn(endpoint, content, f"smoke-test.ps1 must include default Stage 1 endpoint {endpoint}")


class TestDeploymentDocumentation(unittest.TestCase):
    """AC-6: docs/deployment.md documents node.yml, laptop env templates, firewall, and smoke scripts."""

    def test_deployment_md_references_all_m3_02_artifacts(self) -> None:
        self.assertTrue(DEPLOYMENT_DOC.is_file(), f"Missing doc: {DEPLOYMENT_DOC}")
        content = DEPLOYMENT_DOC.read_text(encoding="utf-8")
        self.assertIn("deploy/node.yml", content)
        self.assertRegex(
            content,
            r"laptop(1|N)\.example\.env",
            "docs/deployment.md must reference laptop1.example.env or laptopN.example.env",
        )
        self.assertIn("open-firewall.sh", content)
        self.assertIn("open-firewall.ps1", content)
        self.assertIn("smoke-test.sh", content)
        self.assertIn("smoke-test.ps1", content)



    def test_octal_leading_zero_and_missing_arg_rejected(self):
        fw = REPO_ROOT / "deploy" / "scripts" / "open-firewall.sh"
        sm = REPO_ROOT / "deploy" / "scripts" / "smoke-test.sh"
        for args in [
            [str(fw), "--ports", "0800", "--dry-run"],
            [str(fw), "--ports"],
            [str(fw), "--dry-run", "--ports", "0"],
            [str(fw), "--dry-run", "--ports", "70000"],
            [str(sm), "--timeout", "08", "--targets", "127.0.0.1:7000"],
            [str(sm), "--targets", "127.0.0.1:0800"],
            [str(sm), "--targets"],
            [str(sm), "--timeout"],
        ]:
            proc = subprocess.run(args, capture_output=True, text=True)
            self.assertNotEqual(proc.returncode, 0, f"Expected failure for {args}, got stdout={proc.stdout!r}")

    def test_all_required_compose_vars_present_in_all_laptop_envs(self):
        import re as _re
        node_text = (REPO_ROOT / "deploy" / "node.yml").read_text(encoding="utf-8")
        required_vars = set(_re.findall(r"\$\{([A-Z0-9_]+):\?[^}]*\}", node_text))
        self.assertTrue(required_vars, "Expected required vars in deploy/node.yml")
        for n in range(1, 6):
            env_path = REPO_ROOT / "deploy" / "env" / f"laptop{n}.example.env"
            env_map = {}
            for line in env_path.read_text(encoding="utf-8").splitlines():
                line = line.strip()
                if not line or line.startswith("#") or "=" not in line:
                    continue
                k, v = line.split("=", 1)
                env_map[k.strip()] = v.strip()
            for var in required_vars:
                self.assertTrue(env_map.get(var), f"Missing required {var} in {env_path.name}")


if __name__ == "__main__":
    unittest.main()
