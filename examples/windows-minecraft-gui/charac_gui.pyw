"""Windows-only charac uploader and Minecraft server controller. No pip packages."""

import base64
import codecs
import concurrent.futures
import ctypes as ct
from ctypes import wintypes as wt
import json
import os
from pathlib import Path
import queue
import re
import shlex
import shutil
import subprocess
import sys
import threading
import time
import tkinter as tk
from tkinter import filedialog, messagebox, scrolledtext, ttk
import traceback
import uuid
import zlib


# USER CONFIGURATION: copy config.example.json to config.local.json first.
CONFIG_PATH = Path(__file__).with_name("config.local.json")
SETTINGS = json.loads(CONFIG_PATH.read_text(encoding="utf-8")) if CONFIG_PATH.is_file() else {}
CHARAC_EXE = os.environ.get("CHARAC_EXE") or SETTINGS.get("charac_exe") or shutil.which("charac") or r"C:\Program Files\nitload\charac\charac.exe"
SERVER = SETTINGS.get("server", "https://access.example.com")
TARGET = SETTINGS.get("target", "")
TARGET_NAME = SETTINGS.get("target_name", "Minecraft server")
LOCAL_FOLDER = SETTINGS.get("local_folder") or str(Path.home() / "Downloads")
UPLOAD_FOLDER = SETTINGS.get("upload_folder", "/home/minecraft/characfile")
MC_FOLDER = SETTINGS.get("mc_folder", "/srv/minecraft")
TMUX_SOCKET = SETTINGS.get("tmux_socket") or MC_FOLDER + "/.mc120.tmux.sock"
TMUX_SESSION = SETTINGS.get("tmux_session", "mc120")
TMUX_PANE = SETTINGS.get("tmux_pane", TMUX_SESSION + ":0.0")
FORGE_ARGS = SETTINGS.get("forge_args", "@libraries/net/minecraftforge/forge/1.20.1-47.4.1/unix_args.txt")
MC_PORT = SETTINGS.get("mc_port", 25565)
MAINTAINER_GROUP = SETTINGS.get("maintainer_group", "")
LOGIN_TIMEOUT = 600
UPLOAD_TIMEOUT = 600
STOP_TIMEOUT = 180
START_TIMEOUT = 300
LOG_PATH = Path(__file__).with_suffix(".log")


# This helper runs on the Linux MC host, through the authenticated charac shell.
REMOTE_HELPER = r'''
import filecmp
import grp
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import tempfile
import time

root = Path(config["root"]).resolve()
tmux = ["tmux", "-S", config["socket"]]
pane = config["pane"]
window = pane.rsplit(".", 1)[0]

def run(*args):
    return subprocess.check_output(tmux + list(args), stderr=subprocess.STDOUT, text=True).strip()

def pids():
    found = []
    for entry in Path("/proc").iterdir():
        if not entry.name.isdecimal():
            continue
        try:
            argv = (entry / "cmdline").read_bytes().split(b"\0")
            if not argv or Path(os.fsdecode(argv[0])).name != "java":
                continue
            if config["forge"].encode() in argv and Path(os.readlink(entry / "cwd")).resolve() == root:
                found.append(int(entry.name))
        except FileNotFoundError:
            continue
    if len(found) > 1:
        raise RuntimeError("Multiple MC Java processes: " + repr(found))
    return found

def status():
    processes = pids()
    with socket.socket() as probe:
        probe.settimeout(1)
        listening = probe.connect_ex(("127.0.0.1", config["port"])) == 0
    log = root / "logs/latest.log"
    tail = ""
    if log.exists():
        with log.open("rb") as stream:
            stream.seek(max(0, log.stat().st_size - 65536))
            tail = stream.read().decode("utf-8", "replace")
    return {"state": ("Running" if listening else "Starting") if processes else ("Port in use" if listening else "Stopped"),
            "pids": processes, "port_open": listening, "log": tail[-6000:]}

def stop():
    current = status()
    if current["state"] == "Stopped":
        return current
    if not current["pids"]:
        raise RuntimeError("The game port belongs to an unverified process.")
    parent = int(run("display-message", "-p", "-t", pane, "#{pane_pid}"))
    for pid in current["pids"]:
        ancestors = set()
        while pid > 1 and pid not in ancestors:
            ancestors.add(pid)
            fields = Path("/proc", str(pid), "stat").read_text().rsplit(")", 1)[1].split()
            pid = int(fields[1])
        if parent not in ancestors:
            raise RuntimeError("The configured tmux pane does not own the MC process.")
    run("set-option", "-w", "-t", window, "remain-on-exit", "on")
    run("send-keys", "-t", pane, "-l", "stop")
    run("send-keys", "-t", pane, "Enter")
    deadline = time.monotonic() + config["stop_timeout"]
    while time.monotonic() < deadline:
        current = status()
        if current["state"] == "Stopped":
            return current
        time.sleep(1)
    raise TimeoutError(current["log"])

def start():
    current = status()
    if current["pids"]:
        return current
    if current["port_open"]:
        raise RuntimeError("The game port is already occupied.")
    (root / "run.sh").stat()
    existing = subprocess.run(tmux + ["has-session", "-t", config["session"]], capture_output=True)
    if existing.returncode == 0:
        if run("display-message", "-p", "-t", pane, "#{pane_dead}") != "1":
            raise RuntimeError("The configured tmux pane is still active.")
        run("respawn-pane", "-t", pane, "-c", str(root), "bash run.sh")
    else:
        run("new-session", "-d", "-s", config["session"], "-c", str(root), "bash run.sh", ";",
            "set-option", "-w", "-t", window, "remain-on-exit", "on")
    sock = Path(config["socket"])
    if sock.stat().st_uid == os.geteuid():
        if config["group"]:
            os.chown(sock, -1, grp.getgrnam(config["group"]).gr_gid)
        os.chmod(sock, 0o660)
    started = time.monotonic()
    deadline = started + config["start_timeout"]
    while time.monotonic() < deadline:
        current = status()
        if current["state"] == "Running" and "Done (" in current["log"]:
            return current
        if not current["pids"] and time.monotonic() - started > 10:
            raise ChildProcessError(current["log"])
        time.sleep(1)
    raise TimeoutError(current["log"])

def deploy():
    target = (root / destination).resolve()
    if destination not in ("config", "mods") or target != root / destination:
        raise ValueError(destination)
    target.stat()
    sources = []
    for name in names:
        if Path(name).name != name or name in (".", ".."):
            raise ValueError(name)
        src = Path(config["upload"]) / name
        dst = target / name
        if src.resolve().parent != Path(config["upload"]).resolve() or dst.is_symlink():
            raise ValueError(name)
        if not src.is_file():
            raise FileNotFoundError(src)
        sources.append((src, dst))
    backup = Path(tempfile.mkdtemp(prefix=destination + "-backup-", dir=root))
    for src, dst in sources:
        if dst.exists():
            shutil.copy2(dst, backup / dst.name)
    for src, dst in sources:
        shutil.copyfile(src, dst)
        if not filecmp.cmp(src, dst, shallow=False):
            raise OSError(dst)
    return {"destination": str(target), "backup": str(backup), "files": names}

result = {"status": status, "start": start, "stop": stop, "deploy": deploy}[action]()
print(json.dumps(result).encode().hex())
'''


if os.name != "nt":
    raise OSError("Windows is required.")

kernel = ct.WinDLL("kernel32", use_last_error=True)
user = ct.WinDLL("user32", use_last_error=True)
shell = ct.WinDLL("shell32", use_last_error=True)


def api(dll, name, result, *arguments):
    function = getattr(dll, name)
    function.restype, function.argtypes = result, arguments
    return function


def checked(result):
    if not result:
        raise ct.WinError(ct.get_last_error())
    return result


class Coord(ct.Structure):
    _fields_ = [("X", ct.c_short), ("Y", ct.c_short)]


class StartupInfo(ct.Structure):
    _fields_ = [("cb", wt.DWORD), ("lpReserved", wt.LPWSTR), ("lpDesktop", wt.LPWSTR),
                ("lpTitle", wt.LPWSTR), ("dwX", wt.DWORD), ("dwY", wt.DWORD),
                ("dwXSize", wt.DWORD), ("dwYSize", wt.DWORD), ("dwXCountChars", wt.DWORD),
                ("dwYCountChars", wt.DWORD), ("dwFillAttribute", wt.DWORD),
                ("dwFlags", wt.DWORD), ("wShowWindow", wt.WORD), ("cbReserved2", wt.WORD),
                ("lpReserved2", ct.c_void_p), ("hStdInput", wt.HANDLE),
                ("hStdOutput", wt.HANDLE), ("hStdError", wt.HANDLE)]


class StartupInfoEx(ct.Structure):
    _fields_ = [("StartupInfo", StartupInfo), ("attributes", ct.c_void_p)]


class ProcessInfo(ct.Structure):
    _fields_ = [("process", wt.HANDLE), ("thread", wt.HANDLE),
                ("pid", wt.DWORD), ("tid", wt.DWORD)]


CreatePipe = api(kernel, "CreatePipe", wt.BOOL, ct.POINTER(wt.HANDLE), ct.POINTER(wt.HANDLE), ct.c_void_p, wt.DWORD)
CloseHandle = api(kernel, "CloseHandle", wt.BOOL, wt.HANDLE)
CreatePseudoConsole = api(kernel, "CreatePseudoConsole", ct.c_long, Coord, wt.HANDLE, wt.HANDLE, wt.DWORD, ct.POINTER(wt.HANDLE))
ClosePseudoConsole = api(kernel, "ClosePseudoConsole", None, wt.HANDLE)
InitializeAttributes = api(kernel, "InitializeProcThreadAttributeList", wt.BOOL, ct.c_void_p, wt.DWORD, wt.DWORD, ct.POINTER(ct.c_size_t))
UpdateAttribute = api(kernel, "UpdateProcThreadAttribute", wt.BOOL, ct.c_void_p, wt.DWORD, ct.c_size_t, ct.c_void_p, ct.c_size_t, ct.c_void_p, ct.c_void_p)
DeleteAttributes = api(kernel, "DeleteProcThreadAttributeList", None, ct.c_void_p)
CreateProcess = api(kernel, "CreateProcessW", wt.BOOL, wt.LPCWSTR, wt.LPWSTR, ct.c_void_p, ct.c_void_p, wt.BOOL, wt.DWORD, ct.c_void_p, wt.LPCWSTR, ct.POINTER(StartupInfoEx), ct.POINTER(ProcessInfo))
ReadFile = api(kernel, "ReadFile", wt.BOOL, wt.HANDLE, ct.c_void_p, wt.DWORD, ct.POINTER(wt.DWORD), ct.c_void_p)
WriteFile = api(kernel, "WriteFile", wt.BOOL, wt.HANDLE, ct.c_void_p, wt.DWORD, ct.POINTER(wt.DWORD), ct.c_void_p)
WaitForSingleObject = api(kernel, "WaitForSingleObject", wt.DWORD, wt.HANDLE, wt.DWORD)
TerminateProcess = api(kernel, "TerminateProcess", wt.BOOL, wt.HANDLE, wt.UINT)
GetParent = api(user, "GetParent", wt.HWND, wt.HWND)
SetWindowProc = api(user, "SetWindowLongPtrW" if ct.sizeof(ct.c_void_p) == 8 else "SetWindowLongW", ct.c_ssize_t, wt.HWND, ct.c_int, ct.c_ssize_t)
CallWindowProc = api(user, "CallWindowProcW", ct.c_ssize_t, ct.c_void_p, wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM)
DragAcceptFiles = api(shell, "DragAcceptFiles", None, wt.HWND, wt.BOOL)
DragQueryFile = api(shell, "DragQueryFileW", wt.UINT, wt.HANDLE, wt.UINT, wt.LPWSTR, wt.UINT)
DragFinish = api(shell, "DragFinish", None, wt.HANDLE)
WindowProc = ct.WINFUNCTYPE(ct.c_ssize_t, wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM)
VT = re.compile(r"\x1b\][^\x07\x1b]*(?:\x07|\x1b\\)|\x1b\[[0-?]*[ -/]*[@-~]|\x1b[=>]")


def plain(text):
    return VT.sub("", text).replace("\r", "").replace("\x00", "")


def ascii_text(value):
    return str(value).encode("ascii", "backslashreplace").decode("ascii")


class Terminal:
    """A real, invisible Windows terminal for charac's interactive connect."""

    def __init__(self, arguments):
        self.condition = threading.Condition()
        self.write_lock = threading.Lock()
        self.close_lock = threading.Lock()
        self.text, self.failure = "", None
        self.ended = threading.Event()
        self.input, self.output, self.console, self.process = (wt.HANDLE() for _ in range(4))
        input_read, output_write = wt.HANDLE(), wt.HANDLE()
        attributes, initialized, created = None, False, False
        try:
            checked(CreatePipe(ct.byref(input_read), ct.byref(self.input), None, 0))
            checked(CreatePipe(ct.byref(self.output), ct.byref(output_write), None, 0))
            hr = CreatePseudoConsole(Coord(240, 50), input_read, output_write, 0, ct.byref(self.console))
            if hr < 0:
                raise OSError(hr, ct.FormatError(hr & 0xFFFFFFFF))
            size = ct.c_size_t()
            InitializeAttributes(None, 1, 0, ct.byref(size))
            attributes = ct.create_string_buffer(size.value)
            checked(InitializeAttributes(attributes, 1, 0, ct.byref(size)))
            initialized = True
            checked(UpdateAttribute(attributes, 0, 0x20016, self.console, ct.sizeof(wt.HANDLE), None, None))
            startup, process = StartupInfoEx(), ProcessInfo()
            startup.StartupInfo.cb, startup.attributes = ct.sizeof(startup), ct.addressof(attributes)
            startup.StartupInfo.dwFlags = 0x100
            command = ct.create_unicode_buffer(subprocess.list2cmdline(arguments))
            checked(CreateProcess(None, command, None, None, False, 0x80000, None, None, ct.byref(startup), ct.byref(process)))
            self.process = wt.HANDLE(process.process)
            CloseHandle(process.thread)
            created = True
        finally:
            if initialized:
                DeleteAttributes(attributes)
            for handle in (input_read, output_write):
                if handle:
                    CloseHandle(handle)
            if not created:
                self.close()
        self.reader = threading.Thread(target=self._read, daemon=True)
        self.reader.start()

    @property
    def alive(self):
        return bool(self.process and not self.ended.is_set() and WaitForSingleObject(self.process, 0) == 258)

    def _read(self):
        decoder, buffer, count = codecs.getincrementaldecoder("utf-8")("replace"), ct.create_string_buffer(32768), wt.DWORD()
        try:
            while ReadFile(self.output, buffer, len(buffer), ct.byref(count), None):
                if not count.value:
                    break
                chunk = decoder.decode(buffer.raw[:count.value])
                if "\x1b[6n" in chunk:
                    self.send("\x1b[1;1R")
                with self.condition:
                    self.text = (self.text + chunk)[-2000000:]
                    self.condition.notify_all()
            if ct.get_last_error() not in (0, 109, 232, 995):
                raise ct.WinError(ct.get_last_error())
        except OSError as error:
            self.failure = error
        finally:
            self.ended.set()
            with self.condition:
                self.condition.notify_all()

    def send(self, text):
        data, count = text.encode("utf-8"), wt.DWORD()
        with self.write_lock:
            while data:
                checked(WriteFile(self.input, data, len(data), ct.byref(count), None))
                if not count.value:
                    raise BrokenPipeError()
                data = data[count.value:]

    def wait(self, expression, timeout):
        deadline = time.monotonic() + timeout
        exited = None
        with self.condition:
            while True:
                text = plain(self.text)
                match = re.search(expression, text, re.MULTILINE | re.DOTALL)
                if match:
                    return match
                if self.ended.is_set():
                    raise self.failure or ChildProcessError(text[-6000:])
                if not self.alive:
                    exited = exited or time.monotonic()
                    if time.monotonic() - exited >= 0.25:
                        raise ChildProcessError(text[-6000:])
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise TimeoutError(text[-6000:])
                self.condition.wait(min(remaining, 0.25 if exited else 1))

    def execute(self, command, timeout=45):
        token = uuid.uuid4().hex
        wrapped = ("stty -echo; printf '\\nCGUI_BEGIN_%s\\n' " + token + "; " + command
                   + "; _cg_rc=$?; printf '\\nCGUI_END_%s:%s\\n' " + token + " \"$_cg_rc\"\r")
        self.send(wrapped)
        match = self.wait(r"^CGUI_BEGIN_" + token + r"\n(.*?)\nCGUI_END_" + token + r":(\d+)\n", timeout)
        output, code = match.group(1), int(match.group(2))
        if code:
            raise subprocess.CalledProcessError(code, command, output=output)
        return output.strip()

    def close(self):
        with self.close_lock:
            try:
                if self.process and WaitForSingleObject(self.process, 0) == 258:
                    try:
                        self.send("\x1d")
                    except OSError:
                        if WaitForSingleObject(self.process, 0) == 258:
                            raise
                    if WaitForSingleObject(self.process, 2000) == 258:
                        checked(TerminateProcess(self.process, 1))
                        WaitForSingleObject(self.process, 2000)
            finally:
                if self.console:
                    ClosePseudoConsole(self.console)
                    self.console = wt.HANDLE()
                for name in ("input", "output", "process"):
                    handle = getattr(self, name)
                    if handle:
                        CloseHandle(handle)
                        setattr(self, name, wt.HANDLE())
                self.ended.set()
                with self.condition:
                    self.condition.notify_all()


class Client:
    def __init__(self, events):
        self.events, self.terminal = events, None

    @property
    def connected(self):
        return bool(self.terminal and self.terminal.alive)

    def login(self):
        if not TARGET or SERVER == "https://access.example.com":
            raise ValueError("Configure server and target in config.local.json, then restart the GUI.")
        self.disconnect()
        self.terminal = Terminal([CHARAC_EXE, "connect", "--server", SERVER, "--target", TARGET])
        completed = False
        try:
            self.terminal.wait(r"Connected\. Ctrl", LOGIN_TIMEOUT)
            result = self.remote("status")
            completed = True
            return result
        finally:
            if not completed:
                self.disconnect()

    def disconnect(self):
        terminal, self.terminal = self.terminal, None
        if terminal:
            terminal.close()

    def remote(self, action, destination="", names=()):
        if not self.connected:
            raise ConnectionError("Login is required.")
        config = {"root": MC_FOLDER, "upload": UPLOAD_FOLDER, "socket": TMUX_SOCKET,
                  "session": TMUX_SESSION, "pane": TMUX_PANE, "forge": FORGE_ARGS,
                  "port": MC_PORT, "group": MAINTAINER_GROUP,
                  "stop_timeout": STOP_TIMEOUT, "start_timeout": START_TIMEOUT}
        source = ("config = " + repr(config) + "\naction = " + repr(action)
                  + "\ndestination = " + repr(destination) + "\nnames = " + repr(names) + "\n" + REMOTE_HELPER)
        payload = base64.b64encode(zlib.compress(source.encode("utf-8"))).decode("ascii")
        command = "python3 -c " + shlex.quote("import base64,zlib;exec(zlib.decompress(base64.b64decode(" + repr(payload) + ")))")
        timeout = {"start": START_TIMEOUT + 30, "stop": STOP_TIMEOUT + 30}.get(action, 60)
        return json.loads(bytes.fromhex(self.terminal.execute(command, timeout)))

    def upload(self, paths, destination):
        if not self.connected:
            raise ConnectionError("Login is required.")
        names = [path.name for path in paths]
        if len(set(names)) != len(names):
            raise ValueError("Files with the same name cannot be uploaded together.")
        for index, path in enumerate(paths, 1):
            self.events.put(("log", "Uploading %d/%d: %s" % (index, len(paths), ascii_text(path))))
            process = subprocess.run([CHARAC_EXE, "cp", str(path), "--server", SERVER, "--target", TARGET],
                                     stdout=subprocess.PIPE, stderr=subprocess.STDOUT, encoding="utf-8",
                                     errors="replace", creationflags=subprocess.CREATE_NO_WINDOW, timeout=UPLOAD_TIMEOUT)
            if process.returncode:
                raise subprocess.CalledProcessError(process.returncode, process.args, output=process.stdout)
            self.events.put(("file", str(path), "Uploaded"))
            self.events.put(("log", process.stdout.strip()))
        if destination:
            result = self.remote("deploy", destination, names)
            for path in paths:
                self.events.put(("file", str(path), "Deployed"))
            self.events.put(("log", "Destination: " + result["destination"] + "\nBackup: " + result["backup"]))
        return {"uploaded": len(paths)}


class FileDrop:
    def __init__(self, window, events):
        window.update_idletasks()
        self.hwnd, self.events = GetParent(window.winfo_id()), events
        self.callback = WindowProc(self.receive)
        ct.set_last_error(0)
        self.previous = SetWindowProc(self.hwnd, -4, ct.cast(self.callback, ct.c_void_p).value)
        if not self.previous and ct.get_last_error():
            raise ct.WinError(ct.get_last_error())
        DragAcceptFiles(self.hwnd, True)

    def receive(self, hwnd, message, wparam, lparam):
        if message != 0x233:
            return CallWindowProc(self.previous, hwnd, message, wparam, lparam)
        paths = []
        try:
            for index in range(DragQueryFile(wparam, 0xFFFFFFFF, None, 0)):
                buffer = ct.create_unicode_buffer(DragQueryFile(wparam, index, None, 0) + 1)
                DragQueryFile(wparam, index, buffer, len(buffer))
                paths.append(buffer.value)
            self.events.put(("drop", paths))
        finally:
            DragFinish(wparam)
        return 0

    def close(self):
        DragAcceptFiles(self.hwnd, False)
        SetWindowProc(self.hwnd, -4, self.previous)


class App(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title("Charac | Minecraft Server")
        self.geometry("900x740")
        self.minsize(760, 640)
        self.events, self.paths, self.rows = queue.Queue(), {}, {}
        self.client = Client(self.events)
        self.executor = concurrent.futures.ThreadPoolExecutor(max_workers=1)
        self.future, self.closing, self.operation = None, False, ""
        self.connection = tk.StringVar(value="Disconnected")
        self.server_state = tk.StringVar(value="Unknown")
        self.destination = tk.StringVar(value="Upload folder only")
        self.status_line = tk.StringVar(value="Login, add files, then click Upload.")
        style = ttk.Style(self)
        style.theme_use("vista" if "vista" in style.theme_names() else "clam")
        style.configure("TButton", padding=(12, 7))
        style.configure("Title.TLabel", font=("Segoe UI", 20, "bold"))
        style.configure("Muted.TLabel", foreground="#586776")
        frame = ttk.Frame(self, padding=20)
        frame.pack(fill="both", expand=True)
        ttk.Label(frame, text="Charac Server Manager", style="Title.TLabel").pack(anchor="w")
        ttk.Label(frame, text=TARGET_NAME + "  |  Forge 1.20.1  |  " + SERVER, style="Muted.TLabel").pack(anchor="w", pady=(2, 16))
        toolbar = ttk.Frame(frame)
        toolbar.pack(fill="x")
        self.login_button = ttk.Button(toolbar, text="Login", command=self.login)
        self.login_button.pack(side="left")
        self.disconnect_button = ttk.Button(toolbar, text="Disconnect", command=lambda: self.submit("Disconnecting...", self.client.disconnect))
        self.disconnect_button.pack(side="left", padx=6)
        ttk.Label(toolbar, textvariable=self.connection).pack(side="left", padx=10)
        server = ttk.LabelFrame(frame, text="Minecraft server", padding=12)
        server.pack(fill="x", pady=14)
        ttk.Label(server, text="Status:").pack(side="left")
        ttk.Label(server, textvariable=self.server_state, width=16).pack(side="left", padx=10)
        self.start_button = ttk.Button(server, text="Start Server", command=lambda: self.submit("Starting server; waiting for the Done message...", self.client.remote, "start"))
        self.start_button.pack(side="left", padx=4)
        self.stop_button = ttk.Button(server, text="Stop Server", command=lambda: self.submit("Stopping server; waiting for world saving to finish...", self.client.remote, "stop"))
        self.stop_button.pack(side="left", padx=4)
        self.refresh_button = ttk.Button(server, text="Refresh", command=self.refresh)
        self.refresh_button.pack(side="right")
        ttk.Label(frame, text="Drop files anywhere in this window, or choose files below.", style="Muted.TLabel").pack(anchor="w")
        files = ttk.Frame(frame)
        files.pack(fill="both", expand=True, pady=8)
        self.tree = ttk.Treeview(files, columns=("name", "size", "status"), show="headings", height=6, selectmode="extended")
        for name, label, width in (("name", "Local file", 560), ("size", "Size", 85), ("status", "Status", 100)):
            self.tree.heading(name, text=label)
            self.tree.column(name, width=width, minwidth=60, stretch=name == "name")
        self.tree.pack(side="left", fill="both", expand=True)
        scrollbar = ttk.Scrollbar(files, command=self.tree.yview)
        scrollbar.pack(side="right", fill="y")
        self.tree.configure(yscrollcommand=scrollbar.set)
        self.tree.bind("<<TreeviewSelect>>", self.show_selected)
        file_buttons = ttk.Frame(frame)
        file_buttons.pack(fill="x")
        ttk.Button(file_buttons, text="Choose Files...", command=self.choose_files).pack(side="left")
        ttk.Button(file_buttons, text="Remove Selected", command=self.remove_files).pack(side="left", padx=6)
        self.upload_button = ttk.Button(file_buttons, text="Upload", command=self.upload)
        self.upload_button.pack(side="right")
        location = ttk.Frame(frame)
        location.pack(fill="x", pady=(14, 4))
        ttk.Label(location, text="After upload:").pack(side="left", padx=(0, 8))
        self.destination_box = ttk.Combobox(location, state="readonly", width=32, textvariable=self.destination,
                                           values=("Upload folder only", "Copy to MC config (backup first)", "Copy to MC mods (backup first)"))
        self.destination_box.pack(side="left")
        self.destination_box.bind("<<ComboboxSelected>>", self.show_destination)
        self.destination_label = ttk.Label(frame, text="Destination: " + UPLOAD_FOLDER, style="Muted.TLabel")
        self.destination_label.pack(anchor="w", pady=(0, 10))
        self.progress = ttk.Progressbar(frame, mode="indeterminate")
        self.progress.pack(fill="x")
        ttk.Label(frame, textvariable=self.status_line).pack(anchor="w", pady=6)
        self.log = scrolledtext.ScrolledText(frame, height=8, font=("Consolas", 9), state="disabled", wrap="word")
        self.log.pack(fill="both", expand=True)
        self.drop = FileDrop(self, self.events)
        self.protocol("WM_DELETE_WINDOW", self.close)
        self.after(100, self.poll)
        self.after(30000, self.auto_refresh)
        self.update_buttons()
        self.note("Ready. Uploads first go to " + UPLOAD_FOLDER)

    def note(self, text):
        line = time.strftime("%H:%M:%S ") + ascii_text(text) + "\n"
        with LOG_PATH.open("a", encoding="ascii") as stream:
            stream.write(line)
        self.log.configure(state="normal")
        self.log.insert("end", line)
        self.log.see("end")
        self.log.configure(state="disabled")

    def report_callback_exception(self, kind, value, tb):
        details = "".join(traceback.format_exception(kind, value, tb))
        if isinstance(value, subprocess.CalledProcessError) and value.output:
            details += "\n" + str(value.output)
        self.note(details)
        self.server_state.set("Unknown")
        self.status_line.set("Failed. See the operation log.")
        messagebox.showerror(kind.__name__, ascii_text(value.output or value) if isinstance(value, subprocess.CalledProcessError) else ascii_text(value), parent=self)

    def add_files(self, paths):
        for name in paths:
            path = Path(name).resolve(strict=True)
            if not path.is_file():
                raise IsADirectoryError(path)
            key = os.path.normcase(str(path))
            if key not in self.paths:
                self.paths[key] = path
                self.rows[str(path)] = self.tree.insert("", "end", values=(ascii_text(path.name), "%.1f KB" % (path.stat().st_size / 1024), "Ready"))
        self.update_buttons()

    def show_selected(self, event=None):
        if self.future is None and self.tree.selection():
            selected = self.tree.selection()[0]
            self.status_line.set(next(ascii_text(path) for path, row in self.rows.items() if row == selected))

    def choose_files(self):
        self.add_files(filedialog.askopenfilenames(parent=self, title="Choose files to upload", initialdir=LOCAL_FOLDER))

    def remove_files(self):
        selected = set(self.tree.selection())
        for key, path in list(self.paths.items()):
            if self.rows[str(path)] in selected:
                self.tree.delete(self.rows.pop(str(path)))
                del self.paths[key]
        self.update_buttons()

    def show_destination(self, event=None):
        target = {"Upload folder only": UPLOAD_FOLDER, "Copy to MC config (backup first)": MC_FOLDER + "/config",
                  "Copy to MC mods (backup first)": MC_FOLDER + "/mods"}[self.destination.get()]
        self.destination_label.configure(text="Destination: " + target)

    def update_buttons(self):
        idle, connected = self.future is None, self.client.connected
        for button, enabled in ((self.login_button, idle and not connected),
                                (self.disconnect_button, idle and connected),
                                (self.refresh_button, idle and connected),
                                (self.upload_button, idle and connected and bool(self.paths)),
                                (self.start_button, idle and connected and self.server_state.get() == "Stopped"),
                                (self.stop_button, idle and connected and self.server_state.get() in ("Running", "Starting"))):
            button.configure(state="normal" if enabled else "disabled")
        self.destination_box.configure(state="readonly" if idle else "disabled")

    def submit(self, label, function, *args):
        if self.future is not None:
            return
        self.operation = label
        self.status_line.set(label)
        self.note(label)
        self.future = self.executor.submit(function, *args)
        self.progress.start(12)
        self.update_buttons()

    def login(self):
        self.connection.set("Signing in - complete the browser login...")
        self.submit("Connecting with charac; waiting for browser sign-in...", self.client.login)

    def refresh(self):
        self.submit("Checking server status...", self.client.remote, "status")

    def upload(self):
        destination = {"Upload folder only": "", "Copy to MC config (backup first)": "config",
                       "Copy to MC mods (backup first)": "mods"}[self.destination.get()]
        self.submit("Uploading files...", self.client.upload, list(self.paths.values()), destination)

    def poll(self):
        if self.closing:
            return
        try:
            while not self.events.empty():
                event = self.events.get_nowait()
                if event[0] == "drop":
                    self.add_files(event[1])
                elif event[0] == "log":
                    self.note(event[1])
                elif event[0] == "file" and event[1] in self.rows:
                    self.tree.set(self.rows[event[1]], "status", event[2])
            if self.future is not None and self.future.done():
                future, self.future = self.future, None
                self.progress.stop()
                result = future.result()
                if isinstance(result, dict) and "state" in result:
                    self.server_state.set(result["state"])
                    self.note("Server: " + result["state"] + " | Java PID: " + repr(result["pids"]))
                self.status_line.set("Completed.")
                self.note("Completed.")
        finally:
            if self.future is None:
                self.connection.set("Connected" if self.client.connected else "Disconnected")
                if not self.client.connected:
                    self.server_state.set("Unknown")
            self.update_buttons()
            self.after(100, self.poll)

    def auto_refresh(self):
        if not self.closing:
            if self.future is None and self.client.connected:
                self.refresh()
            self.after(30000, self.auto_refresh)

    def close(self):
        busy = self.future is not None and not self.future.done()
        cancelling_login = busy and self.operation.startswith("Connecting") and self.client.terminal is not None
        if busy and not cancelling_login:
            self.status_line.set("An operation is still running. Wait for it to finish before closing.")
            return
        self.closing = True
        self.drop.close()
        self.executor.shutdown(wait=False)
        self.withdraw()
        threading.Thread(target=self.client.disconnect, daemon=False).start()
        self.after(100, self.wait_close)

    def wait_close(self):
        if self.client.terminal is None:
            self.destroy()
        else:
            self.after(100, self.wait_close)


def uncaught(kind, value, tb):
    details = ascii_text("".join(traceback.format_exception(kind, value, tb)))
    with LOG_PATH.open("a", encoding="ascii") as stream:
        stream.write(details + "\n")
    ct.windll.user32.MessageBoxW(None, details[-5000:], "Charac: " + kind.__name__, 0x10)


if __name__ == "__main__":
    sys.excepthook = uncaught
    threading.excepthook = lambda error: uncaught(error.exc_type, error.exc_value, error.exc_traceback)
    App().mainloop()
