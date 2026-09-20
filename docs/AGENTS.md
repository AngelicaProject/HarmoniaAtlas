# Documentation guidance

Documentation is reader-facing, not task-facing. A sentence belongs in canonical documentation only if it remains useful to a reader who has never seen the prompt, task, PR, or implementation history.

Describe current behavior, not the implementation process. Format documentation should explain the persisted format, field semantics, validation, compatibility, identity/hash rules, and useful examples.

Do not copy acceptance criteria, non-goals, rejected alternatives, prompt constraints, PR rationale, or proof-of-compliance statements into canonical documentation unless they are necessary to understand, use, implement, or maintain the current system. Task boundaries and rejected approaches belong in the PR description.

Do not reserve persisted fields, enum variants, or documentation sections for speculative future features. Add them when their contract is actually designed.
