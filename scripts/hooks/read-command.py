#!/usr/bin/env python3
"""Reads a hook payload on stdin and prints the Bash command with heredoc bodies
stripped, so scripts/hooks/guard-bash.sh does not analyse quoted text — a commit
message describing a blocked command is not an attempt to run it."""
import json
import re
import sys

try:
    raw = json.load(sys.stdin).get("tool_input", {}).get("command", "")
except Exception:
    raw = ""

OPENER = re.compile(r"""<<-?\s*(['"]?)([A-Za-z_][A-Za-z0-9_]*)\1""")

kept, marker = [], None
for line in raw.split("\n"):
    if marker is None:
        kept.append(line)
        m = OPENER.search(line)
        if m:
            marker = m.group(2)
    elif line.strip() == marker:
        marker = None
        # Close the heredoc with a separator so a command on the next line is still
        # seen at a command position by the guard's anchored patterns.
        kept.append(";")

print("\n".join(kept))
