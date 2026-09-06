# Contributing to TimeTracker

Thank you for your interest in contributing to TimeTracker! We welcome contributions from everyone. This document provides guidelines and instructions for contributing to the project.

## Code of Conduct

We are committed to providing a welcoming and inspiring community for all. Please read and follow our Code of Conduct (based on the Contributor Covenant) when participating in this community.

## How to Contribute

### Reporting Bugs

Before creating bug reports, please check the issue list as you might find out that you don't need to create one. When you are creating a bug report, please include as many details as possible:

- **Use a clear and descriptive title**
- **Describe the exact steps which reproduce the problem** in as many details as possible
- **Provide specific examples to demonstrate the steps**
- **Describe the behavior you observed** after following the steps
- **Explain which behavior you expected to see instead** and why
- **Include screenshots and animated GIFs if possible**
- **Include your environment details** (OS, .NET version, etc.)

### Suggesting Enhancements

Enhancement suggestions are tracked as GitHub issues. When creating an enhancement suggestion, please include:

- **Use a clear and descriptive title**
- **Provide a step-by-step description** of the suggested enhancement
- **Provide specific examples to demonstrate the steps**
- **Describe the current behavior** and **the expected behavior**
- **Explain why this enhancement would be useful**

### Pull Requests

- Follow the C# coding standards and conventions used in the project
- Write clear, descriptive commit messages
- Include appropriate comments and documentation
- Update relevant documentation for new features
- Add tests for new functionality
- Ensure all tests pass before submitting the PR
- Keep PRs focused on a single feature or bug fix

#### Steps to Submit a Pull Request:

1. **Fork the repository** to your GitHub account
2. **Clone the fork** locally:
   ```bash
   git clone https://github.com/YOUR-USERNAME/TimeTracker.git
   cd TimeTracker
   ```
3. **Create a feature branch**:
   ```bash
   git checkout -b feature/your-feature-name
   ```
4. **Make your changes** and commit with clear messages:
   ```bash
   git commit -m "Add feature: description of what you added"
   ```
5. **Push to your fork**:
   ```bash
   git push origin feature/your-feature-name
   ```
6. **Create a Pull Request** from your fork to the main repository
   - Provide a clear title and description
   - Reference any related issues
   - Include screenshots/GIFs if UI changes are made

## Development Setup

### Prerequisites
- .NET SDK (version specified in the project)
- Visual Studio, Visual Studio Code, or your preferred IDE
- Git

### Building and Testing
```bash
# Clone the repository
git clone https://github.com/DipeshPansuriya/TimeTracker.git
cd TimeTracker

# Build the project
dotnet build

# Run tests
dotnet test
```

## Coding Standards

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Use meaningful variable and method names
- Keep methods focused and reasonably sized
- Add XML comments for public APIs
- Ensure code is properly formatted before committing

## Commit Messages

Write clear, descriptive commit messages that explain the "why" behind the changes:

- Use imperative mood ("Add feature" not "Added feature")
- Limit the first line to 72 characters
- Reference issues and pull requests liberally in the body
- Example:
  ```
  Add user authentication feature

  - Implement login/logout functionality
  - Add JWT token validation
  - Create authentication middleware

  Closes #123
  ```

## Review Process

All contributions will be reviewed by maintainers. We aim to provide feedback within a reasonable timeframe. Please be patient and constructive during the review process.

## License

By contributing to TimeTracker, you agree that your contributions will be licensed under the same license as the project.

## Questions?

Feel free to open an issue with the `question` label or reach out to the maintainers.

Happy contributing! 🎉
