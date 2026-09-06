# Security Configuration Setup Guide

Complete step-by-step guide to configure all security features for TimeTracker.

## Table of Contents

1. [GitHub Settings](#github-settings)
2. [Workflow Configuration](#workflow-configuration)
3. [Code Analysis Setup](#code-analysis-setup)
4. [Secret Management](#secret-management)
5. [Testing Locally](#testing-locally)
6. [Verification Checklist](#verification-checklist)

---

## GitHub Settings

### Step 1: Enable Secret Scanning

1. Go to **Settings** → **Security & analysis**
2. Under "Secret scanning":
   - ✅ Enable "Secret scanning"
   - ✅ Enable "Push protection"
3. Click **Save**

**What it does**: GitHub will scan your code for exposed secrets (API keys, tokens, credentials).

### Step 2: Enable Dependabot

1. Go to **Settings** → **Security & analysis**
2. Under "Dependabot":
   - ✅ Enable "Dependabot alerts"
   - ✅ Enable "Dependabot security updates"
   - ✅ Enable "Dependabot version updates"
3. Create `.github/dependabot.yml`:

```yaml
version: 2
updates:
  # NuGet dependencies
  - package-ecosystem: "nuget"
    directory: "/"
    schedule:
      interval: "weekly"
      day: "monday"
      time: "03:00"
    allow:
      - dependency-type: "direct"
      - dependency-type: "indirect"
    reviewers:
      - "DipeshPansuriya"
    assignees:
      - "DipeshPansuriya"
    labels:
      - "dependencies"
      - "security"
    commit-message:
      prefix: "chore"
      include: "scope"
```

### Step 3: Configure Branch Protection

1. Go to **Settings** → **Branches**
2. Click **Add a rule** (or edit existing)
3. Set **Branch name pattern**: `master`
4. Configure rules:

```
✅ Require a pull request before merging
   ✅ Require approvals: 1
   ✅ Require review from code owners
   ✅ Dismiss stale PR approvals
   ✅ Require approval of the most recent reviewable push

✅ Require status checks to pass before merging
   ✅ Require branches to be up to date before merging
   ✅ Selected checks:
       - build-and-test
       - security-scanning
       - code-quality
       - dependency-check
       - branch-name-check
       - pr-validation

✅ Require commits to be signed

✅ Dismiss stale pull request approvals when new commits are pushed

✅ Require conversation resolution before merging

✅ Include administrators

✅ Restrict who can push to matching branches
   ✅ Allow specified actors to bypass required pull requests
   - Add yourself as maintainer

✅ Allow auto-merge

✅ Automatically delete head branches
```

5. Click **Save**

### Step 4: Enable Code Scanning (CodeQL)

1. Go to **Settings** → **Security & analysis**
2. Under "Code scanning":
   - Click **Set up tool** → **CodeQL**
   - Choose **Create configuration file**
3. GitHub will create `.github/workflows/github-codeql-analysis.yml`
4. Customize if needed and commit

### Step 5: Enable Discussions

1. Go to **Settings** → **Features**
2. ✅ Check **Discussions**
3. Go to **Discussions** tab
4. Set up categories:
   - Announcements
   - General
   - Q&A
   - Security

---

## Workflow Configuration

### Adding GitHub Actions Workflows

Create these files in your repository:

#### `.github/workflows/security-and-quality.yml`

```yaml
# [See the full workflow file from the repository]
```

#### `.github/workflows/ai-code-review.yml`

```yaml
# [See the full workflow file from the repository]
```

#### `.github/workflows/branch-protection.yml`

```yaml
name: Branch Protection Checks

on:
  pull_request:
    types: [opened, synchronize, reopened]

jobs:
  check-branch-name:
    runs-on: ubuntu-latest
    steps:
      - name: Validate branch naming
        run: |
          BRANCH="${{ github.head_ref }}"
          if [[ ! "$BRANCH" =~ ^(feature|bugfix|hotfix|docs|refactor)/ ]]; then
            echo "❌ Branch must start with: feature/, bugfix/, hotfix/, docs/, or refactor/"
            echo "Current: $BRANCH"
            exit 1
          fi
          echo "✅ Branch name valid: $BRANCH"
```

---

## Code Analysis Setup

### SonarCloud Integration

1. **Create SonarCloud Account**
   - Go to https://sonarcloud.io
   - Click **Log in with GitHub**
   - Authorize access

2. **Create Organization**
   - Skip organization creation (use personal)
   - Or create new organization

3. **Add Repository**
   - Click **Analyze new project**
   - Select TimeTracker repository
   - Click **Set up**

4. **Get Token**
   - Go to https://sonarcloud.io/account/security
   - Generate new token (copy the token)

5. **Add to GitHub Secrets**
   - Go to TimeTracker Settings → **Secrets & variables** → **Actions**
   - Click **New repository secret**
   - Name: `SONAR_TOKEN`
   - Value: (paste the token)
   - Click **Add secret**

6. **Create `sonar-project.properties`**

```properties
sonar.projectKey=DipeshPansuriya_TimeTracker
sonar.organization=dipeshpansuriya-1

# Encoding of the source code
sonar.sourceEncoding=UTF-8

# C# settings
sonar.lang=cs
sonar.exclusions=**/bin/**,**/obj/**,**/test/**
sonar.coverage.exclusions=**/test/**
```

### CodeRabbit AI Integration (Optional)

1. **Install CodeRabbit App**
   - Go to https://github.com/marketplace/coderabbit-ai
   - Click **Install**
   - Select your account
   - Select TimeTracker repository
   - Click **Install**

2. **Configuration** (`.coderabbit.yaml`)

```yaml
rules:
  - type: pattern
    pattern: '(password|secret|api_key|token)\s*='
    severity: high
    message: 'Potential hardcoded secret detected'

reviews:
  enabled: true
  auto_review: true
  auto_review_on_pr_opening: true
  request_changes_on_findings: true
  auto_review_fallback_to_summary: true
```

---

## Secret Management

### GitHub Secrets Setup

1. Go to **Settings** → **Secrets & variables** → **Actions**
2. Click **New repository secret** for each:

| Name | Description | Example |
|------|-------------|----------|
| `SONAR_TOKEN` | SonarCloud token | (from sonarcloud.io) |
| `OPENAI_API_KEY` | OpenAI API key (for AI review) | sk-... |
| `DATABASE_PASSWORD` | Database password | (use env var, not hardcoded) |

### Environment-based Secrets

**Development Environment** (`.env.development`)
```
DATABASE_HOST=localhost
DATABASE_PORT=1433
DATABASE_USER=sa
DATABASE_PASSWORD=YourPassword123!
API_KEY=dev-key-12345
```

**Never commit this file!** Add to `.gitignore`:
```
.env*
!.env.example
```

**Production Secrets** - Use GitHub Secrets or Azure Key Vault

---

## Testing Locally

### Prerequisite: Install Tools

```bash
# .NET SDK
download from https://dotnet.microsoft.com/download

# GitHub CLI
brew install gh  # macOS
choco install gh  # Windows
sudo apt install gh  # Linux

# Docker (for local testing)
download from https://www.docker.com/products/docker-desktop
```

### Test 1: Build & Unit Tests

```bash
cd TimeTracker

# Restore dependencies
dotnet restore

# Build project
dotnet build --configuration Release

# Run tests
dotnet test --no-build --verbosity normal

# Expected output:
# Passed: ✅
# Failed: ❌ (fix before pushing)
```

### Test 2: Code Quality (Local)

```bash
# Install SonarScanner
dotnet tool install --global dotnet-sonarscanner

# Run local analysis
sonarscanner begin /k:"TimeTracker" /o:"your-org"
dotnet build
sonarscanner end

# View report at:
# https://sonarcloud.io/dashboard?id=TimeTracker
```

### Test 3: Dependency Vulnerability Check

```bash
# List vulnerable packages
dotnet list package --vulnerable

# Update packages
dotnet package update
```

### Test 4: Secret Detection (Local)

```bash
# Install TruffleHog
pip install trufflehog

# Scan repository
trufflehog filesystem . --json

# Expected: No secrets found ✅
```

### Test 5: Branch Naming Validation

```bash
# Test 1: Valid feature branch
git checkout -b feature/my-new-feature
# Should pass validation ✅

# Test 2: Invalid branch name
git checkout -b my-invalid-branch
# Should fail on PR creation ❌
```

### Test 6: GitHub Actions Locally

```bash
# Install act (run GitHub Actions locally)
brew install act

# Run security workflow
act -j build-and-test

# Run all workflows
act

# Run specific event
act pull_request
```

---

## Verification Checklist

### GitHub Settings

- [ ] Secret scanning enabled
- [ ] Push protection enabled
- [ ] Dependabot alerts enabled
- [ ] Dependabot security updates enabled
- [ ] Branch protection configured
- [ ] CodeQL enabled
- [ ] Status checks required
- [ ] PR approvals required (minimum 1)
- [ ] Commit signatures required (optional)
- [ ] Discussions enabled

### Workflows

- [ ] `.github/workflows/security-and-quality.yml` created
- [ ] `.github/workflows/ai-code-review.yml` created (if using AI)
- [ ] `.github/workflows/dependabot.yml` created
- [ ] Workflows pass when run

### Code Analysis

- [ ] SonarCloud configured
- [ ] `SONAR_TOKEN` secret added
- [ ] `sonar-project.properties` created
- [ ] CodeRabbit app installed (optional)
- [ ] `.coderabbit.yaml` configured (optional)

### Security Documents

- [ ] `SECURITY.md` created
- [ ] `CONTRIBUTING.md` created
- [ ] `CODE_OF_CONDUCT.md` created
- [ ] `BRANCH_POLICY.md` created
- [ ] `.gitignore` includes secrets

### Local Testing

- [ ] Build succeeds locally
- [ ] All unit tests pass
- [ ] No vulnerable dependencies
- [ ] No secrets detected
- [ ] Code quality meets standards

### First PR Test

1. Create a test branch: `feature/test-workflow`
2. Make a small change
3. Push and create PR
4. Verify:
   - [ ] Branch naming check passes
   - [ ] PR template displayed
   - [ ] Build and test pass
   - [ ] Code quality check passes
   - [ ] Security scan completes
   - [ ] AI review posted (if enabled)
   - [ ] Status checks all green ✅

---

## Troubleshooting

### Issue: Workflow not triggering

```bash
# Solution: Check workflow file syntax
gh workflow view security-and-quality.yml

# Enable logging
gh debug
```

### Issue: SonarCloud not analyzing

```bash
# Solution: Verify token and project key
echo $SONAR_TOKEN  # Should not be empty
cat sonar-project.properties  # Check project key
```

### Issue: Branch protection blocking merge

- Ensure all status checks pass ✅
- Ensure PR is approved by at least 1 reviewer ✅
- Ensure branch is up to date with main ✅
- Dismiss stale reviews if needed

### Issue: Secret detected but not a real secret

```bash
# Add false positive exception in `.gitignore`
# or
# Go to Settings → Secret scanning → Bypass alerts (if admin)
```

---

## Next Steps

1. ✅ Follow this guide step-by-step
2. ✅ Test locally first
3. ✅ Create a test PR
4. ✅ Verify all checks pass
5. ✅ Merge and celebrate! 🎉
6. ✅ Keep up with security updates

**Contact**: For issues, open a discussion in the repository or email dipeshpansuriya@ymail.com
