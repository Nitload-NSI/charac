#!/bin/sh
set -eu
systemd-sysusers /usr/lib/sysusers.d/charac.conf
systemctl daemon-reload
