# Git Commit Message Guidelines

## Format

Use Conventional Commits:
`<type>[optional scope][!]: <description>`

## Language

- Always write commit messages in English.
- Use clear, concise and technically accurate wording.

## Types

- `feat`: Add new functionality.
- `fix`: Fix a bug.
- `docs`: Change documentation.
- `style`: Change formatting without affecting behavior.
- `refactor`: Restructure code without changing behavior.
- `perf`: Improve performance.
- `test`: Add or modify tests.
- `build`: Change build configuration or dependencies.
- `ci`: Change CI/CD configuration.
- `chore`: Perform maintenance tasks.
- `revert`: Revert a previous commit.

## Subject

- Keep the subject concise, ideally 50 characters or fewer.
- Use imperative wording, e.g. "add", "fix", "remove".
- Do not end the subject with a period.
- Describe the actual purpose of the change.
- Use a scope when it improves clarity.
- Avoid vague descriptions such as "update code" or "fix stuff".

## Body

- Add a body only when additional context is useful.
- Explain why the change was necessary, not just what changed.
- Separate the subject and body with a blank line.
- Keep the body concise and factual.

## Breaking Changes

- Mark breaking changes with `!` after the type or scope.
- Explain the incompatibility in a `BREAKING CHANGE:` footer.

## Rules

- Base every commit message on the actual changes.
- Never invent changes, tests, issue numbers or motivations.
- Prefer one logical change per commit.
- Do not include unnecessary file lists.
- Use issue references only when they are known and relevant.
- Follow the conventions already established in the repository
  if they are more specific than these defaults.

## Examples

- `feat(auth): add password reset`
- `fix(parser): handle empty input`
- `docs(readme): clarify installation steps`
- `refactor(api): extract request handler`
- `perf(rendering): cache parsed styles`
