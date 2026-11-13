"""Local integration checks. Never invokes the live server's start/stop actions."""

import ctypes as ct
from ctypes import wintypes as wt
import importlib.machinery
import importlib.util
import json
from pathlib import Path
import queue
import struct
import sys
import tempfile
import time
import types
import unittest
from unittest.mock import Mock, patch


ROOT = Path(__file__).resolve().parent
loader = importlib.machinery.SourceFileLoader("charac_gui", str(ROOT / "charac_gui.pyw"))
spec = importlib.util.spec_from_loader(loader.name, loader)
gui = importlib.util.module_from_spec(spec)
loader.exec_module(gui)


def helper(folder):
    config = {"root": str(folder), "socket": str(folder / "socket"), "upload": str(folder / "uploads"),
              "pane": "mc120:0.0", "session": "mc120", "port": 25566,
              "forge": gui.FORGE_ARGS, "group": "minecraft-maintainer", "start_timeout": 1, "stop_timeout": 1}
    namespace = {"config": config, "destination": "config", "names": []}
    with patch.dict(sys.modules, {"grp": types.SimpleNamespace(getgrnam=lambda name: None)}):
        exec(gui.REMOTE_HELPER.split("\nresult = ")[0], namespace)
    return namespace


class RemoteActions(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(dir=ROOT)
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        (self.folder / "run.sh").write_text("true\n", encoding="ascii")
        (self.folder / "socket").touch()
        self.ns = helper(self.folder)
        self.ns["os"] = types.SimpleNamespace(geteuid=lambda: 99999)

    def test_start_does_not_duplicate_an_existing_server(self):
        state = {"state": "Running", "pids": [123], "port_open": True}
        self.ns["status"] = lambda: state
        self.ns["run"] = Mock()
        self.assertEqual(self.ns["start"](), state)
        self.ns["run"].assert_not_called()

    def test_start_refuses_an_unrelated_port_listener(self):
        self.ns["status"] = lambda: {"state": "Port in use", "pids": [], "port_open": True}
        self.ns["run"] = Mock()
        with self.assertRaises(RuntimeError):
            self.ns["start"]()
        self.ns["run"].assert_not_called()

    def test_start_respawns_a_dead_pane_without_forcing(self):
        running = {"state": "Running", "pids": [123], "port_open": True, "log": "Done (1s)!"}
        self.ns["status"] = Mock(side_effect=[{"state": "Stopped", "pids": [], "port_open": False}, running])
        self.ns["run"] = Mock(return_value="1")
        with patch.object(self.ns["subprocess"], "run", return_value=types.SimpleNamespace(returncode=0)):
            self.assertEqual(self.ns["start"](), running)
        command = self.ns["run"].call_args_list[-1].args
        self.assertEqual(command[0], "respawn-pane")
        self.assertNotIn("-k", command)
        self.assertIn(str(self.folder), command)

    def test_start_refuses_a_live_pane(self):
        self.ns["status"] = lambda: {"state": "Stopped", "pids": [], "port_open": False}
        self.ns["run"] = Mock(return_value="0")
        with patch.object(self.ns["subprocess"], "run", return_value=types.SimpleNamespace(returncode=0)):
            with self.assertRaises(RuntimeError):
                self.ns["start"]()
        self.assertEqual(self.ns["run"].call_count, 1)

    def test_start_creates_a_session_with_remain_on_exit(self):
        running = {"state": "Running", "pids": [123], "port_open": True, "log": "Done (1s)!"}
        self.ns["status"] = Mock(side_effect=[{"state": "Stopped", "pids": [], "port_open": False}, running])
        self.ns["run"] = Mock(return_value="")
        with patch.object(self.ns["subprocess"], "run", return_value=types.SimpleNamespace(returncode=1)):
            self.assertEqual(self.ns["start"](), running)
        command = self.ns["run"].call_args.args
        self.assertEqual(command[0], "new-session")
        self.assertEqual(command[-2:], ("remain-on-exit", "on"))

    def test_running_status_survives_daily_log_rotation(self):
        (self.folder / "logs").mkdir()
        (self.folder / "logs/latest.log").write_text("Recent activity only", encoding="ascii")
        self.ns["pids"] = lambda: [123]
        with patch.object(self.ns["socket"], "socket") as factory:
            factory.return_value.__enter__.return_value.connect_ex.return_value = 0
            self.assertEqual(self.ns["status"]()["state"], "Running")

    def test_start_waits_for_world_ready_after_the_port_opens(self):
        loading = {"state": "Running", "pids": [123], "port_open": True, "log": "Preparing level"}
        ready = {**loading, "log": "Done (1s)!"}
        self.ns["status"] = Mock(side_effect=[{"state": "Stopped", "pids": [], "port_open": False}, loading, ready])
        self.ns["run"] = Mock(return_value="1")
        with patch.object(self.ns["subprocess"], "run", return_value=types.SimpleNamespace(returncode=0)), patch.object(self.ns["time"], "sleep"):
            self.assertEqual(self.ns["start"](), ready)
        self.assertEqual(self.ns["status"].call_count, 3)

    def test_stop_verifies_pane_ownership_before_sending(self):
        self.ns["status"] = lambda: {"state": "Running", "pids": [3], "port_open": True}
        self.ns["run"] = Mock(return_value="99")
        self.ns["Path"] = lambda *parts: types.SimpleNamespace(read_text=lambda: "3 (java) S 1 0")
        with self.assertRaises(RuntimeError):
            self.ns["stop"]()
        self.assertEqual(self.ns["run"].call_count, 1)

    def test_stop_sends_stop_and_waits_for_process_exit(self):
        stopped = {"state": "Stopped", "pids": [], "port_open": False}
        self.ns["status"] = Mock(side_effect=[{"state": "Running", "pids": [3], "port_open": True}, stopped])
        self.ns["run"] = Mock(return_value="2")
        self.ns["Path"] = lambda *parts: types.SimpleNamespace(read_text=lambda: "3 (java) S 2 0" if parts[1] == "3" else "2 (bash) S 1 0")
        self.assertEqual(self.ns["stop"](), stopped)
        commands = [call.args for call in self.ns["run"].call_args_list]
        self.assertIn(("send-keys", "-t", "mc120:0.0", "-l", "stop"), commands)
        self.assertEqual(commands[-1], ("send-keys", "-t", "mc120:0.0", "Enter"))

    def test_deployment_backs_up_and_preserves_unicode_names(self):
        (self.folder / "uploads").mkdir()
        (self.folder / "config").mkdir()
        name = "space ' \u914d\u7f6e.json"
        (self.folder / "uploads" / name).write_bytes(b"new")
        (self.folder / "config" / name).write_bytes(b"old")
        self.ns["names"] = [name]
        result = self.ns["deploy"]()
        self.assertEqual((Path(result["backup"]) / name).read_bytes(), b"old")
        self.assertEqual((self.folder / "config" / name).read_bytes(), b"new")

    def test_deployment_rejects_path_traversal(self):
        (self.folder / "config").mkdir()
        self.ns["names"] = ["../other.json"]
        with self.assertRaises(ValueError):
            self.ns["deploy"]()
        self.assertFalse(list(self.folder.glob("config-backup-*")))


class LocalTerminal(unittest.TestCase):
    def test_actual_login_banner_and_wrapped_status_response(self):
        with tempfile.TemporaryDirectory(dir=ROOT) as folder:
            child = Path(folder) / "fake_charac.py"
            child.write_text('''import json,re,sys
print("Connected. Ctrl+] detaches from this workspace; mouse input is forwarded.",flush=True)
for line in sys.stdin:
    match=re.search(r"[a-f0-9]{32}",line)
    if match:
        token=match.group(0)
        body=json.dumps({"state":"Running","pids":[123],"port_open":True,"log":"x"*2000}).encode().hex()
        print("\\nCGUI_BEGIN_"+token+"\\n"+body+"\\nCGUI_END_"+token+":0",flush=True)
''', encoding="ascii")
            native_terminal = gui.Terminal
            client = gui.Client(queue.Queue())
            with patch.object(gui, "TARGET", "00000000-0000-0000-0000-000000000001"), patch.object(gui, "SERVER", "https://access.example.test"), patch.object(gui, "Terminal", side_effect=lambda arguments: native_terminal([sys.executable, "-u", str(child)])):
                try:
                    state = client.login()
                    self.assertEqual(state["log"], "x" * 2000)
                    self.assertEqual(state["state"], "Running")
                finally:
                    client.disconnect()

    def test_real_charac_runs_in_an_invisible_terminal(self):
        if not Path(gui.CHARAC_EXE).is_file():
            self.skipTest("Install charac or set CHARAC_EXE for the optional CLI smoke test.")
        terminal = gui.Terminal([gui.CHARAC_EXE, "--help"])
        self.addCleanup(terminal.close)
        terminal.wait(r"Usage: charac", 15)

    def test_unconfigured_login_does_not_start_charac(self):
        client = gui.Client(queue.Queue())
        with patch.object(gui, "TARGET", ""), patch.object(gui, "Terminal") as terminal:
            with self.assertRaises(ValueError):
                client.login()
            terminal.assert_not_called()

    def test_real_terminal_preserves_utf8_and_frame_boundaries(self):
        with tempfile.TemporaryDirectory(dir=ROOT) as folder:
            child = Path(folder) / "fake_shell.py"
            child.write_text('''import re,sys
for line in sys.stdin:
    match = re.search(r"[a-f0-9]{32}", line)
    if match:
        token = match.group(0)
        print("\\nCGUI_BEGIN_" + token + "\\n" + chr(0x914d) + "\\nCGUI_END_" + token + ":0", flush=True)
''', encoding="ascii")
            terminal = gui.Terminal([sys.executable, "-u", str(child)])
            try:
                self.assertEqual(terminal.execute("true", 10), "\u914d")
            finally:
                terminal.close()

    def test_a_failed_child_is_reported_without_waiting_for_login_timeout(self):
        terminal = gui.Terminal([sys.executable, "-c", "raise ValueError('test')"])
        self.addCleanup(terminal.close)
        started = time.monotonic()
        with self.assertRaises(ChildProcessError):
            terminal.wait("not-a-real-success-marker", 15)
        self.assertLess(time.monotonic() - started, 5)


class GuiIntegration(unittest.TestCase):
    def test_native_drop_picker_and_worker_completion(self):
        app = gui.App()
        self.addCleanup(app.destroy)
        self.addCleanup(app.drop.close)
        self.addCleanup(app.executor.shutdown)
        with tempfile.TemporaryDirectory(dir=ROOT) as folder:
            path = Path(folder) / "test \u914d\u7f6e.json"
            path.write_bytes(b"{}")
            payload = struct.pack("<IiiII", 20, 0, 0, 0, 1) + (str(path) + "\0\0").encode("utf-16le")
            alloc = gui.api(gui.kernel, "GlobalAlloc", wt.HANDLE, wt.UINT, ct.c_size_t)
            lock = gui.api(gui.kernel, "GlobalLock", ct.c_void_p, wt.HANDLE)
            unlock = gui.api(gui.kernel, "GlobalUnlock", wt.BOOL, wt.HANDLE)
            send = gui.api(gui.user, "SendMessageW", ct.c_ssize_t, wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM)
            handle = gui.checked(alloc(0x42, len(payload)))
            ct.memmove(gui.checked(lock(handle)), payload, len(payload))
            unlock(handle)
            send(app.drop.hwnd, 0x233, handle, 0)
            deadline = time.monotonic() + 2
            while not app.paths and time.monotonic() < deadline:
                app.update()
                time.sleep(0.02)
            self.assertIn(str(path), app.rows)
            with patch.object(gui.filedialog, "askopenfilenames", return_value=(str(path),)):
                app.choose_files()
            self.assertEqual(len(app.paths), 1)
            self.assertIn("\\u914d", app.tree.item(next(iter(app.rows.values())), "values")[0])
            app.client = types.SimpleNamespace(connected=True)
            app.submit("Mock status", lambda: {"state": "Running", "pids": [123]})
            deadline = time.monotonic() + 3
            while app.future is not None and time.monotonic() < deadline:
                app.update()
                time.sleep(0.02)
            self.assertEqual(app.server_state.get(), "Running")
            self.assertEqual(str(app.stop_button["state"]), "normal")
            self.assertEqual(str(app.start_button["state"]), "disabled")


if __name__ == "__main__":
    unittest.main(verbosity=2)
