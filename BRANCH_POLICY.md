# Branch Protection & PR Policy Configuration

## Branch Naming Conventions

All contributors **MUST** follow these naming conventions when creating branches:

### Feature Branches
```
feature/short-description
feature/user-authentication
feature/add-time-tracking-dashboard
```

### Bug Fix Branches
```
bugfix/issue-description
bugfix/fix-login-timeout
bugfix/null-reference-exception
```

### Hotfix Branches (Critical Production Fixes)
```
hotfix/critical-issue-description
hotfix/security-patch
```

### Documentation Branches
```
docs/update-readme
docs/api-documentation
```

### Refactoring Branches
```
refactor/code-module-name
refactor/database-layer
```

### General Rules
- Use lowercase letters
- Use hyphens (-) to separate words, NOT underscores
- Keep branch names concise and descriptive
- Start with the prefix (feature/, bugfix/, etc.)

## PR Submission Requirements

Before submitting a PR, ensure:

1. ✅ Your branch follows naming conventions
2. ✅ All tests pass locally (`dotnet test`)
3. ✅ Code builds without errors (`dotnet build`)
4. ✅ No security vulnerabilities in dependencies
5. ✅ Proper documentation is included
6. ✅ PR description fills out the template completely

## Automatic PR Rejection Rules

PRs will be **automatically rejected/blocked** if:

### Code Quality Issues
- ❌ Tests fail
- ❌ Build fails
- ❌ Code coverage drops below minimum threshold
- ❌ SonarQube quality gate fails

### Security Vulnerabilities
- ❌ Dependency vulnerabilities detected
- ❌ Secrets or credentials exposed in code
- ❌ OWASP security issues detected
- ❌ Code injection vulnerabilities

### Branch/Naming Issues
- ❌ Branch name doesn't follow conventions (must start with feature/, bugfix/, etc.)
- ❌ Commits not rebased on latest main/master
- ❌ Merge conflicts unresolved

### Documentation & Review Issues
- ❌ PR description is empty or incomplete
- ❌ No linked issues or context provided
- ❌ Less than 1 required review approval
- ❌ Requested changes not addressed

## Automated Checks (GitHub Actions)

The following checks run automatically:

- **Build Test:** Verifies the code compiles
- **Unit Tests:** Runs all unit tests
- **Code Quality:** SonarQube analysis
- **Security Scan:** Dependency and vulnerability checks
- **Linting:** Code style verification
- **Documentation:** Checks for proper comments and documentation

## Manual Review Process

Even if automated checks pass, a maintainer must:

1. Review code quality and architecture
2. Verify business logic correctness
3. Check for performance implications
4. Approve or request changes
5. Merge or reject the PR

## Security Scanning Details

### Dependency Vulnerabilities
- Checked using GitHub Dependabot
- Reports any known CVEs in dependencies
- Blocks merge if critical vulnerabilities found

### Code Vulnerabilities
- SAST (Static Application Security Testing)
- Detects common security issues:
  - SQL Injection risks
  - XSS vulnerabilities
  - Insecure cryptography
  - Hardcoded secrets/credentials
  - Path traversal issues

### Secret Detection
- Scans for exposed:
  - API keys and tokens
  - Database credentials
  - Private keys
  - Authentication tokens

## Timeline for Review

- Small PRs (< 100 lines): 24-48 hours
- Medium PRs (100-500 lines): 2-3 days
- Large PRs (> 500 lines): 3-5 days or may be asked to split

## Questions or Issues?

- Check existing issues and discussions
- Open a new discussion if you have questions
- Contact maintainers for clarification on requirements
