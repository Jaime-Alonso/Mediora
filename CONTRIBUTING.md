# Contributing to Mediora

Thanks for your interest in improving Mediora.

## Ground Rules

- Be respectful and professional in all discussions
- Keep pull requests focused on one logical change
- Prefer small, reviewable commits with clear messages
- Add or update tests for behavior changes
- Update documentation when API or behavior changes

## Getting Started

1. Fork the repository and create a feature branch.
2. Implement your change following existing project conventions.
3. Add tests that prove the change and prevent regressions.
4. Run build and test locally before opening a PR.
5. Open a pull request using the provided template.

## Pull Request Checklist

- [ ] The change is scoped and clearly described
- [ ] Public API changes are justified and documented
- [ ] Relevant tests are added/updated and passing
- [ ] Documentation is updated where needed
- [ ] No unrelated refactors are included

## Repository protection

- Branch protection should require the `CI / build-test-pack` status check before merge.
- Dismiss stale approvals when new commits are pushed to a pull request.
- Restrict direct pushes to the default branch.

## Design Principles

Mediora prioritizes:

- Predictable behavior over clever abstractions
- Backward compatibility and semantic versioning discipline
- Performance awareness without sacrificing clarity
- Developer experience supported by clear docs and examples

## Reporting Bugs and Requesting Features

- Use GitHub issue templates for bug reports and feature requests
- Provide reproducible steps, expected behavior, and environment details
- For security issues, do not open a public issue; follow `SECURITY.md`

## Code of Conduct

By participating in this project, you agree to follow `CODE_OF_CONDUCT.md`.
