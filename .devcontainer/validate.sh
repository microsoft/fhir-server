#!/bin/bash

# FHIR DevContainer Validation Script
# This script helps validate that DevContainer configurations are properly set up

set -e

echo "🏥 FHIR Server DevContainer Validation"
echo "======================================"

# Function to check if required files exist
check_file() {
    local file="$1"
    local description="$2"
    
    if [[ -f "$file" ]]; then
        echo "✅ $description: $file"
        return 0
    else
        echo "❌ Missing $description: $file"
        return 1
    fi
}

# Function to check if directories exist
check_directory() {
    local dir="$1"
    local description="$2"
    
    if [[ -d "$dir" ]]; then
        echo "✅ $description: $dir"
        return 0
    else
        echo "❌ Missing $description: $dir"
        return 1
    fi
}

# Check main DevContainer directories
echo ""
echo "📁 Checking DevContainer directories..."
check_directory ".devcontainer/github-sql" "GitHub SQL DevContainer"
check_directory ".devcontainer/github-cosmos" "GitHub Cosmos DevContainer"
check_directory ".devcontainer/local-vscode" "Local VSCode DevContainer"

echo ""
echo "📄 Checking configuration files..."

# Check GitHub SQL container
check_file ".devcontainer/github-sql/devcontainer.json" "GitHub SQL DevContainer config"
check_file ".devcontainer/github-sql/docker-compose.yml" "GitHub SQL Docker Compose"
check_file ".devcontainer/github-sql/launch.json" "GitHub SQL Launch config"

# Check GitHub Cosmos container
check_file ".devcontainer/github-cosmos/devcontainer.json" "GitHub Cosmos DevContainer config"
check_file ".devcontainer/github-cosmos/docker-compose.yml" "GitHub Cosmos Docker Compose"
check_file ".devcontainer/github-cosmos/launch.json" "GitHub Cosmos Launch config"

# Check Local VSCode container
check_file ".devcontainer/local-vscode/devcontainer.json" "Local VSCode DevContainer config"
check_file ".devcontainer/local-vscode/docker-compose.yml" "Local VSCode Docker Compose"
check_file ".devcontainer/local-vscode/launch.json" "Local VSCode Launch config"

# Check supporting files
echo ""
echo "📖 Checking documentation and templates..."
check_file ".devcontainer/README.md" "DevContainer documentation"
check_file ".devcontainer/.env.template" "Environment template"
check_file ".devcontainer/Dockerfile" "DevContainer Dockerfile"

# Check library scripts
echo ""
echo "🔧 Checking library scripts..."
check_directory ".devcontainer/library-scripts" "Library scripts directory"
check_file ".devcontainer/library-scripts/fix-cert.sh" "Certificate fix script"

# Check for environment variables setup
echo ""
echo "🌍 Environment Variable Validation"
echo "=================================="

# Check if any .env files exist (they should be gitignored)
if find .devcontainer -name ".env" -type f | grep -q .; then
    echo "📝 Found .env files (this is good for local development)"
    find .devcontainer -name ".env" -type f | while read envfile; do
        echo "   • $envfile"
    done
else
    echo "ℹ️  No .env files found (use .env.template to create them)"
fi

# Validate gitignore
echo ""
echo "🔒 Security Check"
echo "================="
if grep -q ".devcontainer/.*/.env" .gitignore; then
    echo "✅ .env files are properly gitignored"
else
    echo "⚠️  Consider adding .devcontainer/**/.env to .gitignore"
fi

# Check for hardcoded passwords in configs
echo ""
echo "🔍 Security Scan - Checking for hardcoded passwords..."

# Define patterns to look for
password_patterns=("password=" "pwd=" "sa_password=" "secret=" "key=" "token=")

found_hardcoded=false
for container in "github-sql" "github-cosmos" "local-vscode"; do
    if [[ -d ".devcontainer/$container" ]]; then
        for pattern in "${password_patterns[@]}"; do
            if grep -ri "$pattern" ".devcontainer/$container"/ 2>/dev/null | grep -v "env:" | grep -v "\${" | grep -q .; then
                if ! $found_hardcoded; then
                    echo "⚠️  Potential hardcoded secrets found:"
                    found_hardcoded=true
                fi
                grep -ri "$pattern" ".devcontainer/$container"/ 2>/dev/null | grep -v "env:" | grep -v "\${"
            fi
        done
    fi
done

if ! $found_hardcoded; then
    echo "✅ No obvious hardcoded secrets detected"
fi

# Check Docker/VSCode extensions
echo ""
echo "🔌 Extension Check"
echo "=================="

# Check if GitHub Copilot is in the extensions
if grep -r "github.copilot" .devcontainer/*/devcontainer.json >/dev/null 2>&1; then
    echo "✅ GitHub Copilot extension configured"
else
    echo "❌ GitHub Copilot extension missing"
fi

if grep -r "ms-dotnettools.csdevkit" .devcontainer/*/devcontainer.json >/dev/null 2>&1; then
    echo "✅ C# Dev Kit extension configured"
else
    echo "❌ C# Dev Kit extension missing"
fi

# Check Docker availability
echo ""
echo "🐳 Docker Environment Check"
echo "==========================="

if command -v docker >/dev/null 2>&1; then
    echo "✅ Docker is available"
    if docker info >/dev/null 2>&1; then
        echo "✅ Docker daemon is running"
    else
        echo "⚠️  Docker daemon may not be running"
    fi
else
    echo "❌ Docker not found - required for DevContainers"
fi

# Summary
echo ""
echo "📋 Validation Summary"
echo "===================="

if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then
    echo "🎉 DevContainer setup appears to be valid!"
    echo ""
    echo "Next steps:"
    echo "1. Copy .devcontainer/.env.template to .devcontainer/<container-name>/.env"
    echo "2. Customize the .env file with your settings"
    echo "3. Open one of the DevContainer folders in VSCode"
    echo "4. Select 'Reopen in Container' when prompted"
    echo ""
    echo "Available containers:"
    echo "• .devcontainer/github-sql/     - For GitHub CI with SQL Server"
    echo "• .devcontainer/github-cosmos/  - For GitHub CI with Cosmos DB"
    echo "• .devcontainer/local-vscode/   - For local development"
else
    echo "⚠️  Setup validation completed with warnings. Check Docker installation."
fi

echo ""
echo "For detailed instructions, see: .devcontainer/README.md"