# Local Testing Guide

## Testing Security Workflows Locally

### Prerequisites

```bash
# Install act (run GitHub Actions locally)
macOS:
brew install act

Linux:
bash <(curl https://raw.githubusercontent.com/nektos/act/master/install.sh)

Windows:
choco install act-cli
```

### Test 1: Run All Workflows

```bash
cd TimeTracker
act
```

### Test 2: Run Specific Workflow

```bash
# Test security workflow
act -j security-scanning

# Test build and tests
act -j build-and-test

# Test code quality
act -j code-quality

# Test branch naming
act -j branch-name-check
```

### Test 3: Test with Specific Event

```bash
# Simulate pull request event
act pull_request

# Simulate push event
act push

# Simulate with payload
act pull_request -e .github/pull_request_payload.json
```

### Test 4: Debug Workflow

```bash
# Run with debug output
act -v

# Run with shell debugging
act -j build-and-test --shell bash
```

### Test 5: Manual Branch Name Validation

```bash
# Test valid names
branch="feature/test-feature"
if [[ ! $branch =~ ^(feature|bugfix|hotfix|docs|refactor)/ ]]; then
    echo "Invalid"
else
    echo "Valid: $branch"
fi

# Test invalid names
branch="test-branch"
if [[ ! $branch =~ ^(feature|bugfix|hotfix|docs|refactor)/ ]]; then
    echo "Invalid: $branch"
else
    echo "Valid"
fi
```

### Test 6: Simulate PR with Issues

```bash
# 1. Create invalid branch
git checkout -b invalid-branch
echo "test" > test.txt
git add test.txt
git commit -m "Test commit"

# 2. Try to create PR (should fail branch check)

# 3. Fix with proper branch name
git checkout -b feature/test
git push origin feature/test
```

## Local SonarQube Testing

### Setup SonarQube Docker Container

```bash
# Pull SonarQube image
docker pull sonarqube:latest

# Run container
docker run -d --name sonarqube \
  -p 9000:9000 \
  -e SONAR_ES_BOOTSTRAP_CHECKS_DISABLED=true \
  sonarqube:latest

# Access at http://localhost:9000
# Default: admin/admin
```

### Run Local Analysis

```bash
# Install scanner
dotnet tool install --global dotnet-sonarscanner

# Begin analysis
sonarscanner begin /k:"TimeTracker" /d:sonar.host.url="http://localhost:9000" /d:sonar.login="admin"

# Build
dotnet build

# End analysis
sonarscanner end /d:sonar.login="admin"

# View results at http://localhost:9000
```

## Testing Security Scans

### Test Secret Detection

```bash
# Create test file with fake secret
echo 'api_key = "sk-1234567890abcdefghij"' > test_secret.cs

# Run trufflehog
trufflehog filesystem . --json

# Should detect the fake secret

# Clean up
rm test_secret.cs
```

### Test Dependency Scanning

```bash
# Check for vulnerabilities
dotnet list package --vulnerable

# Add vulnerable package (for testing only)
# Add to .csproj: <PackageReference Include="VulnerablePackage" Version="1.0.0" />

# Restore and check
dotnet restore
dotnet list package --vulnerable

# Remove vulnerable package
```

### Test Code Quality

```bash
# Run style cop
dotnet format --verify-no-changes --verbosity diagnostic

# Run tests with coverage
dotnet test /p:CollectCoverage=true /p:CoverageFormat=opencover
```

## CI/CD Pipeline Testing

### Full Integration Test

```bash
# 1. Create feature branch
git checkout -b feature/test-workflow

# 2. Make changes
echo "// Test" >> Program.cs

# 3. Commit
git add .
git commit -m "test: workflow integration"

# 4. Push
git push origin feature/test-workflow

# 5. Create PR (manually or via CLI)
gh pr create --title "Test Workflow" --body "Integration test"

# 6. Monitor GitHub Actions
gh run list
gh run view <run-id>

# 7. Verify all checks pass
```

## Troubleshooting Local Tests

### Issue: Act not finding workflows
```bash
# Solution: Ensure .github/workflows/ exists
ls -la .github/workflows/
```

### Issue: Docker/SonarQube not starting
```bash
# Solution: Check Docker daemon
docker ps

# Or use local installation
dotnet tool list --global
```

### Issue: Secret detection false positives
```bash
# Solution: Update patterns in .truffleconfig.json
```

## Continuous Testing

### Set up pre-commit hooks

```bash
# Create .git/hooks/pre-commit
#!/bin/bash
echo "Running pre-commit checks..."

# Run tests
dotnet test || exit 1

# Check for secrets
trufflehog filesystem . || exit 1

echo "✅ Pre-commit checks passed"
```

### Enable auto-testing on save

```bash
# Install file watcher
npm install -g watch-cli

# Watch and test
watch "dotnet test" src/
```

## Reporting Test Results

### Generate test report

```bash
dotnet test --logger "html;logfilename=test-report.html"

# View report
open test-report.html
```

### Generate coverage report

```bash
dotnet test /p:CollectCoverage=true /p:CoverageFormat=lcov

# View with visualizer
genhtml coverage.lcov -o coverage_html/
open coverage_html/index.html
```
