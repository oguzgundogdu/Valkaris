Always follow GDD.md

## Running tests

- Run the per-commit gate with `tools/gate.sh` (clean build, Core tests in both cultures, the four UI test setups) through a subagent on a cheaper model (Haiku). The subagent returns only a short summary: pass/fail per step, counts, and for failures the failing test names with the relevant error lines. Never paste full logs into the main context.
- Use quiet or minimal output flags for builds and tests wherever possible, such as `dotnet test --verbosity quiet`.
- Investigate and fix failures in the main context, with the full output of just the failing tests.
