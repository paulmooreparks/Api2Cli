![XferKit Logo](logo/XferKit-sm.png)

# XferKit - API Management CLI Tool

<p>
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
    <img alt="Build Status" src="https://img.shields.io/github/actions/workflow/status/paulmooreparks/XferKit/auto-version.yml?branch=main">
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

### Cross-Platform Compatibility
- **Multi-OS support**: Windows, Linux, and macOS
- **Native installers**: Platform-specific packages available

### Advanced Configuration
- **Environment variables**: Centralized configuration management
- **Parameter substitution**: Dynamic value replacement in requests
- **XferLang configuration**: Powerful configuration language
- **Template support**: Reusable request templates

### Developer Experience
- **REPL mode**: Interactive command-line interface
- **Command-line execution**: Single-command API calls
- **Input redirection**: Pipe data from other commands
- **Scriptable workflows**: Custom scripts for automated tasks

## 📦 Installation

### Pre-built Downloads

Download the latest release from [GitHub Releases](https://github.com/paulmooreparks/XferKit/releases) for your platform:

#### Installers (Automatic PATH Setup)
- **Windows**: `xk-VERSION-installer-win-x64.exe` - Windows Installer
- **Linux**: `XferKit-vVERSION-installer-linux-x64.deb` - Debian Package (`sudo dpkg -i`)
- **macOS**: `XferKit-vVERSION-installer-osx-x64.pkg` - macOS Installer Package

### Build from Source
git clone https://github.com/paulmooreparks/XferKit.git
cd XferKit
dotnet build --configuration Release
dotnet publish xk/xk.csproj --configuration Release --output ./publish
## 🎯 Quick Start

### 1. First Run

When you run `xk` for the first time, it creates a `.xk` folder in your home directory with initial configuration files:
xk --help
This creates:
- `~/.xk/workspaces.xfer` - Workspace definitions
- `~/.xk/.env` - Environment variables
- `~/.xk/packages/` - NuGet packages storage

### 2. Basic HTTP Requests
# Simple GET request
xk get https://api.example.com/users

# POST with JSON payload
echo '{"name": "John"}' | xk post https://api.example.com/users

# Add headers
xk get https://api.example.com/users --headers "Authorization: Bearer token"

## 📖 Configuration

### Workspace Configuration (`~/.xk/workspaces.xfer`)

Workspaces are defined using the XferLang configuration language. Here's a realistic example showing enterprise-grade patterns:
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
                    let token = xk.store.get(workspace.tokenName);
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
                        xk.store.set(tokenName, token);

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
                            fileContent = xk.fileSystem.readText(filePath);
                        } else {
                            fileContent = xk.fileSystem.readBytes(filePath);
                        }

                        const result = workspace.post_Upload.execute(fileContent, resourceType);
                        console.log(`✅ Upload completed: ${result}`);
                        return result;
                    '>
                }
            }

            // Global pre-request authentication
            preRequest <'
                let token = xk.store.get(workspace.tokenName);
                if (token) {
                    request.headers["Authorization"] = "Bearer " + token;
                }
                request.headers["Content-Type"] = "application/json";
                request.headers["Accept"] = "application/json";
                request.headers["User-Agent"] = "XferKit/1.0";
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
                                xk.store.set("lastResourceMeta", data.data.metadata);
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
                        xk.process.run("npm", ".", "start");

                        // Start database
                        xk.process.runCommand(false, ".", "docker-compose", "up -d postgres");

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
### Environment Variables (`~/.xk/.env`)

The `.env` file contains sensitive configuration that should never be committed to version control:
# Authentication credentials for different environments
API_USERNAME=your-username
API_PASSWORD=your-secure-password

# Environment-specific tokens (populated by login scripts)
DEV_TOKEN=
STAGING_TOKEN=
PROD_TOKEN=

# Database connections
DB_CONNECTION_STRING=Server=localhost;Database=myapp;Trusted_Connection=true;
REDIS_URL=redis://localhost:6379

# External service API keys
GITHUB_TOKEN=ghp_your_github_personal_access_token
SLACK_WEBHOOK_URL=https://hooks.slack.com/services/YOUR/SLACK/WEBHOOK
AWS_ACCESS_KEY_ID=AKIAIOSFODNN7EXAMPLE
AWS_SECRET_ACCESS_KEY=wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY
AWS_REGION=us-west-2

# Service endpoints
PAYMENT_SERVICE_URL=https://payments.internal.company.com
NOTIFICATION_SERVICE_URL=https://notifications.internal.company.com

# Feature flags
ENABLE_DEBUG_LOGGING=true
ENABLE_METRICS=false
MAX_RETRY_ATTEMPTS=3

# Local development settings
LOCAL_IP=192.168.1.100
DEV_PORT=3000
DEV_SSL_CERT_PATH=/path/to/cert.pem
DEV_SSL_KEY_PATH=/path/to/key.pem
## 🔧 Advanced Usage

### Enterprise JavaScript Scripting

XferKit's JavaScript engine provides full .NET CLR integration for sophisticated automation:
// Advanced pre-request script with .NET integration
function preRequest(headers, parameters, payload, cookies) {
    let clr = host.lib('mscorlib', 'System', 'System.Core');
    let Environment = clr.System.Environment;
    let DateTime = clr.System.DateTime;

    // Add dynamic timestamps and request IDs
    headers['X-Timestamp'] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
    headers['X-Request-ID'] = generateUUID();
    headers['X-Environment'] = Environment.GetEnvironmentVariable("ENVIRONMENT") || "development";

    // Conditional authentication based on environment
    const env = Environment.GetEnvironmentVariable("ENVIRONMENT");
    if (env === "production") {
        headers['X-API-Version'] = "2.0";
        headers['X-Client-Version'] = "xferkit-1.0";
    }

    return { headers, parameters, payload, cookies };
}

// Complex response processing with error handling
function postResponse(statusCode, headers, content) {
    try {
        if (statusCode >= 200 && statusCode < 300) {
            const data = JSON.parse(content);

            // Extract and store important data for later use
            if (data.pagination) {
                xk.store.set('lastPagination', data.pagination);
            }

            if (data.data && Array.isArray(data.data)) {
                console.log(`✅ Retrieved ${data.data.length} items`);
            }

            // Auto-retry logic for rate limiting
            if (statusCode === 429) {
                const retryAfter = headers['Retry-After'] || 60;
                console.log(`⏳ Rate limited, retrying in ${retryAfter} seconds...`);
                // Could implement retry logic here
            }

            return formatJson(content);
        } else {
            console.error(`❌ Request failed: ${statusCode}`);

            // Enhanced error reporting
            try {
                const errorData = JSON.parse(content);
                if (errorData.error) {
                    console.error(`Error: ${errorData.error.code} - ${errorData.error.message}`);
                }
            } catch (e) {
                console.error(`Raw error: ${content}`);
            }

            return content;
        }
    } catch (error) {
        console.error(`Response processing error: ${error.message}`);
        return content;
    }
}

// Advanced utility functions
function processFileUpload(filePath, chunkSize = 1024 * 1024) {
    let clr = host.lib('mscorlib', 'System.IO');
    let File = clr.System.IO.File;
    let FileInfo = clr.System.IO.FileInfo;

    if (!File.Exists(filePath)) {
        throw new Error(`File not found: ${filePath}`);
    }

    let fileInfo = new FileInfo(filePath);
    let fileSize = fileInfo.Length;

    console.log(`📁 Processing file: ${filePath} (${fileSize} bytes)`);

    // For large files, implement chunked upload
    if (fileSize > chunkSize) {
        console.log("📤 Large file detected, using chunked upload...");
        return uploadFileInChunks(filePath, chunkSize);
    } else {
        return xk.fileSystem.readText(filePath);
    }
}
### Multi-Environment Management

Create sophisticated environment hierarchies with inheritance:
workspaces {
    // Base configuration
    microserviceBase {
        isHidden ~true
        description "Base configuration for microservice environments"

        properties {
            timeout 30000
            retryAttempts 3
            serviceName "user-service"
        }

        scripts {
            deployService {
                description "Deploy service to environment"
                arguments {
                    version { type "string" description "Service version to deploy" }
                    force { type "boolean" description "Force deployment" }
                }
                script <'
                    console.log(`🚀 Deploying ${workspace.serviceName} v${version} to ${workspace.environment}...`);

                    // Pre-deployment health check
                    const health = workspace.get_Health.execute();
                    if (!JSON.parse(health).healthy && !force) {
                        throw new Error("Environment unhealthy, use --force to override");
                    }

                    // Deployment logic
                    const result = workspace.post_Deploy.execute(version, workspace.environment);
                    console.log(`✅ Deployment completed: ${result}`);

                    // Post-deployment verification
                    setTimeout(() => {
                        workspace.verifyDeployment(version);
                    }, 10000);

                    return result;
                '>
            }

            rollback {
                description "Rollback to previous version"
                script <'
                    const lastVersion = xk.store.get(`${workspace.serviceName}_last_version`);
                    if (!lastVersion) {
                        throw new Error("No previous version found for rollback");
                    }

                    console.log(`⏪ Rolling back to version ${lastVersion}...`);
                    return workspace.deployService(lastVersion, true);
                '>
            }
        }

        preRequest <'
            // Add service metadata to all requests
            request.headers["X-Service-Name"] = workspace.serviceName;
            request.headers["X-Environment"] = workspace.environment;
            request.headers["X-Correlation-ID"] = generateUUID();
        '>
    }

    // Development environment
    dev {
        extend "microserviceBase"
        description "Development environment"
        baseUrl "http://localhost:8080"

        properties {
            environment "development"
            debugMode ~true
        }

        scripts {
            startLocalStack {
                description "Start local development stack"
                script <'
                    console.log("🔧 Starting local development stack...");

                    // Start database
                    xk.process.runCommand(false, ".", "docker-compose", "up -d postgres redis");

                    // Wait for services
                    console.log("⏳ Waiting for services to be ready...");
                    let attempts = 0;
                    while (attempts < 30) {
                        try {
                            workspace.get_Health.execute();
                            break;
                        } catch (e) {
                            attempts++;
                            if (attempts >= 30) throw new Error("Services failed to start");
                            Thread.Sleep(1000);
                        }
                    }

                    console.log("✅ Development stack ready");
                '>
            }
        }
    }

    // Production environment
    prod {
        extend "microserviceBase"
        description "Production environment"
        baseUrl "https://api.company.com"

        properties {
            environment "production"
            debugMode ~false
            requireApproval ~true
        }

        scripts {
            deployService {
                description "Deploy service with production safeguards"
                arguments {
                    version { type "string" description "Service version to deploy" }
                    approvalTicket { type "string" description "Approval ticket number" }
                }
                script <'
                    if (workspace.requireApproval && !approvalTicket) {
                        throw new Error("Production deployment requires approval ticket");
                    }

                    console.log(`🏭 Production deployment approved: ${approvalTicket}`);

                    // Store current version for rollback
                    const currentVersion = workspace.getCurrentVersion();
                    xk.store.set(`${workspace.serviceName}_last_version`, currentVersion);

                    return workspace.deployService(version, false);
                '>
            }
        }
    }
}
### Advanced Parameter Substitution

XferKit supports complex parameter replacement patterns:
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
            X-Request-Source <'xferkit-{{[env]::USERNAME}}'>
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
### Workspace Inheritance and Composition

Build complex workspace hierarchies:
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

                    xk.store.set("oauth_token", token);
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
            let token = xk.store.get("oauth_token");
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
                    xk.store.set("last_user_count", users.length);

                    return nextHandler();
                '>
            }
        }
    }
}
## 🌟 Real-World Examples

### CI/CD Pipeline Integration

Integrate XferKit into your deployment pipelines:
#!/bin/bash
# deploy.sh - Deployment script using XferKit

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
                xk.store.set("testUserId", userData.id);

            } catch (e) {
                errors.push(`User creation error: ${e.message}`);
            }

            // Test user retrieval
            try {
                let userId = xk.store.get("testUserId");
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
                let userId = xk.store.get("testUserId");
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
### Development Workflow Automation

Streamline your development workflow:
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
                    let gitStatus = xk.process.runCommand(true, ".", "git", "status --porcelain");
                    if (gitStatus.trim()) {
                        console.log("⚠️ Uncommitted changes detected");
                    }

                    // 2. Pull latest changes
                    console.log("📥 Pulling latest changes...");
                    xk.process.runCommand(false, ".", "git", "pull origin main");

                    // 3. Start services
                    console.log("🐳 Starting Docker services...");
                    xk.process.runCommand(false, workspace.dockerComposePath, "docker-compose", "up -d");

                    // 4. Run database migrations
                    console.log("🗄️ Running migrations...");
                    setTimeout(() => workspace.runMigrations(), 10000);

                    // 5. Start API in watch mode
                    console.log("👀 Starting API in watch mode...");
                    xk.process.run("npm", ".", "run", "dev");

                    console.log("✅ Development environment ready!");
                '>
            }

            endDay {
                description "Clean up development session"
                script <'
                    console.log("🌙 Ending development session...");

                    // 1. Stop services
                    xk.process.runCommand(false, workspace.dockerComposePath, "docker-compose", "down");

                    // 2. Show git status
                    let status = xk.process.runCommand(true, ".", "git", "status");
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
                            let result = xk.process.runCommand(true, ".", "pwsh", `-Command ${cmd}`);
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
### API Documentation Generation

Auto-generate API documentation from your XferKit workspace:
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
            xk.fileSystem.writeText("./docs/api-docs.json", docsJson);

            console.log(`✅ Documentation generated for ${docs.endpoints.length} endpoints`);
            return docs;
        '>
    }
}
## 🛠️ Command Reference

### Global Commands
# Get help
xk --help
xk <command> --help

# Set base URL globally
xk --baseurl https://api.example.com <command>

# Use specific workspace file
xk --workspace /path/to/workspace.xfer <command>
### HTTP Commands
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
### Workspace Commands
# List workspaces
xk workspace list

# Show workspace details
xk workspace show <name>

# Use workspace
xk workspace use <name>

# Execute workspace request
xk <workspace> <request> [options]
### Scripting Commands
# Execute JavaScript
xk script <code>

# Run workspace script
xk <workspace> <script> [arguments]
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
XferKit/
├── xk/                                    # Main CLI executable
├── ParksComputing.XferKit.Api/           # Core API interfaces
├── ParksComputing.XferKit.Http/          # HTTP services
├── ParksComputing.XferKit.Workspace/     # Workspace management
├── ParksComputing.XferKit.Scripting/     # JavaScript engine
├── ParksComputing.XferKit.DataStore/     # Data persistence
├── ParksComputing.XferKit.Diagnostics/   # Logging and diagnostics
└── .github/workflows/                    # CI/CD pipelines
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
