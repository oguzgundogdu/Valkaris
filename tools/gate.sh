#!/bin/zsh
# The per-commit gate: clean build, Core tests in both cultures, the UI test in all four setups, and real processes over the network.
# Prints one line per step, and only failures beyond that. Exits non-zero if any step failed.
cd "${0:A:h}/.."
failed=0

report() { # name, exit code, output
    if [[ $2 -eq 0 ]]; then
        print -- "PASS $1${4:+ ($4)}"
    else
        failed=1
        print -- "FAIL $1"
        print -- "$3" | tail -40 | cut -c1-300
    fi
}

godot-mono --headless --import --path . >/dev/null 2>&1
dotnet clean Valkaris.sln -v q >/dev/null 2>&1

out=$(dotnet build Valkaris.csproj -v q -nologo 2>&1); code=$?
warnings=$(print -- "$out" | grep -E "warning " | grep -v "^$" | sort -u | wc -l | tr -d ' ')
report build $code "$(print -- "$out" | grep -E ' error |rror\(s\)' | sort -u)" "$warnings warnings"
[[ $warnings -ne 0 ]] && { failed=1; print -- "$out" | grep -E "warning " | sort -u | head -10 | cut -c1-300; }

core() { # name, culture
    local out code
    out=$(VALKARIS_TEST_CULTURE=$2 dotnet test Valkaris.sln --verbosity quiet --nologo 2>&1); code=$?
    report "$1" $code "$(print -- "$out" | grep -E '\[FAIL\]|Error Message|Assert\.| error |Failed!' -A2)" "$(print -- "$out" | grep -oE 'Passed: +[0-9]+' | tr -s ' ')"
}
core "core" ""
core "core tr-TR" "tr-TR"

ui() { # arguments for the game
    local out code
    out=$(godot-mono --headless --path . res://scenes/dev/UiTest.tscn -- "$@" 2>&1); code=$?
    [[ $out == *"[uitest] PASS"* ]] || code=1
    report "ui ${*:-default}" $code "$(print -- "$out" | grep -E '\[uitest\] FAIL|failure|rror CS|Unhandled')"
}
ui
ui --dev-culture=tr-TR
ui --dev-language=tr
ui --dev-language=tr --dev-culture=tr-TR

sweep() { # language, size
    local out code
    out=$(godot-mono --headless --path . --resolution $2 res://scenes/dev/UiTest.tscn -- --layout-sweep --dev-language=$1 2>&1); code=$?
    [[ $out == *"[uitest] PASS"* ]] || code=1
    report "layout $1 $2" $code "$(print -- "$out" | grep -E '\[uitest\] FAIL|failure|Unhandled')"
}
for language in en tr; do
    for size in 1024x640 1280x720 2560x1080; do
        sweep $language $size
    done
done

out=$(tools/nettest.sh 2>&1); code=$?
report "two-process network" $code "$out"

exit $failed
