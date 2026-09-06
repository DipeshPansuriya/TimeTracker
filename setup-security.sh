# Quick Security Setup Script

> Automated setup script for security features (macOS/Linux)

```bash
#!/bin/bash

echo "🔒 TimeTracker Security Setup"
echo "============================="

# Colors
GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check prerequisites
echo -e "\n${YELLOW}Checking prerequisites...${NC}"

if ! command -v git &> /dev/null; then
    echo -e "${RED}❌ Git not found${NC}"
    exit 1
fi
echo -e "${GREEN}✅ Git found${NC}"

if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}❌ .NET SDK not found${NC}"
    exit 1
fi
echo -e "${GREEN}✅ .NET SDK found${NC}"

# Create necessary files
echo -e "\n${YELLOW}Creating configuration files...${NC}"

# Create .gitignore updates
echo "
echo '# Security - Environment variables' >> .gitignore
echo '.env' >> .gitignore
echo '.env.local' >> .gitignore
echo '.env.*.local' >> .gitignore
echo 'appsettings.*.json' >> .gitignore
echo -e "${GREEN}✅ .gitignore updated${NC}"

# Build and test
echo -e "\n${YELLOW}Running build and tests...${NC}"
dotnet restore
if dotnet build --configuration Release; then
    echo -e "${GREEN}✅ Build successful${NC}"
else
    echo -e "${RED}❌ Build failed${NC}"
    exit 1
fi

if dotnet test --no-build; then
    echo -e "${GREEN}✅ Tests passed${NC}"
else
    echo -e "${RED}❌ Tests failed${NC}"
    exit 1
fi

# Check for secrets
echo -e "\n${YELLOW}Checking for exposed secrets...${NC}"
if pip install trufflehog &> /dev/null; then
    if trufflehog filesystem . --json | grep -q 'secret'; then
        echo -e "${RED}❌ Potential secrets found!${NC}"
        exit 1
    fi
    echo -e "${GREEN}✅ No secrets detected${NC}"
fi

# Dependency check
echo -e "\n${YELLOW}Checking for vulnerable dependencies...${NC}"
if dotnet list package --vulnerable | grep -i 'vulnerable'; then
    echo -e "${RED}❌ Vulnerable dependencies found${NC}"
    echo "Run: dotnet package update"
    exit 1
fi
echo -e "${GREEN}✅ No vulnerable dependencies${NC}"

echo -e "\n${GREEN}✅ Security setup complete!${NC}"
echo -e "\n${YELLOW}Next steps:${NC}"
echo "1. Add SONAR_TOKEN secret to GitHub"
echo "2. Configure branch protection in Settings"
echo "3. Create test PR to verify workflows"
echo "4. Review SECURITY.md for guidelines"
```

**Usage**:
```bash
chmod +x setup-security.sh
./setup-security.sh
```
