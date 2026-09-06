# 🔄 MsSqlToPg

> An intelligent, open-source migration tool for seamlessly converting SQL Server databases to PostgreSQL with data integrity verification.

[![Build Status](https://img.shields.io/badge/build-passing-brightgreen)]() 
[![License](https://img.shields.io/badge/license-MIT-blue)]() 
[![Python](https://img.shields.io/badge/python-3.8+-3776ab?logo=python)]() 
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-13+-336791?logo=postgresql)]() 
[![Open Source](https://img.shields.io/badge/Open%20Source-%E2%9C%93-brightgreen)]() 
[![Community](https://img.shields.io/badge/Community-Driven-ff69b4)](/CONTRIBUTING.md)

## 📋 Table of Contents

- [Features](#features)
- [Quick Start](#quick-start)
- [Installation](#installation)
- [Configuration](#configuration)
- [Usage](#usage)
- [Architecture](#architecture)
- [Contributing](#contributing)
- [Security](#security)
- [License](#license)
- [Support](#support)

---

## ✨ Features

### Core Functionality
- 🔄 **Schema Migration** - Automatic SQL Server to PostgreSQL schema conversion
- 📊 **Data Migration** - Intelligent data type mapping and transformation
- ✅ **Data Validation** - Verify data integrity post-migration
- 🔍 **Mapping Preview** - Review and customize schema mapping before migration
- 🏃 **Incremental Migration** - Support for partial and incremental migrations
- 📈 **Progress Tracking** - Real-time migration progress monitoring

### Advanced Features
- 🔐 **Secure Connections** - SSL/TLS for both databases
- 🛡️ **Rollback Support** - Reverse migration capability
- 📝 **Migration Logging** - Detailed logs for every operation
- 🧪 **Validation Reports** - Comprehensive validation and mismatch reports
- ⚙️ **Customizable Mapping** - Override automatic type mappings
- 🔌 **Parallel Processing** - Multi-threaded migration for performance

### Developer Features
- 🐍 **Python-based** - Easy to extend and customize
- 📚 **CLI Interface** - Command-line tools for automation
- 🔌 **API Mode** - Programmatic access to migration engine
- 📦 **Modular Design** - Reusable components
- 🧪 **Comprehensive Tests** - Unit and integration tests included

---

## 🚀 Quick Start

### Prerequisites
- Python 3.8 or higher
- SQL Server 2019+ (source)
- PostgreSQL 13+ (target)
- Access to both databases

### Installation

```bash
# Clone the repository
git clone https://github.com/DipeshPansuriya/MsSqlToPg.git
cd MsSqlToPg

# Install dependencies
pip install -r requirements.txt

# Verify installation
python -m mssqltopg --version
```

### Basic Usage

```bash
# Migrate a database
python -m mssqltopg migrate \
  --source "Server=mssql.example.com;Database=MyDb;User Id=sa;Password=pass" \
  --target "postgresql://user:password@pg.example.com:5432/mydb"

# Validate migration
python -m mssqltopg validate \
  --source "Server=mssql.example.com;Database=MyDb;User Id=sa;Password=pass" \
  --target "postgresql://user:password@pg.example.com:5432/mydb"
```

---

## 📦 Installation

### Option 1: From Source

```bash
git clone https://github.com/DipeshPansuriya/MsSqlToPg.git
cd MsSqlToPg
pip install -e .
```

### Option 2: From PyPI

```bash
pip install mssqltopg
```

### Option 3: Docker

```bash
git clone https://github.com/DipeshPansuriya/MsSqlToPg.git
cd MsSqlToPg

# Build image
docker build -t mssqltopg:latest .

# Run migration
docker run -it mssqltopg:latest migrate \
  --source "Server=mssql;Database=MyDb;..." \
  --target "postgresql://user:password@postgres:5432/mydb"
```

---

## ⚙️ Configuration

### Create Configuration File

**config.yaml**

```yaml
source:
  type: mssql
  server: mssql.example.com
  database: MyDatabase
  user: sa
  password: ${MSSQL_PASSWORD}  # Use environment variables
  port: 1433
  encrypt: true
  trust_certificate: false

target:
  type: postgresql
  host: pg.example.com
  database: mydb
  user: postgres
  password: ${PG_PASSWORD}
  port: 5432
  ssl_mode: require

migration:
  # Schema options
  include_schemas:
    - dbo
    - public
  exclude_tables:
    - aspnet_*
    - temp_*
  
  # Data options
  skip_data: false
  batch_size: 1000
  parallel_threads: 4
  
  # Validation
  validate_after_migration: true
  row_count_check: true
  data_integrity_check: true
  
  # Logging
  log_level: INFO
  log_file: migration.log
```

### Environment Variables

```bash
# Source Database
export MSSQL_SERVER=mssql.example.com
export MSSQL_DATABASE=MyDb
export MSSQL_USER=sa
export MSSQL_PASSWORD=password

# Target Database
export PG_HOST=pg.example.com
export PG_DATABASE=mydb
export PG_USER=postgres
export PG_PASSWORD=password
```

---

## 📖 Usage

### CLI Commands

#### 1. Analyze Schema

```bash
python -m mssqltopg analyze \
  --source "Server=localhost;Database=MyDb;User Id=sa;Password=pass" \
  --output analysis_report.json
```

#### 2. Preview Mapping

```bash
python -m mssqltopg preview-mapping \
  --source "Server=localhost;Database=MyDb;User Id=sa;Password=pass" \
  --config config.yaml
```

#### 3. Perform Migration

```bash
python -m mssqltopg migrate \
  --config config.yaml \
  --dry-run  # Optional: preview without actual migration
```

#### 4. Validate Migration

```bash
python -m mssqltopg validate \
  --source "Server=localhost;Database=MyDb;User Id=sa;Password=pass" \
  --target "postgresql://user:password@localhost:5432/mydb" \
  --report validation_report.html
```

#### 5. Generate Report

```bash
python -m mssqltopg report \
  --source "Server=localhost;Database=MyDb;User Id=sa;Password=pass" \
  --target "postgresql://user:password@localhost:5432/mydb" \
  --format html
```

### Python API

```python
from mssqltopg import MigrationEngine, Config

# Load configuration
config = Config.from_file('config.yaml')

# Create migration engine
engine = MigrationEngine(config)

# Analyze schema
analysis = engine.analyze_schema()
print(f"Tables: {len(analysis['tables'])}")
print(f"Indexes: {len(analysis['indexes'])}")

# Perform migration
result = engine.migrate(
    skip_data=False,
    validate=True,
    dry_run=False
)

if result.success:
    print(f"✅ Migration successful")
    print(f"Rows migrated: {result.rows_migrated}")
else:
    print(f"❌ Migration failed: {result.error}")
```

---

## 🏗️ Architecture

### Technology Stack

| Component | Technology |
|-----------|------------|
| **Language** | Python 3.8+ |
| **MSSQL Driver** | pyodbc, sqlalchemy-mssql |
| **PostgreSQL Driver** | psycopg2, sqlalchemy |
| **ORM** | SQLAlchemy 2.0 |
| **CLI** | Click |
| **Testing** | pytest, pytest-cov |
| **Logging** | Python logging |

### Module Structure

```
MsSqlToPg/
├── mssqltopg/
│   ├── __init__.py
│   ├── config.py              # Configuration management
│   ├── engine.py              # Migration engine
│   ├── validators.py          # Data validation
│   ├── mappers/
│   │   ├── schema_mapper.py   # Schema mapping logic
│   │   ├── type_mapper.py     # Data type mapping
│   │   └── constraint_mapper.py
│   ├── connectors/
│   │   ├── mssql_connector.py # SQL Server connection
│   │   └── pg_connector.py    # PostgreSQL connection
│   ├── migrators/
│   │   ├── schema_migrator.py # Schema migration
│   │   └── data_migrator.py   # Data migration
│   ├── cli.py                 # CLI interface
│   └── utils/
│       ├── logger.py
│       └── helpers.py
├── tests/
│   ├── test_config.py
│   ├── test_schema_mapper.py
│   ├── test_type_mapper.py
│   └── test_engine.py
├── docs/                      # Documentation
└── requirements.txt
```

### Data Type Mapping

| SQL Server | PostgreSQL |
|------------|------------|
| `BIGINT` | `BIGINT` |
| `VARCHAR(max)` | `TEXT` |
| `NVARCHAR(max)` | `TEXT` |
| `UNIQUEIDENTIFIER` | `UUID` |
| `DATETIME2` | `TIMESTAMP` |
| `BIT` | `BOOLEAN` |
| `DECIMAL(p,s)` | `NUMERIC(p,s)` |

---

## 🤝 Contributing

We welcome contributions! See [CONTRIBUTING.md](/CONTRIBUTING.md) for details.

### Development Setup

```bash
# Clone and setup
git clone https://github.com/DipeshPansuriya/MsSqlToPg.git
cd MsSqlToPg

# Create virtual environment
python -m venv venv
source venv/bin/activate  # On Windows: venv\Scripts\activate

# Install dev dependencies
pip install -r requirements-dev.txt

# Run tests
pytest tests/ -v --cov=mssqltopg
```

### Code Style

- Follow [PEP 8](https://www.python.org/dev/peps/pep-0008/)
- Use `black` for formatting: `black mssqltopg/`
- Use `flake8` for linting: `flake8 mssqltopg/`
- Use `mypy` for type checking: `mypy mssqltopg/`

---

## 🔒 Security

### Reporting Security Issues

Email: 📧 **[dipeshpansuriya@ymail.com](mailto:dipeshpansuriya@ymail.com)**

See [SECURITY.md](/SECURITY.md) for details.

### Best Practices

- ✅ Use SSL/TLS for database connections
- ✅ Never hardcode credentials in code
- ✅ Use environment variables for secrets
- ✅ Review connection strings before migration
- ✅ Keep logs separate from version control

---

## 📄 License

This project is licensed under the MIT License - see [LICENSE](LICENSE) file.

---

## 📞 Support

### Get Help

- 📖 **Documentation**: [CONTRIBUTING.md](/CONTRIBUTING.md)
- 💬 **Discussions**: [GitHub Discussions](/discussions)
- 🐛 **Issues**: [GitHub Issues](/issues)
- 📧 **Email**: dipeshpansuriya@ymail.com

### Resources

- [SQL Server to PostgreSQL Migration Guide](https://wiki.postgresql.org/wiki/Migration,_Conversion,_and_Verification)
- [PostgreSQL Documentation](https://www.postgresql.org/docs/)
- [Data Type Mapping Reference](/docs/TYPE_MAPPING.md)

---

## 🎯 Roadmap

### v1.1 (Q4 2026)
- [ ] GUI Interface
- [ ] Support for more databases (Oracle, MySQL)
- [ ] Enhanced validation reports
- [ ] Performance optimization

### v2.0 (Q1 2027)
- [ ] Cloud-based migration
- [ ] Real-time synchronization
- [ ] Advanced conflict resolution
- [ ] Rollback capabilities

---

## 👥 Authors

- **Dipesh Pansuriya** - [@DipeshPansuriya](https://github.com/DipeshPansuriya)

---

<div align="center">

**[Back to Top](#-mssqltopg)**

Made with ❤️ by the MsSqlToPg Community

</div>
