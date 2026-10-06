---
name: crap4net
description: "Calculates cyclomatic complexity and CRAP scores for C#/.NET methods by combining Roslyn-based complexity analysis with test coverage, producing a worst-first report of complex, under-tested code. Use when the user asks for a CRAP report, cyclomatic complexity, risky or under-tested methods, or code quality metrics on a C# or .NET project."
---

# crap4net: CRAP metric for C#

Scores every C# method, constructor, accessor and operator with
`CRAP = CC² × (1 − coverage)³ + CC`, where coverage comes from running the project's tests.

## Running it

From the project, solution or repository directory:

```bash
crap4net                  # whole tree under the current directory
crap4net --changed        # only files changed according to git status
crap4net src/Api          # specific files or directories
crap4net --json           # machine-readable; progress goes to stderr
```

If `crap4net` isn't on the PATH, run it from a checkout of this repository:
`dotnet run --project <crap4net>/src/Crap4Net -- <args>`, from the target project's directory.

crap4net runs the relevant test projects itself with `dotnet test` and coverage on. That takes
as long as the test suite does, and any failing test aborts the run with exit code 3: report the
failing test rather than retrying.

## Reading the result

- Exit 0: every score is at or below the threshold (default 8). Exit 2: at least one exceeds it.
  Exit 1 is a usage error, 3 an analysis failure.
- `N/A` coverage means no test project covers that code, or no coverage report was found. The
  warning on stderr says which.
- CRAP ≤ CC means the method is fully covered: only refactoring lowers it. A score far above CC
  means missing tests, which are usually the cheapest fix.

| CRAP | Meaning |
|---|---|
| 1–5 | Fine |
| 5–30 | Worth refactoring or testing more |
| 30+ | High risk: complex and barely tested |

## Fixing a high score

1. Look at `Cov%`. Below about 80%, add tests for the uncovered branches first.
2. If it's well covered and still over the threshold, the complexity itself is the problem. Extract
   methods, replace conditionals with lookup tables or polymorphism, and split validation from work.
3. Re-run `crap4net <file>` to confirm the score dropped.
