#!/bin/zsh
# A real match over ENet on this machine: a host and two clients as separate processes.
# One client leaves, an AI takes over, the rest surrender, and the view each client holds must match the host's.
cd "${0:A:h}/.."
port=$((24600 + RANDOM % 300))
logs=$(mktemp -d)
run() { perl -e 'alarm 90; exec @ARGV' godot-mono --headless --path . res://scenes/dev/NetTest.tscn -- --net-role=$1 --net-port=$port >$logs/$1.log 2>&1; print $? >$logs/$1.code; }

run host &
for i in {1..300}; do
    grep -q '\[nettest\] ready' $logs/host.log 2>/dev/null && break
    perl -e 'select(undef,undef,undef,0.1)'
done
run stay &
run leave &
wait

failed=0
complain() { print -- "$1"; failed=1; }
for role in host stay leave; do
    [[ $(<$logs/$role.code) == 0 ]] || complain "$role exited with $(<$logs/$role.code): $(grep nettest $logs/$role.log | tail -3 | tr '\n' ' ') $(grep -E 'ERROR|Unhandled' $logs/$role.log | head -3 | cut -c1-200)"
done
grep -q '\[nettest\] takeover' $logs/host.log || complain "No AI took over from the player who left"
line=$(grep '\[nettest\] view' $logs/stay.log | tail -1)
[[ -n $line ]] || complain "The client printed no final view"
grep -qF "$line" $logs/host.log || complain "The client's final view differs from the host's: $line"
grep -q 'event PlayerDisconnected' $logs/stay.log || complain "The remaining client didn't hear of the disconnect"

rm -rf $logs
exit $failed
