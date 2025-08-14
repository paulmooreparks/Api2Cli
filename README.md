![Api2Cli Logo](https://raw.githubusercontent.com/paulmooreparks/Api2Cli/master/logo/Api2Cli-sm.png)

# Api2Cli - API Management CLI Tool

<p>
  <a href="https://github.com/paulmooreparks/Api2Cli/releases">
    <img alt="Api2Cli CLI Version" src="https://img.shields.io/github/v/release/paulmooreparks/Api2Cli?include_prereleases">
  </a>
  <a href="https://github.com/paulmooreparks/Api2Cli">
    <img alt="GitHub last commit" src="https://img.shields.io/github/last-commit/paulmooreparks/Api2Cli">
  </a>
  <a href="https://github.com/paulmooreparks/Api2Cli/issues">
    <img alt="GitHub issues" src="https://img.shields.io/github/issues/paulmooreparks/Api2Cli">
  </a>
  <a href="https://github.com/paulmooreparks/Api2Cli/actions">
    <img alt="Build Status" src="https://img.shields.io/github/actions/workflow/status/paulmooreparks/Api2Cli/auto-build.yml?branch=main">
  </a>
  <a href="https://opensource.org/licenses/MIT">
    <img alt="License: MIT" src="https://img.shields.io/badge/License-MIT-yellow.svg">
  </a>
  <a href="https://dotnet.microsoft.com/">
    <img alt=".NET 8.0" src="https://img.shields.io/badge/.NET-8.0-purple">
  </a>
</p>

## Api2Cli Overview

**Api2Cli** is a powerful command-line interface (CLI) tool for HTTP API management, testing, and automation. It provides a workspace-based approach to organize API interactions, supports JavaScript scripting for advanced workflows, and offers an intuitive command-line experience for developers working with REST APIs. It bridges the gap between simple command-line tools like curl and complex GUI applications like Postman, providing the power and flexibility developers need for modern API workflows.

### 🚀 Key Features

#### HTTP Methods Support
- **Complete HTTP coverage**: GET, POST, PUT, PATCH, DELETE, HEAD, OPTIONS
- **Flexible request configuration**: Headers, query parameters, request bodies
- **Response handling**: Status codes, headers, content processing
- **Cookie management**: Automatic cookie handling and custom cookie support

#### Workspace Management
- **Organized collections**: Group related API requests into workspaces
- **Workspace inheritance**: Create derived workspaces from existing ones
- **Multiple workspace support**: Switch between different API environments
- **Configuration persistence**: Automatic workspace configuration storage

#### JavaScript Scripting Engine
- **Pre-request scripts**: Modify requests before execution
- **Post-response scripts**: Process responses and extract data
- **Global scripts**: Shared functionality across workspaces
- **Environment manipulation**: Dynamic configuration and data processing
- **NuGet package support**: Extend functionality with .NET packages

#### Cross-Platform Compatibility
- **Multi-OS support**: Windows, Linux, and macOS
- **Native installers**: Platform-specific packages available

#### Advanced Configuration
- **Environment variables**: Centralized configuration management
- **Parameter substitution**: Dynamic value replacement in requests
- **XferLang configuration**: Powerful configuration language
- **Template support**: Reusable request templates

#### Developer Experience
- **REPL mode**: Interactive command-line interface
- **Command-line execution**: Single-command API calls
- **Input redirection**: Pipe data from other commands
- **Scriptable workflows**: Custom scripts for automated tasks

### 📦 Installation

#### Pre-built Downloads

Download the latest release from [GitHub Releases](https://github.com/paulmooreparks/Api2Cli/releases) for your platform:

##### Installers (Automatic PATH Setup)
- **Windows**: `xk-VERSION-installer-win-x64.exe` - Windows Installer
- **Linux**: `Api2Cli-vVERSION-installer-linux-x64.deb` - Debian Package (`sudo dpkg -i`)
- **macOS**: `Api2Cli-vVERSION-installer-osx-x64.pkg` - macOS Installer Package

#### Build from Sourcegit clone https://github.com/paulmooreparks/Api2Cli.git
cd Api2Cli
dotnet build --configuration Release
dotnet publish xk/xk.csproj --configuration Release --output ./publish
### 🎯 Quick Start

#### 1. First Run

When you run `xk` for the first time, it creates a `.a2c` folder in your home directory with initial configuration files:xk --helpThis creates:
- `~/.a2c/workspaces.xfer` - Workspace definitions
- `~/.a2c/.env` - Environment variables
- `~/.a2c/packages/` - NuGet packages storage

#### 2. Basic HTTP Requests# Simple GET request
`xk get https://api.example.com/users`

# POST with JSON payload
`echo '{"name": "John"}' | xk post https://api.example.com/users`

# Add headers
`xk get https://api.example.com/users --headers "Authorization: Bearer token"`

### 📖 Configuration

#### Workspace Configuration (`~/.a2c/workspaces.xfer`)

Workspaces are defined using the XferLang configuration language. Here's a realistic example showing enterprise-grade patterns:
```xferlang
{
    // Global initialization script with .NET CLR integration
    initScript <'
        let clr = host.lib('mscorlib', 'System', 'System.Core');
        let Environment = clr.System.Environment;
        let Dns = clr.System.Net.Dns;
        let AddressFamily = clr.System.Net.Sockets.AddressFamily;

        // Global utility functions
        function formatJson(rawJson) {
            let obj = JSON.parse(rawJson);
            return JSON.stringify(obj, null, 2);
        }

        function isApiSuccess(httpStatus, apiError) {
            return httpStatus === 200 && apiError?.code === "200";
        }

        // Auto-detect local IP for development
        function getLocalIpAddress() {
            let hostEntry = Dns.GetHostEntry(Dns.GetHostName());
            for (let i = 0; i < hostEntry.AddressList.Length; i++) {
                let ip = hostEntry.AddressList.GetValue(i);
                if (ip.AddressFamily.Equals(AddressFamily.InterNetwork)) {
                    return ip.ToString();
                }
            }
            throw new Error("No IPv4 address found!");
        }

        let localIp = getLocalIpAddress();
        Environment.SetEnvironmentVariable("LOCAL_IP", localIp);
    '>

    // Global response formatting
    postResponse <'
        let formattedContent = formatJson(request.response.body);
        return formattedContent;
    '>

    properties {
        hideReplMessages ~true
        defaultTimeout 30000
    }

    workspaces {
        // Base workspace with common patterns
        apiBase {
            isHidden ~true
            description "Base workspace for API environments"

            properties {
                tokenName "API_TOKEN"
                region "us-east-1"
            }

            // Workspace initialization
            initScript <'
                if (workspace.tokenName != null) {
                    let token =\ a2c.store.get(workspace.tokenName);
                    if (token) {
                        Environment.SetEnvironmentVariable(workspace.tokenName, token);
                    }
                }
            '>

            // Common authentication flow
            scripts {
                login {
                    description "Authenticate and store token"
                    script <'
                        const response = workspace.post_Auth_Login.execute();
                        const status = workspace.post_Auth_Login.response.statusCode ?? 0;
                        const data = response ? JSON.parse(response) : null;

                        if (!isApiSuccess(status, data?.error)) {
                            console.error(`Login failed: ${status} - ${data?.error?.message ?? response}`);
                            return null;
                        }

                        const token = data.data?.access_token;
                        if (!token) {
                            console.error("No access token received");
                            return null;
                        }

                        // Store token for future requests
                        const tokenName = workspace.tokenName ?? "API_TOKEN";
                        Environment.SetEnvironmentVariable(tokenName, token);
                       \ a2c.store.set(tokenName, token);

                        console.log("✅ Authentication successful");
                        return token;
                    '>
                }

                getResourceById {
                    description "Get a resource by ID with error handling"
                    arguments {
                        id { type "number" description "Resource ID" }
                        includeDetails { type "boolean" description "Include detailed information" }
                    }
                    script <'
                        try {
                            const content = workspace.get_Resource_Details.execute(id, includeDetails);
                            const parsed = JSON.parse(content);

                            if (parsed.data) {
                                return formatJson(JSON.stringify(parsed.data));
                            } else {
                                throw new Error(`Resource ${id} not found`);
                            }
                        } catch (error) {
                            console.error(`Error fetching resource ${id}: ${error.message}`);
                            return null;
                        }
                    '>
                }

                uploadFile {
                    description "Upload file with progress tracking"
                    arguments {
                        filePath { type "string" description "Path to file to upload" }
                        resourceType { type "string" description "Type of resource" }
                    }
                    script <'
                        console.log(`📤 Uploading ${filePath}...`);

                        let fileContent;
                        if (filePath.endsWith('.json')) {
                            fileContent =\ a2c.fileSystem.readText(filePath);
                        } else {
                            fileContent =\ a2c.fileSystem.readBytes(filePath);
                        }

                        const result = workspace.post_Upload.execute(fileContent, resourceType);
                        console.log(`✅ Upload completed: ${result}`);
                        return result;
                    '>
                }
            }

            // Global pre-request authentication
            preRequest <'
                let token =\ a2c.store.get(workspace.tokenName);
                if (token) {
                    request.headers["Authorization"] = "Bearer " + token;
                }
                request.headers["Content-Type"] = "application/json";
                request.headers["Accept"] = "application/json";
                request.headers["User-Agent"] = "Api2Cli/1.0";
            '>

            requests {
                post_Auth_Login {
                    description "Authenticate with username/password"
                    endpoint "/auth/login"
                    method "POST"
                    payload <'{
                        "username": "{{[env]::API_USERNAME}}",
                        "password": "{{[env]::API_PASSWORD}}"
                    }'>
                }

                get_Resource_Details {
                    description "Get detailed resource information"
                    endpoint "/api/v2/resources/{{[arg]::id}}"
                    method "GET"

                    arguments {
                        id { type "number" description "Resource ID" }
                        includeDetails { type "boolean" description "Include details" }
                    }

                    parameters (
                        'include_details={{[arg]::includeDetails::false}}'
                        'format=json'
                    )

                    postResponse <'
                        let content = request.response.body;
                        if (request.response.statusCode === 200) {
                            let data = JSON.parse(content);
                            if (data.data && data.data.metadata) {
                                // Store metadata for later use
                               \ a2c.store.set("lastResourceMeta", data.data.metadata);
                            }
                        }
                        return nextHandler();
                    '>
                }

                post_Upload {
                    description "Upload file or data"
                    endpoint "/api/v2/upload"
                    method "POST"

                    arguments {
                        fileData { type "string" description "File content or data" }
                        resourceType { type "string" description "Type of resource" }
                    }

                    payload <'{{[arg]::fileData}}'>

                    preRequest <'
                        request.headers["Content-Type"] = "multipart/form-data";
                        request.headers["X-Resource-Type"] = "{{[arg]::resourceType}}";
                        nextHandler();
                    '>
                }
            }
        }

        // Development environment
        development {
            extend "apiBase"
            description "Development API environment"
            baseUrl "http://localhost:3000"

            properties {
                tokenName "DEV_TOKEN"
                environment "development"
            }

            scripts {
                startLocalServices {
                    description "Start local development services"
                    script <'
                        console.log("🚀 Starting local services...");

                        // Start API server
                       \ a2c.process.run("npm", ".", "start");

                        // Start database
                       \ a2c.process.runCommand(false, ".", "docker-compose", "up -d postgres");

                        console.log("✅ Services started");
                    '>
                }

                resetDatabase {
                    description "Reset development database"
                    script <'
                        console.log("🔄 Resetting database...");
                        let result = workspace.post_Admin_Database_Reset.execute();
                        console.log("✅ Database reset completed");
                        return result;
                    '>
                }
            }

            requests {
                post_Admin_Database_Reset {
                    description "Reset development database"
                    endpoint "/admin/database/reset"
                    method "POST"
                    payload <'{"confirm": true}'>
                }
            }
        }

        // Production environment
        production {
            extend "apiBase"
            description "Production API environment"
            baseUrl "https://api.company.com"

            properties {
                tokenName "PROD_TOKEN"
                environment "production"
                region "us-west-2"
            }

            scripts {
                healthCheck {
                    description "Check production system health"
                    script <'
                        console.log("🏥 Checking system health...");

                        const health = workspace.get_Health.execute();
                        const status = JSON.parse(health);

                        if (status.status === "healthy") {
                            console.log("✅ All systems operational");
                        } else {
                            console.log("⚠️ System issues detected:");
                            status.checks.forEach(check => {
                                if (check.status !== "healthy") {
                                    console.log(`  ❌ ${check.service}: ${check.message}`);
                                }
                            });
                        }

                        return health;
                    '>
                }
            }

            requests {
                get_Health {
                    description "Get system health status"
                    endpoint "/health"
                    method "GET"
                }
            }
        }
    }
}
```

#### Environment Variables (`~/.a2c/.env`)

The `.env` file contains sensitive configuration that should never be committed to version control:

```env
# Authentication credentials for different environments
API_USERNAME=your-username
API_PASSWORD=your-secure-password
```

# Environment-specific tokens (populated by login scripts)
```env
DEV_TOKEN=
STAGING_TOKEN=
PROD_TOKEN=
```

# Database connections
```env
DB_CONNECTION_STRING=Server=localhost;Database=myapp;Trusted_Connection=true;
REDIS_URL=redis://localhost:6379
```

# External service API keys
```env
GITHUB_TOKEN=ghp_your_github_personal_access_token
SLACK_WEBHOOK_URL=https://hooks.slack.com/services/YOUR/SLACK/WEBHOOK
AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
AWS_REGION=us-west-2
```


### Advanced Parameter Substitution

Api2Cli supports complex parameter replacement patterns:

```xferlang
requests {
    // Dynamic endpoint construction
    getUserByContext {
        endpoint "/api/v2/users/{{[env]::USER_ID}}/{{[arg]::context}}"
        method "GET"

        arguments {
            context { type "string" description "User context (profile, settings, activity)" }
            includeMetadata { type "boolean" description "Include metadata" }
        }

        parameters (
            'include_metadata={{[arg]::includeMetadata::false}}'
            'timestamp={{[script]::Date.now()}}'
            'environment={{[env]::ENVIRONMENT}}'
        )

        headers {
            Authorization <'Bearer {{[store]::API_TOKEN}}'>
            X-User-Context <'{{[arg]::context}}'>
            X-Request-Source <'Api2Cli-{{[env]::USERNAME}}'>
        }
    }

    // Conditional payload based on environment
    createResource {
        endpoint "/api/v2/resources"
        method "POST"

        arguments {
            resourceData { type "string" description "Resource data as JSON" }
            dryRun { type "boolean" description "Dry run mode" }
        }

        payload <'
        {
            "data": {{[arg]::resourceData}},
            "metadata": {
                "created_by": "{{[env]::USERNAME}}",
                "environment": "{{[env]::ENVIRONMENT}}",
                "dry_run": {{[arg]::dryRun::false}},
                "timestamp": "{{[script]::new Date().toISOString()}}"
            }
        }'>
    }
}
```

### Workspace Inheritance and Composition

Build complex workspace hierarchies:

```xferlang
workspaces {
    // Shared authentication workspace
    authBase {
        isHidden ~true

        scripts {
            oauth2Login {
                description "OAuth2 authentication flow"
                script <'
                    const clientId = Environment.GetEnvironmentVariable("OAUTH_CLIENT_ID");
                    const clientSecret = Environment.GetEnvironmentVariable("OAUTH_CLIENT_SECRET");

                    if (!clientId || !clientSecret) {
                        throw new Error("OAuth credentials not configured");
                    }

                    // Exchange credentials for token
                    const tokenResponse = workspace.post_OAuth_Token.execute(clientId, clientSecret);
                    const token = JSON.parse(tokenResponse).access_token;

                   \ a2c.store.set("oauth_token", token);
                    Environment.SetEnvironmentVariable("API_TOKEN", token);

                    return token;
                '>
            }
        }
    }

    // API client workspace
    apiClient {
        extend "authBase"
        description "Generic API client with authentication"

        preRequest <'
            let token =\ a2c.store.get("oauth_token");
            if (!token) {
                console.log("🔐 No token found, authenticating...");
                token = workspace.oauth2Login();
            }
            request.headers["Authorization"] = `Bearer ${token}`;
        '>
    }

    // Specific service workspace
    userService {
        extend "apiClient"
        description "User management service"
        baseUrl "https://users.api.company.com"

        // Service-specific scripts and requests
        requests {
            getAllUsers {
                endpoint "/users"
                method "GET"

                postResponse <'
                    const users = JSON.parse(request.response.body);
                    console.log(`📊 Found ${users.length} users`);

                    // Store user count for reporting
                   \ a2c.store.set("last_user_count", users.length);

                    return nextHandler();
                '>
            }
        }
    }
}
## 🌟 Real-World Examples

### CI/CD Pipeline Integration

Integrate Api2Cli into your deployment pipelines:
#!/bin/bash
# deploy.sh - Deployment script using Api2Cli

echo "🚀 Starting deployment pipeline..."

# Login to API
xk prod login

# Run health check
if ! xk prod healthCheck; then
    echo "❌ Environment unhealthy, aborting deployment"
    exit 1
fi

# Deploy with version from CI
VERSION=${CI_COMMIT_TAG:-"latest"}
APPROVAL_TICKET=${JIRA_TICKET:-""}

xk prod deployService --version "$VERSION" --approvalTicket "$APPROVAL_TICKET"

# Verify deployment
sleep 30
if xk prod verifyDeployment --version "$VERSION"; then
    echo "✅ Deployment successful"
    # Notify team
    xk prod notifyTeam --message "Deployment of $VERSION completed successfully"
else
    echo "❌ Deployment verification failed, rolling back..."
    xk prod rollback
    exit 1
fi
### Microservices Testing Automation

Automate complex microservice testing scenarios:
// Global test setup script
initScript <'
    let testResults = [];
    let testStartTime = Date.now();

    function recordTest(testName, success, duration, details) {
        testResults.push({
            name: testName,
            success: success,
            duration: duration,
            details: details,
            timestamp: new Date().toISOString()
        });
    }

    function generateTestReport() {
        let totalTests = testResults.length;
        let passedTests = testResults.filter(t => t.success).length;
        let failedTests = totalTests - passedTests;
        let totalDuration = Date.now() - testStartTime;

        console.log(`\n📊 Test Report:`);
        console.log(`   Total: ${totalTests} | Passed: ${passedTests} | Failed: ${failedTests}`);
        console.log(`   Duration: ${totalDuration}ms`);

        if (failedTests > 0) {
            console.log(`\n❌ Failed Tests:`);
            testResults.filter(t => !t.success).forEach(test => {
                console.log(`   - ${test.name}: ${test.details}`);
            });
        }

        return {
            total: totalTests,
            passed: passedTests,
            failed: failedTests,
            duration: totalDuration,
            results: testResults
        };
    }
'>

// Complex integration test
scripts {
    runIntegrationTests {
        description "Run full integration test suite"
        script <'
            console.log("🧪 Starting integration test suite...");

            try {
                // Test 1: User service
                let startTime = Date.now();
                let users = workspace.testUserService();
                recordTest("User Service", true, Date.now() - startTime, `Retrieved ${users.length} users`);

                // Test 2: Authentication flow
                startTime = Date.now();
                let authResult = workspace.testAuthFlow();
                recordTest("Auth Flow", authResult.success, Date.now() - startTime, authResult.message);

                // Test 3: Data consistency
                startTime = Date.now();
                let dataCheck = workspace.testDataConsistency();
                recordTest("Data Consistency", dataCheck.passed, Date.now() - startTime,
                    `${dataCheck.checks} checks, ${dataCheck.issues} issues`);

                // Test 4: Performance benchmarks
                startTime = Date.now();
                let perfTest = workspace.runPerformanceTests();
                recordTest("Performance", perfTest.acceptable, Date.now() - startTime,
                    `Avg response: ${perfTest.avgResponseTime}ms`);

            } catch (error) {
                recordTest("Test Suite", false, Date.now() - testStartTime, error.message);
            }

            return generateTestReport();
        '>
    }

    testUserService {
        description "Test user service endpoints"
        script <'
            let errors = [];

            // Test user creation
            try {
                let newUser = workspace.post_CreateUser.execute(JSON.stringify({
                    name: "Test User",
                    email: "test@example.com",
                    role: "user"
                }));

                let userData = JSON.parse(newUser);
                if (!userData.id) {
                    errors.push("User creation failed - no ID returned");
                }

                // Store for cleanup
               \ a2c.store.set("testUserId", userData.id);

            } catch (e) {
                errors.push(`User creation error: ${e.message}`);
            }

            // Test user retrieval
            try {
                let userId =\ a2c.store.get("testUserId");
                if (userId) {
                    let user = workspace.get_UserById.execute(userId);
                    let userData = JSON.parse(user);

                    if (userData.email !== "test@example.com") {
                        errors.push("User data mismatch");
                    }
                }
            } catch (e) {
                errors.push(`User retrieval error: ${e.message}`);
            }

            // Cleanup test user
            try {
                let userId =\ a2c.store.get("testUserId");
                if (userId) {
                    workspace.delete_User.execute(userId);
                }
            } catch (e) {
                console.warn(`Cleanup warning: ${e.message}`);
            }

            if (errors.length > 0) {
                throw new Error(errors.join("; "));
            }

            return { success: true, message: "All user service tests passed" };
        '>
    }
}
```

### Development Workflow Automation

Streamline your development workflow:

```xferlang
workspaces {
    devWorkflow {
        description "Complete development workflow automation"
        baseUrl "http://localhost:3000"

        properties {
            gitBranch "feature/new-api"
            dockerComposePath "./docker"
        }

        scripts {
            startDay {
                description "Start development session"
                script <'
                    console.log("🌅 Starting development session...");

                    // 1. Check git status
                    let gitStatus =\ a2c.process.runCommand(true, ".", "git", "status --porcelain");
                    if (gitStatus.trim()) {
                        console.log("⚠️ Uncommitted changes detected");
                    }

                    // 2. Pull latest changes
                    console.log("📥 Pulling latest changes...");
                   \ a2c.process.runCommand(false, ".", "git", "pull origin main");

                    // 3. Start services
                    console.log("🐳 Starting Docker services...");
                   \ a2c.process.runCommand(false, workspace.dockerComposePath, "docker-compose", "up -d");

                    // 4. Run database migrations
                    console.log("🗄️ Running migrations...");
                    setTimeout(() => workspace.runMigrations(), 10000);

                    // 5. Start API in watch mode
                    console.log("👀 Starting API in watch mode...");
                   \ a2c.process.run("npm", ".", "run", "dev");

                    console.log("✅ Development environment ready!");
                '>
            }

            endDay {
                description "Clean up development session"
                script <'
                    console.log("🌙 Ending development session...");

                    // 1. Stop services
                   \ a2c.process.runCommand(false, workspace.dockerComposePath, "docker-compose", "down");

                    // 2. Show git status
                    let status =\ a2c.process.runCommand(true, ".", "git", "status");
                    console.log("📊 Git Status:\n" + status);

                    // 3. Generate daily report
                    workspace.generateDailyReport();

                    console.log("✅ Session ended cleanly");
                '>
            }

            runTests {
                description "Run comprehensive test suite"
                arguments {
                    coverage { type "boolean" description "Generate coverage report" }
                    integration { type "boolean" description "Include integration tests" }
                }
                script <'
                    console.log("🧪 Running test suite...");

                    let testCommands = ["npm test"];

                    if (coverage) {
                        testCommands.push("npm run test:coverage");
                    }

                    if (integration) {
                        testCommands.push("npm run test:integration");
                    }

                    let allPassed = true;
                    for (let cmd of testCommands) {
                        try {
                            let result =\ a2c.process.runCommand(true, ".", "pwsh", `-Command ${cmd}`);
                            console.log(`✅ ${cmd} completed`);
                        } catch (e) {
                            console.log(`❌ ${cmd} failed: ${e.message}`);
                            allPassed = false;
                        }
                    }

                    if (allPassed) {
                        console.log("🎉 All tests passed!");
                        // Could trigger deployment pipeline here
                        // workspace.triggerDeployment();
                    } else {
                        console.log("❌ Some tests failed");
                    }

                    return allPassed;
                '>
            }
        }
    }
}
```

### API Documentation Generation

Auto-generate API documentation from your Api2Cli workspace:

```xferlang
scripts {
    generateApiDocs {
        description "Generate API documentation from workspace"
        script <'
            console.log("📚 Generating API documentation...");

            let docs = {
                title: "API Documentation",
                version: "1.0.0",
                baseUrl: workspace.baseUrl,
                endpoints: []
            };

            // Iterate through all requests in workspace
            for (let requestName in workspace.requests) {
                let request = workspace.requests[requestName];

                let endpoint = {
                    name: requestName,
                    method: request.method,
                    path: request.endpoint,
                    description: request.description || "",
                    parameters: request.arguments || {},
                    exampleResponse: null
                };

                // Try to get example response
                try {
                    if (request.method === "GET") {
                        let response = request.execute();
                        endpoint.exampleResponse = JSON.parse(response);
                    }
                } catch (e) {
                    console.log(`⚠️ Could not get example for ${requestName}: ${e.message}`);
                }

                docs.endpoints.push(endpoint);
            }

            // Save documentation
            let docsJson = JSON.stringify(docs, null, 2);
           \ a2c.fileSystem.writeText("./docs/api-docs.json", docsJson);

            console.log(`✅ Documentation generated for ${docs.endpoints.length} endpoints`);
            return docs;
        '>
    }
}
```

## 🛠️ Command Reference

### Global Commands
# Get help
`xk --help`
`xk <command> --help`

# Set base URL globally
`xk --baseurl https://api.example.com <command>`

# Use specific workspace file
`xk --workspace /path/to/workspace.xfer <command>`

### HTTP Commands
# GET request
`xk get <url> [--headers <headers>] [--parameters <params>]`

# POST request
`xk post <url> [--payload <data>] [--headers <headers>]`

# PUT request
`xk put <url> [--payload <data>] [--headers <headers>]`

# PATCH request
`xk patch <url> [--payload <data>] [--headers <headers>]`

# DELETE request
`xk delete <url> [--headers <headers>]`

# HEAD request
`xk head <url> [--headers <headers>]`

### Workspace Commands
# List workspaces
`xk workspace list`

# Execute workspace request
`xk <workspace> <request> [options]`    

### Scripting Commands
# Execute JavaScript
`xk script <code>`

# Run workspace script
`xk <workspace> <script> [arguments]`

## 🏗️ Architecture

Api2Cli is built on .NET 8.0 and consists of several modular components:

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

  Api2Cli/
  ├── xk/                                    # Main CLI executable
  ├── ParksComputing.Api2Cli.Api/           # Core API interfaces
  ├── ParksComputing.Api2Cli.Http/          # HTTP services
  ├── ParksComputing.Api2Cli.Workspace/     # Workspace management
  ├── ParksComputing.Api2Cli.Scripting/     # JavaScript engine
  ├── ParksComputing.Api2Cli.DataStore/     # Data persistence
  ├── ParksComputing.Api2Cli.Diagnostics/   # Logging and diagnostics
  └── .github/workflows/                    # CI/CD pipelines

## 📄 License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.

## 🔗 Related Projects

- [Cliffer](https://github.com/paulmooreparks/Cliffer) - CLI framework used by Api2Cli
- [XferLang](https://github.com/paulmooreparks/Xfer) - Configuration language specification

## 📞 Support

- 🐛 [Report Issues](https://github.com/paulmooreparks/Api2Cli/issues)
- 💡 [Feature Requests](https://github.com/paulmooreparks/Api2Cli/issues)
- 📖 [Documentation](https://github.com/paulmooreparks/Api2Cli/wiki) (Coming Soon)

