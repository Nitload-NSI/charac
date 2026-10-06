[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $Target = '10.10.0.103'
)

$ErrorActionPreference = 'Stop'

$remoteScript = @'
set -u

section() {
    printf '\n===== %s =====\n' "$1"
}

run() {
    printf '+ %s\n' "$*"
    "$@" 2>&1 || printf '[command failed: %s]\n' "$?"
}

section "host"
run hostnamectl --static
run uname -a
run id

section "charac user"
run getent passwd charac
run id charac
run stat -c '%A %U:%G %n' /var/lib/charac /var/lib/charac/keys

section "rpm"
run rpm -q charac-server
run rpm -qf /opt/charac/server/char_rac_server
run rpm -ql charac-server

section "server service"
run systemctl is-enabled charac-server.service
run systemctl is-active charac-server.service
run systemctl show charac-server.service --no-pager \
    --property=LoadState,ActiveState,SubState,User,Group,ExecMainPID,ExecStart,FragmentPath,DropInPaths
run systemctl cat charac-server.service
run ps -o user,pid,ppid,etime,args -C char_rac_server

section "server files"
run stat -c '%A %U:%G %n' /opt/charac/server/char_rac_server /etc/charac /etc/charac/workspace-access.config
printf '+ configuration sections (values intentionally omitted)\n'
sudo -v
sudo -u charac grep -E '^\[[^]]+\]' /etc/charac/workspace-access.config 2>&1 || true
printf '+ configuration readable by charac\n'
sudo -u charac test -r /etc/charac/workspace-access.config && echo yes || echo no

section "database listeners"
run ss -ltnp

section "rootless podman as charac"
run sudo -u charac podman ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'
run sudo -u charac podman volume ls

section "root podman view"
run sudo podman ps -a --format 'table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}'

section "relevant units"
run systemctl list-units --all --type=service --no-legend 'charac*'
run systemctl list-unit-files --no-legend 'charac*'

section "server health via localhost"
run curl --silent --show-error --max-time 5 http://127.0.0.1:8080/health/live
run curl --silent --show-error --max-time 5 http://127.0.0.1:8080/health/ready
'@

Write-Host "Connecting to $Target ..."
$remoteScript | & ssh.exe -tt -o ConnectTimeout=10 $Target 'bash -s'
if ($LASTEXITCODE -ne 0) {
    throw "Remote probe failed with exit code $LASTEXITCODE."
}
