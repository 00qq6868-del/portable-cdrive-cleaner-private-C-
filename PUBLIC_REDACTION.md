# Public Repository Redaction Notes

## Current Policy
- The repository is public, so committed files must not contain API keys, access tokens, passwords, private keys, personal screenshots, or machine-specific absolute paths.
- Local QA screenshots and raw run artifacts stay on the tester machine under `artifacts/icon-qa/` and are ignored by Git.
- Public QA records should be summarized as metrics and conclusions, not raw screenshots that expose usernames, installed software, or local paths.

## Redaction Completed
- Removed tracked raw QA screenshot artifacts from the public Git index.
- Added ignore rules for `artifacts/icon-qa/**`.
- Replaced public documentation references to local absolute paths with placeholders such as `<LOCAL_REPO_PATH>`, `<LOCAL_WORKSPACE>`, `<INSTALLED_EXE_PATH>`, and `<USER_PROFILE>`.
- Updated checkpoint tooling so future public checkpoints write redacted path placeholders instead of the current machine path.
- Updated QA tooling defaults so it resolves paths from the repository root instead of hardcoding a local workspace path.

## Latest Scan Scope
- Scanned current tracked text files outside build output and local artifacts for common secret patterns:
  - GitHub tokens
  - OpenAI-style `sk-` tokens
  - private key headers
  - common API-key / token / password assignment patterns
  - local path markers for the tester user profile, install directory, and workspace

## Latest Result
- No obvious token, private key, or password pattern remains in the current public working tree after redaction.
- Gitleaks 8.30.1 was installed and used to scan the Git repository history; 34 commits were scanned and no leaks were found.
- Historical Git commits may still contain previously pushed raw QA artifacts and local paths unless the repository history is rewritten and force-pushed. Do not run history rewrite casually; it should be treated as a separate destructive maintenance task.
