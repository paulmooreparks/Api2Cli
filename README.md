![XferKit Logo](logo/XferKit-sm.png)

# XferKit - API Management CLI Tool

<p align="center">
  <a href="https://github.com/paulmooreparks/XferKit/releases">
    <img alt="XferKit CLI Version" src="https://img.shields.io/badge/XferKit_CLI-0.3.0--prerelease-blue">
  </a>
  <a href="https://github.com/paulmooreparks/XferKit">
    <img alt="GitHub last commit" src="https://img.shields.io/github/last-commit/paulmooreparks/XferKit">
  </a>
  <a href="https://github.com/paulmooreparks/XferKit/issues">
    <img alt="GitHub issues" src="https://img.shields.io/github/issues/paulmooreparks/XferKit">
  </a>
  <a href="https://github.com/paulmooreparks/XferKit/actions">
    <img alt="Build Status" src="https://img.shields.io/github/actions/workflow/status/paulmooreparks/XferKit/ci.yml?branch=main">
  </a>
  <a href="https://opensource.org/licenses/MIT">
    <img alt="License: MIT" src="https://img.shields.io/badge/License-MIT-yellow.svg">
  </a>
  <a href="https://dotnet.microsoft.com/">
    <img alt=".NET 8.0" src="https://img.shields.io/badge/.NET-8.0-purple">
  </a>
</p>

**XferKit** is a powerful command-line interface (CLI) tool for HTTP API management, testing, and automation. It provides a workspace-based approach to organize API interactions, supports JavaScript scripting for advanced workflows, and offers an intuitive command-line experience for developers working with REST APIs.

## 🚀 Key Features

### HTTP Methods Support
- **Complete HTTP coverage**: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- **Flexible request configuration**: Headers, query parameters, request bodies
- **Response handling**: Status codes, headers, content processing
- **Cookie management**: Automatic cookie handling and custom cookie support

### Workspace Management
- **Organized collections**: Group related API requests into workspaces
- **Workspace inheritance**: Create derived workspaces from existing ones
- **Multiple workspace support**: Switch between different API environments
- **Configuration persistence**: Automatic workspace configuration storage

### JavaScript Scripting Engine
- **Pre-request scripts**: Modify requests before execution
- **Post-response scripts**: Process responses and extract data
- **Global scripts**: Shared functionality across workspaces
- **Environment manipulation**: Dynamic configuration and data processing
- **NuGet package support**: Extend functionality with .NET packages

### Advanced Configuration
- **Environment variables**: Centralized configuration management
- **Parameter substitution**: Dynamic value replacement in requests
- **XferLang configuration**: Powerful configuration language
- **Template support**: Reusable request templates

### Developer Experience
- **REPL mode**: Interactive command-line interface
- **Command-line execution**: Single-command API calls
- **Input redirection**: Pipe data from other commands
- **Cross-platform**: Windows, Linux, and macOS support

## 📦 Installation

### Download Pre-built Binaries

Download the latest release for your platform:

- **Windows**: `xk-windows-x64.zip`
- **Linux**: `xk-linux-x64.tar.gz`
- **macOS**: `xk-macos-x64.tar.gz`

Extract the archive and add the executable to your PATH.

### Build from Source

```bash
git clone https://github.com/paulmooreparks/XferKit.git
cd XferKit
dotnet build --configuration Release
dotnet publish xk/xk.csproj --configuration Release --output ./publish
```

## 🎯 Quick Start

### 1. First Run

When you run `xk` for the first time, it creates a `.xk` folder in your home directory with initial configuration files:

```bash
xk --help
```

This creates:
- `~/.xk/workspaces.xfer` - Workspace definitions
- `~/.xk/.env` - Environment variables
- `~/.xk/packages/` - NuGet packages storage

### 2. Basic HTTP Requests

```bash
# Simple GET request
xk get https://api.example.com/users

# POST with JSON payload
echo '{"name": "John"}' | xk post https://api.example.com/users

# Add headers
xk get https://api.example.com/users --headers "Authorization: Bearer token"
```

### 3. Using Workspaces

```bash
# List available workspaces
xk workspace list

# Switch to a workspace
xk workspace use myapi

# Execute a request from the workspace
xk myapi getUser --baseurl https://api.example.com
```

## 📖 Configuration

### Workspace Configuration (`~/.xk/workspaces.xfer`)

Workspaces are defined using the XferLang configuration language:

```xfer
{
    properties {
        apiKey "your-api-key"
        baseUrl "https://api.example.com"
    }

    workspaces {
        myapi {
            description "Example API workspace"
            baseUrl <'$baseUrl'>

            requests {
                getUsers {
                    method "GET"
                    endpoint "/users"
                    headers {
                        Authorization <'Bearer $apiKey'>
                        Content-Type "application/json"
                    }
                    description "Retrieve all users"
                }

                createUser {
                    method "POST"
                    endpoint "/users"
                    headers {
                        Authorization <'Bearer $apiKey'>
                        Content-Type "application/json"
                    }
                    arguments {
                        userData {
                            type "string"
                            description "User data as JSON"
                        }
                    }
                    description "Create a new user"
                }
            }

            scripts {
                processResponse {
                    description "Process API responses"
                    script <'
                        function processResponse(response) {
                            if (response.statusCode === 200) {
                                console.log("Success!");
                                return JSON.stringify(JSON.parse(response.content), null, 2);
                            }
                            return "Request failed: " + response.statusCode;
                        }
                    '>
                }
            }
        }
    }
}
```

### Environment Variables (`~/.xk/.env`)

```env
API_KEY=your-secret-key
BASE_URL=https://api.example.com
DEBUG=true
```

## 🔧 Advanced Usage

### JavaScript Scripting

XferKit supports JavaScript for request preprocessing and response handling:

```javascript
// Pre-request script
function preRequest(headers, parameters, payload, cookies) {
    headers['X-Timestamp'] = new Date().toISOString();
    headers['X-Request-ID'] = generateUUID();
    return { headers, parameters, payload, cookies };
}

// Post-response script
function postResponse(statusCode, headers, content) {
    if (statusCode === 200) {
        const data = JSON.parse(content);
        xk.store.set('lastUserId', data.id);
        return formatJson(content);
    }
    return `Error: ${statusCode}`;
}
```

### Parameter Substitution

Use dynamic placeholders in your configurations:

```xfer
requests {
    getUser {
        endpoint "/users/$userId"
        headers {
            Authorization <'Bearer $apiKey'>
        }
    }
}
```

### Workspace Inheritance

Create specialized workspaces based on existing ones:

```xfer
workspaces {
    production {
        extend "base"
        baseUrl "https://api.production.com"
        properties {
            environment "prod"
        }
    }

    staging {
        extend "base"
        baseUrl "https://api.staging.com"
        properties {
            environment "staging"
        }
    }
}
```

## 🛠️ Command Reference

### Global Commands

```bash
# Get help
xk --help
xk <command> --help

# Set base URL globally
xk --baseurl https://api.example.com <command>

# Use specific workspace file
xk --workspace /path/to/workspace.xfer <command>
```

### HTTP Commands

```bash
# GET request
xk get <url> [--headers <headers>] [--parameters <params>]

# POST request
xk post <url> [--payload <data>] [--headers <headers>]

# PUT request
xk put <url> [--payload <data>] [--headers <headers>]

# PATCH request
xk patch <url> [--payload <data>] [--headers <headers>]

# DELETE request
xk delete <url> [--headers <headers>]

# HEAD request
xk head <url> [--headers <headers>]
```

### Workspace Commands

```bash
# List workspaces
xk workspace list

# Show workspace details
xk workspace show <name>

# Use workspace
xk workspace use <name>

# Execute workspace request
xk <workspace> <request> [options]
```

### Scripting Commands

```bash
# Execute JavaScript
xk script <code>

# Run workspace script
xk <workspace> <script> [arguments]
```

## 🏗️ Architecture

XferKit is built on .NET 8.0 and consists of several modular components:

- **Core CLI** (`xk`): Main executable and command processing
- **HTTP Service**: HTTP client functionality and request handling
- **Workspace Service**: Configuration management and workspace operations
- **Scripting Engine**: JavaScript execution and API integration
- **Data Store**: Configuration persistence and state management
- **Diagnostics**: Logging and error reporting

### Dependencies

- [Cliffer](https://github.com/paulmooreparks/Cliffer): CLI framework
- [XferLang](https://github.com/paulmooreparks/Xfer): Configuration language parser
- .NET 8.0 Runtime

## 🤝 Contributing

Contributions are welcome! Please feel free to submit issues, feature requests, or pull requests.

### Development Setup

1. Clone the repository
2. Ensure .NET 8.0 SDK is installed
3. Run `dotnet restore`
4. Build with `dotnet build`
5. Run tests with `dotnet test`

### Project Structure

```
XferKit/
├── xk/                                    # Main CLI executable
├── ParksComputing.XferKit.Api/           # Core API interfaces
├── ParksComputing.XferKit.Http/          # HTTP services
├── ParksComputing.XferKit.Workspace/     # Workspace management
├── ParksComputing.XferKit.Scripting/     # JavaScript engine
├── ParksComputing.XferKit.DataStore/     # Data persistence
├── ParksComputing.XferKit.Diagnostics/   # Logging and diagnostics
└── .github/workflows/                    # CI/CD pipelines
```

## 📄 License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.

## 🔗 Related Projects

- [Cliffer](https://github.com/paulmooreparks/Cliffer) - CLI framework used by XferKit
- [XferLang](https://github.com/paulmooreparks/Xfer) - Configuration language specification

## 📞 Support

- 🐛 [Report Issues](https://github.com/paulmooreparks/XferKit/issues)
- 💡 [Feature Requests](https://github.com/paulmooreparks/XferKit/issues)
- 📖 [Documentation](https://github.com/paulmooreparks/XferKit/wiki) (Coming Soon)

---

**XferKit** bridges the gap between simple command-line tools like curl and complex GUI applications like Postman, providing the power and flexibility developers need for modern API workflows.
