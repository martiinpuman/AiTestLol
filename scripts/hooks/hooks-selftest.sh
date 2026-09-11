#!/usr/bin/env bash
# Proves every hook rule can fail, and that none of them fires on the legitimate
# case it sits next to. A guard that cannot block is not a guard, and a guard that
# blocks correct work is worse than none — so each rule is asserted from both sides.
#
# Usage: bash scripts/hooks/hooks-selftest.sh
set -uo pipefail

cd "$(dirname "$0")/../.." || exit 1
BASH_HOOK=scripts/hooks/guard-bash.sh
EDIT_HOOK=scripts/hooks/guard-source-edit.sh
TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT

pass=0; fail=0; blocked_cases=0; allowed_cases=0

json_cmd()  { python3 -c 'import json,sys; print(json.dumps({"tool_name":"Bash","tool_input":{"command":sys.argv[1]}}))' "$1"; }
json_file() { python3 -c 'import json,sys; print(json.dumps({"tool_name":"Write","tool_input":{"file_path":sys.argv[1]}}))' "$1"; }

# must_block <hook> <payload> <expected-rule-id> <label>
must_block() {
    local hook=$1 payload=$2 rule=$3 label=$4 out rc
    out=$(printf '%s' "$payload" | bash "$hook" 2>&1 >/dev/null); rc=$?
    blocked_cases=$((blocked_cases+1))
    if (( rc == 2 )) && [[ "$out" == *"$rule"* ]]; then
        pass=$((pass+1)); printf '  ok   %-14s %s\n' "$rule" "$label"
    else
        fail=$((fail+1)); printf '  FAIL %-14s %s (exit %d, said: %s)\n' "$rule" "$label" "$rc" "${out:-<nothing>}"
    fi
}

# must_allow <hook> <payload> <label>
must_allow() {
    local hook=$1 payload=$2 label=$3 out rc
    out=$(printf '%s' "$payload" | bash "$hook" 2>&1 >/dev/null); rc=$?
    allowed_cases=$((allowed_cases+1))
    if (( rc == 0 )); then
        pass=$((pass+1)); printf '  ok   %-14s %s\n' "allow" "$label"
    else
        fail=$((fail+1)); printf '  FAIL %-14s %s (exit %d, said: %s)\n' "allow" "$label" "$rc" "$out"
    fi
}

writecs() { mkdir -p "$(dirname "$TMP/$1")"; cat > "$TMP/$1"; printf '%s' "$TMP/$1"; }

echo "PreToolUse(Bash) — must block"
must_block $BASH_HOOK "$(json_cmd 'git push --force origin task/B-09')"                   AURORA-BASH-01 'force push'
must_block $BASH_HOOK "$(json_cmd 'git push -f origin task/B-09')"                        AURORA-BASH-01 'force push, short flag'
must_block $BASH_HOOK "$(json_cmd 'git push --force-with-lease origin task/B-09')"        AURORA-BASH-01 'force-with-lease'
must_block $BASH_HOOK "$(json_cmd 'git push -u origin main')"                             AURORA-BASH-02 'push to main'
must_block $BASH_HOOK "$(json_cmd 'git push origin HEAD:refs/heads/master')"              AURORA-BASH-02 'push to master via refspec'
must_block $BASH_HOOK "$(json_cmd 'for b in task/B-04 main; do git push -u origin "$b"; done')" AURORA-BASH-02 'a variable refspec, which cannot be evaluated'
must_block $BASH_HOOK "$(json_cmd 'rm -rf docs/decisions/ADR-0007-tenancy.md')"           AURORA-BASH-03 'delete an ADR'
must_block $BASH_HOOK "$(json_cmd 'git rm docs/decisions/ADR-0001-stack.md')"             AURORA-BASH-03 'git rm an ADR'
must_block $BASH_HOOK "$(json_cmd 'git add .env && git commit -m "config"')"              AURORA-BASH-04 'stage a .env'
must_block $BASH_HOOK "$(json_cmd 'dotnet test Aurora.sln')"                              AURORA-BASH-05 'dotnet without dev-env'
real_push_after_heredoc=$'git commit -F - <<MSG\na message\nMSG\ngit push -u origin main'
must_block $BASH_HOOK "$(json_cmd "$real_push_after_heredoc")" AURORA-BASH-02 'a real push following a heredoc'
env_after_heredoc=$'cat > x.md <<EOF\ntext\nEOF\ngit add .env'
must_block $BASH_HOOK "$(json_cmd "$env_after_heredoc")"       AURORA-BASH-04 'a real .env stage following a heredoc'
must_block $BASH_HOOK "$(json_cmd 'cd src && dotnet build')"                              AURORA-BASH-05 'dotnet after a cd'
must_block $BASH_HOOK "$(json_cmd 'for p in a b; do dotnet test $p; done')"                AURORA-BASH-05 'dotnet inside a loop body'

echo "PreToolUse(Bash) — must allow"
must_allow $BASH_HOOK "$(json_cmd 'git push -u origin task/B-09')"                        'push to a task branch'
must_allow $BASH_HOOK "$(json_cmd 'git push -u origin claude/multi-tenant-saas-erp-pv2nap')" 'push to the integration branch'
must_allow $BASH_HOOK "$(json_cmd 'source scripts/dev-env.sh && dotnet test Aurora.sln')" 'dotnet with dev-env sourced'
must_allow $BASH_HOOK "$(json_cmd 'grep -rn "dotnet" scripts/')"                          'the word dotnet inside another command'
must_allow $BASH_HOOK "$(json_cmd 'git log --oneline -20')"                               'an ordinary git read'
must_allow $BASH_HOOK "$(json_cmd 'git push -u origin claude/multi-tenant-saas-erp-pv2nap 2>&1 | tail -3')" 'a push whose output is piped'
must_allow $BASH_HOOK "$(json_cmd 'git push origin task/B-09 && echo done')"              'a push followed by another command'
must_allow $BASH_HOOK "$(json_cmd 'git push -u origin task/B-09 > /tmp/push.log 2>&1')"   'a push redirected to a file'
must_allow $BASH_HOOK "$(json_cmd 'git worktree remove /tmp/review-B-04 --force && git push -u origin task/B-09')" 'a --force elsewhere in the same command'
must_allow $BASH_HOOK "$(json_cmd 'git push -u origin task/B-09 && rm -f /tmp/x.force')" 'the word force after the push'

# A commit message or file body that *describes* a blocked command is not one. Both
# of these refused a legitimate commit before heredoc bodies were stripped.
msg_quoting_push=$'git commit -F - <<MSG\nfix: the guard refused git push -u origin main\nMSG'
must_allow $BASH_HOOK "$(json_cmd "$msg_quoting_push")"                                   'a commit message quoting a push'
msg_quoting_rm=$'git commit -F - <<MSG\nthe rule matches rm -rf docs/decisions/ADR-0001.md\nMSG'
must_allow $BASH_HOOK "$(json_cmd "$msg_quoting_rm")"                                     'a commit message quoting an ADR deletion'
body_quoting_force=$'cat > notes.md <<EOF\ngit push --force origin main\nEOF'
must_allow $BASH_HOOK "$(json_cmd "$body_quoting_force")"                                 'a heredoc body quoting a force push'
must_allow $BASH_HOOK "$(json_cmd 'ls docs/decisions/')"                                  'listing the ADR folder'

echo "PostToolUse(Write|Edit) — must block"
f=$(writecs src/Aurora.Tax/VatRule.cs <<'EOF'
public sealed class VatRule
{
    public decimal Rate(string country) => country == "SE" ? 0.25m : 0.20m;
}
EOF
); must_block $EDIT_HOOK "$(json_file "$f")" AURORA-EDIT-01 'country string comparison in core'

f=$(writecs src/Aurora.Sales/Order.cs <<'EOF'
public sealed class Order
{
    // TODO rename this before release
    public int Lines { get; }
}
EOF
); must_block $EDIT_HOOK "$(json_file "$f")" AURORA-EDIT-02 'TODO with no backlog id'

f=$(writecs src/Aurora.Ledger/Posting.cs <<'EOF'
using System;
public sealed class Posting
{
    public DateTimeOffset PostedAt { get; } = DateTimeOffset.UtcNow;
}
EOF
); must_block $EDIT_HOOK "$(json_file "$f")" AURORA-EDIT-03 'ambient clock in src'

f=$(writecs src/Aurora.SharedKernel/Percentage.cs <<'EOF'
public readonly struct Percentage
{
    public decimal Apply(decimal amount, int bp)
    {
        double factor = bp / 10000.0;
        return (decimal)((double)amount * factor);
    }
}
EOF
); must_block $EDIT_HOOK "$(json_file "$f")" AURORA-EDIT-04 'float/double in src (B-03 m-1 shape)'

f=$(writecs src/Aurora.Web/Secrets.cs <<'EOF'
public static class Secrets
{
    public const string Token = "ghp_abcdefghijklmnopqrstuvwxyz0123456789";
}
EOF
); must_block $EDIT_HOOK "$(json_file "$f")" AURORA-EDIT-05 'a credential-shaped literal'

echo "PostToolUse(Write|Edit) — must allow"
f=$(writecs src/Countries/Aurora.Countries.SE/SeVatRule.cs <<'EOF'
public sealed class SeVatRule
{
    public bool Handles(string country) => country == "SE";
}
EOF
); must_allow $EDIT_HOOK "$(json_file "$f")" 'the same comparison inside a Country Package'

f=$(writecs src/Aurora.Platform/SystemClock.cs <<'EOF'
using System;
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
EOF
); must_allow $EDIT_HOOK "$(json_file "$f")" 'the one clock implementation'

f=$(writecs src/Aurora.Sales/Invoice.cs <<'EOF'
public sealed class Invoice
{
    // TODO(B-21): add credit notes once the ledger contract lands
    public decimal Total { get; }
}
EOF
); must_allow $EDIT_HOOK "$(json_file "$f")" 'a TODO carrying a backlog id'

f=$(writecs tests/Aurora.Sales.Tests/MathHelper.cs <<'EOF'
public static class MathHelper
{
    public static double Tolerance = 0.0001;
}
EOF
); must_allow $EDIT_HOOK "$(json_file "$f")" 'a double under tests/'

f=$(writecs tests/Aurora.Platform.Tests/DbFixture.cs <<'EOF'
public static class DbFixture
{
    public const string Conn = "Host=localhost;Username=postgres;Password=postgres";
}
EOF
); must_allow $EDIT_HOOK "$(json_file "$f")" 'a Testcontainers placeholder password'

f=$(writecs src/Aurora.SharedKernel/Money.cs <<'EOF'
public readonly record struct Money(decimal Amount, Currency Currency)
{
    public Money Add(Money other) => this with { Amount = Amount + other.Amount };
}
EOF
); must_allow $EDIT_HOOK "$(json_file "$f")" 'ordinary compliant source'

echo
printf 'hook selftest: %d passed, %d failed (%d block cases, %d allow cases, %d total)\n' \
    "$pass" "$fail" "$blocked_cases" "$allowed_cases" "$((blocked_cases+allowed_cases))"
(( fail == 0 )) || exit 1
