# CLAUDE.md

VintageStoryModKit provides shared build rules, dependencies and development
helpers for Vintage Story modding. Use the [documentation index](docs/README.md)
for package and helper usage. Read [CONTRIBUTING.md](CONTRIBUTING.md) for
package maintenance and validation.

## Code and dependencies

- Keep changes scoped. Follow existing patterns before introducing helpers or
  abstractions. Avoid unrelated formatting, renaming and cleanup.
- Comments explain non-obvious constraints, invariants or workarounds. Do not
  restate the code or describe earlier implementations.
- Verify dependency behavior against the pinned source. Distinguish upstream
  defects from VSMK packaging adaptations, and state what remains unverified.
- Handle failure paths as well as success. Downloads, extraction and packaging
  must clean up temporary resources and report enough context to diagnose
  errors.
- Pass untrusted GitHub Actions inputs to shell steps through environment
  variables, never direct expression interpolation into command text.

## Validation

- Run the checks relevant to the change before calling it complete. Do not rely
  on CI alone.
- Validate package and build-rule changes through installation and an affected
  consumer. Inspect generated metadata, deployed files or archive contents when
  those outputs change.
- Tests must exercise real behavior and assert observed results. Compilation or
  a successful command alone does not establish that the result is correct.

## Commits

- Use Conventional Commits. Describe the problem and resulting behavior for a
  reviewer evaluating the current diff. Omit session history and Git mechanics.
- Fix failures from the pre-commit checks instead of skipping them.
