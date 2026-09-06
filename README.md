# ⏱️ TimeTracker

> A comprehensive, open-source time tracking application built with C# and .NET for managing work hours, projects, and productivity.

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)]() 
[![License](https://img.shields.io/badge/license-MIT-blue)]() 
[![C#](https://img.shields.io/badge/language-C%23-239120?logo=csharp)]() 
[![.NET](https://img.shields.io/badge/.NET-6.0+-512BD4?logo=dotnet)]() 
[![Open Source](https://img.shields.io/badge/Open%20Source-%E2%9C%93-brightgreen)]() 
[![Community](https://img.shields.io/badge/Community-Driven-ff69b4)](/CONTRIBUTING.md)

## 📋 Table of Contents

- [Features](#features)
- [Quick Start](#quick-start)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
- [API Documentation](#api-documentation)
- [Architecture](#architecture)
- [Contributing](#contributing)
- [Security](#security)
- [License](#license)
- [Support](#support)

---

## ✨ Features

### Core Functionality
- ⏱️ **Time Tracking** - Track work hours with start/stop functionality
- 📊 **Project Management** - Organize tasks by projects
- 👥 **Team Collaboration** - Share projects and track team productivity
- 📈 **Analytics & Reports** - Generate detailed time reports and insights
- 🏷️ **Task Tagging** - Organize tasks with custom tags
- 💾 **Data Export** - Export data to CSV, PDF, and other formats

### Security Features
- 🔐 **User Authentication** - Secure login with password hashing
- 🛡️ **Role-Based Access Control** - Different permission levels
- 🔒 **Data Encryption** - Sensitive data encrypted at rest
- 📝 **Audit Logs** - Track all user activities

### Developer Features
- 🔌 **RESTful API** - Complete REST API for integrations
- 📚 **Swagger Documentation** - Interactive API documentation
- 🧪 **Unit Tests** - High test coverage
- 🐳 **Docker Support** - Run in containers

---

## 🚀 Quick Start

### Prerequisites
- .NET 6.0 or higher
- SQL Server 2019+
- Git

### Installation

```bash
# Clone the repository
git clone https://github.com/DipeshPansuriya/TimeTracker.git
cd TimeTracker

# Restore dependencies
dotnet restore

# Build the project
dotnet build --configuration Release

# Run migrations
dotnet ef database update

# Start the application
dotnet run
```

Access the application at `http://localhost:5000`

---

## 📦 Installation

### Option 1: From Source

```bash
git clone https://github.com/DipeshPansuriya/TimeTracker.git
cd TimeTracker
dotnet restore
dotnet build
dotnet run
```

### Option 2: Docker

```bash
git clone https://github.com/DipeshPansuriya/TimeTracker.git
cd TimeTracker

# Build Docker image
docker build -t timetracker:latest .

# Run container
docker run -p 5000:5000 \
  -e ConnectionStrings__DefaultConnection="Server=sqlserver;..." \
  timetracker:latest
```

### Option 3: NuGet Package

```bash
dotnet add package TimeTracker --version 1.0.0
```

---

## ⚙️ Configuration

### Environment Variables

Create `.env` file:

```env
ConnectionStrings__DefaultConnection=Server=localhost;Database=TimeTracker;User Id=sa;Password=YourPassword123;
Jwt__Key=your-secret-key-here-min-32-chars
Jwt__Issuer=TimeTracker
Jwt__Audience=TimeTrackerUsers
CORS__AllowedOrigins=http://localhost:3000,http://localhost:5000
```

### Application Settings

Edit `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=TimeTracker;..."
  },
  "Jwt": {
    "Key": "your-secret-key",
    "Issuer": "TimeTracker",
    "Audience": "TimeTrackerUsers",
    "ExpirationMinutes": 60
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  }
}
```

---

## 📖 Usage

### CLI Examples

```bash
# Create a new project
curl -X POST http://localhost:5000/api/projects \
  -H "Content-Type: application/json" \
  -d '{"name":"My Project"}'

# Start tracking time
curl -X POST http://localhost:5000/api/time-entries/start \
  -H "Content-Type: application/json" \
  -d '{"projectId":1,"description":"Working on feature"}'

# Stop tracking
curl -X POST http://localhost:5000/api/time-entries/stop \
  -H "Content-Type: application/json" \
  -d '{"id":1}'

# Get time report
curl http://localhost:5000/api/reports/monthly?month=09&year=2026
```

### Web Interface

1. Navigate to `http://localhost:5000`
2. Login with your credentials
3. Create a project
4. Start tracking time
5. View analytics and reports

---

## 📚 API Documentation

### Interactive Swagger UI

Access Swagger documentation at:
```
http://localhost:5000/swagger/index.html
```

### API Endpoints

#### Projects
```
GET    /api/projects              - List all projects
GET    /api/projects/{id}         - Get project details
POST   /api/projects              - Create new project
PUT    /api/projects/{id}         - Update project
DELETE /api/projects/{id}         - Delete project
```

#### Time Entries
```
GET    /api/time-entries          - List entries
GET    /api/time-entries/{id}     - Get entry details
POST   /api/time-entries/start    - Start tracking
POST   /api/time-entries/stop     - Stop tracking
PUT    /api/time-entries/{id}     - Update entry
DELETE /api/time-entries/{id}     - Delete entry
```

#### Reports
```
GET    /api/reports/daily         - Daily report
GET    /api/reports/weekly        - Weekly report
GET    /api/reports/monthly       - Monthly report
GET    /api/reports/custom        - Custom date range
GET    /api/reports/export        - Export reports
```

---

## 🏗️ Architecture

### Technology Stack

| Layer | Technology |
|-------|------------|
| **Frontend** | ASP.NET Core Razor, Blazor |
| **Backend** | ASP.NET Core 6.0 |
| **Database** | SQL Server / PostgreSQL |
| **ORM** | Entity Framework Core |
| **API** | REST with Swagger/OpenAPI |
| **Testing** | xUnit, Moq, Fluent Assertions |
| **CI/CD** | GitHub Actions |

### Project Structure

```
TimeTracker/
├── src/
│   ├── TimeTracker.Api/          # API layer
│   ├── TimeTracker.Core/         # Business logic
│   ├── TimeTracker.Data/         # Data access
│   ├── TimeTracker.Models/       # Data models
│   └── TimeTracker.Shared/       # Shared utilities
├── tests/
│   ├── TimeTracker.Tests/        # Unit tests
│   └── TimeTracker.IntegrationTests/
├── docs/                         # Documentation
└── .github/
    └── workflows/                # CI/CD workflows
```

### Database Schema

```
Users
├── Id (PK)
├── Email (Unique)
├── PasswordHash
└── CreatedAt

Projects
├── Id (PK)
├── UserId (FK)
├── Name
└── CreatedAt

TimeEntries
├── Id (PK)
├── ProjectId (FK)
├── UserId (FK)
├── StartTime
├── EndTime
└── Duration
```

---

## 🤝 Contributing

We welcome contributions! Please see [CONTRIBUTING.md](/CONTRIBUTING.md) for details.

### Development Workflow

1. **Fork** the repository
2. **Create** a feature branch: `git checkout -b feature/amazing-feature`
3. **Commit** changes: `git commit -m 'Add amazing feature'`
4. **Push** branch: `git push origin feature/amazing-feature`
5. **Open** a Pull Request

### Code Standards

- Follow [C# Coding Conventions](https://docs.microsoft.com/en-us/dotnet/csharp/fundamentals/coding-style/coding-conventions)
- Write unit tests for new features
- Maintain > 80% code coverage
- Add XML documentation comments

### Commit Message Format

```
<type>(<scope>): <subject>

<body>

<footer>
```

Types: `feat`, `fix`, `docs`, `style`, `refactor`, `test`, `chore`

---

## 🔒 Security

### Reporting Security Issues

Do NOT open GitHub issues for security vulnerabilities. Instead, email:

📧 **[dipeshpansuriya@ymail.com](mailto:dipeshpansuriya@ymail.com)**

See [SECURITY.md](/SECURITY.md) for detailed security guidelines.

### Security Features

- ✅ HTTPS/TLS encryption
- ✅ Password hashing (bcrypt)
- ✅ JWT authentication
- ✅ Role-based access control
- ✅ Input validation
- ✅ SQL injection prevention
- ✅ CSRF protection

---

## 📄 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) file for details.

---

## 📞 Support

### Get Help

- 📖 **Documentation**: [CONTRIBUTING.md](/CONTRIBUTING.md), [SECURITY.md](/SECURITY.md)
- 💬 **Discussions**: [GitHub Discussions](/discussions)
- 🐛 **Issues**: [GitHub Issues](/issues)
- 📧 **Email**: dipeshpansuriya@ymail.com

### Community

- ⭐ Star us on GitHub
- 🍴 Fork and contribute
- 📢 Share with others
- 💭 Provide feedback

---

## 🎯 Roadmap

### v1.1 (Q4 2026)
- [ ] Mobile app support
- [ ] Advanced analytics
- [ ] Team features
- [ ] Integration with calendar apps

### v2.0 (Q1 2027)
- [ ] AI-powered insights
- [ ] Billing module
- [ ] Cloud deployment
- [ ] Offline support

---

## 👥 Authors

- **Dipesh Pansuriya** - [@DipeshPansuriya](https://github.com/DipeshPansuriya)

---

## 🙏 Acknowledgments

- Community contributors
- Security researchers
- All open-source projects we depend on

---

<div align="center">

**[Back to Top](#-timetracker)**

Made with ❤️ by the TimeTracker Community

</div>
