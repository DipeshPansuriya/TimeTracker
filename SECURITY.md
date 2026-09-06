# Security Policy

## Reporting a Vulnerability

If you discover a security vulnerability in TimeTracker, please report it responsibly to protect our users and community.

### How to Report

**DO NOT** open a public GitHub issue for security vulnerabilities.

Instead:

1. **Email Security Report**: Send details to `dipeshpansuriya@ymail.com` with subject line `[SECURITY] TimeTracker Vulnerability Report`
2. **Include in your report**:
   - Description of the vulnerability
   - Steps to reproduce
   - Potential impact
   - Suggested fix (if you have one)
   - Your contact information

### Response Timeline

- **Acknowledgment**: Within 24 hours
- **Initial Assessment**: Within 48 hours
- **Fix/Update**: Within 7 days (depending on severity)
- **Public Disclosure**: 30-90 days after patch release

### Severity Levels

#### 🔴 Critical (Score 9.0-10.0)
- Allows remote code execution
- Database compromise
- Complete authentication bypass
- Immediate patch required

#### 🟠 High (Score 7.0-8.9)
- Unauthorized data access
- Privilege escalation
- Denial of service
- Patch within 7 days

#### 🟡 Medium (Score 4.0-6.9)
- Partial data exposure
- Limited privilege escalation
- Patch within 30 days

#### 🟢 Low (Score 0.1-3.9)
- Minimal impact
- Requires specific conditions
- Patch within 90 days

## Security Best Practices

### For Users

1. **Keep Updated**
   - Always use the latest version
   - Subscribe to release notifications
   - Review security advisories

2. **Secure Configuration**
   ```csharp
   // ✅ DO: Use environment variables for secrets
   var apiKey = Environment.GetEnvironmentVariable("API_KEY");
   
   // ❌ DON'T: Hardcode secrets
   var apiKey = "secret-key-12345";
   ```

3. **Database Security**
   - Use strong, unique passwords
   - Enable encryption at rest
   - Regular backups
   - Principle of least privilege

4. **Network Security**
   - Use HTTPS only
   - Enable firewall rules
   - Use VPN for remote access
   - Disable unnecessary ports

### For Contributors

1. **Code Security**
   ```csharp
   // ❌ SQL Injection Risk
   string query = $"SELECT * FROM Users WHERE Email = '{email}'";
   
   // ✅ Parameterized Query
   string query = "SELECT * FROM Users WHERE Email = @Email";
   using (SqlCommand cmd = new SqlCommand(query, connection))
   {
       cmd.Parameters.AddWithValue("@Email", email);
       // ...
   }
   ```

2. **Input Validation**
   ```csharp
   // ✅ Validate and sanitize all inputs
   public class UserController : ControllerBase
   {
       [HttpPost]
       public ActionResult Create([FromBody] CreateUserRequest request)
       {
           if (!ModelState.IsValid) return BadRequest(ModelState);
           if (string.IsNullOrWhiteSpace(request.Email)) return BadRequest();
           if (!IsValidEmail(request.Email)) return BadRequest();
           // Process request...
       }
   }
   ```

3. **Avoid Common Vulnerabilities**
   - SQL Injection
   - Cross-Site Scripting (XSS)
   - Cross-Site Request Forgery (CSRF)
   - Broken Authentication
   - Sensitive Data Exposure
   - XML External Entities (XXE)
   - Path Traversal
   - Insecure Deserialization

4. **Secure Dependencies**
   ```bash
   # Check for vulnerable packages
   dotnet list package --vulnerable
   
   # Update packages
   dotnet package update
   ```

## Security Tools & Monitoring

### Automated Checks

1. **GitHub Secret Scanning**
   - Detects exposed credentials
   - Real-time alerts
   - Automatic remediation suggestions

2. **Dependabot**
   - Automatic vulnerability scans
   - Dependency update PRs
   - Security advisories

3. **SonarCloud**
   - Code quality analysis
   - Security hotspots
   - Coverage reports

4. **CodeQL**
   - Advanced vulnerability detection
   - Custom security rules
   - Semantic code analysis

### Manual Security Review

- All PRs require at least 1 approval
- Security-focused reviews for sensitive changes
- Regular penetration testing
- Quarterly security audits

## Security Headers & Configuration

### Recommended ASP.NET Core Configuration

```csharp
public void Configure(IApplicationBuilder app)
{
    // Security Headers
    app.Use(async (context, next) => {
        context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Add("X-Frame-Options", "DENY");
        context.Response.Headers.Add("X-XSS-Protection", "1; mode=block");
        context.Response.Headers.Add("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
        context.Response.Headers.Add("Content-Security-Policy", "default-src 'self'");
        await next();
    });
    
    // Use HTTPS
    app.UseHttpsRedirection();
    
    // Authentication & Authorization
    app.UseAuthentication();
    app.UseAuthorization();
}
```

## Data Protection

### Encryption

```csharp
// Encrypt sensitive data at rest
var encryptionProvider = new DataProtectionProvider();
var protector = encryptionProvider.CreateProtector("MyApp.Security");

// Encrypt
string encryptedData = protector.Protect(sensitiveData);

// Decrypt
string decryptedData = protector.Unprotect(encryptedData);
```

### GDPR Compliance

- Right to access
- Right to be forgotten
- Data portability
- Consent management
- Privacy by design

## Incident Response

### If a vulnerability is discovered:

1. **Immediate Actions** (< 1 hour)
   - Stop further exposure
   - Notify affected users
   - Gather evidence
   - Create incident ticket

2. **Short-term** (1-24 hours)
   - Develop fix
   - Test thoroughly
   - Deploy patch
   - Monitor for exploitation

3. **Follow-up** (1-7 days)
   - Post-incident review
   - Improve processes
   - Update documentation
   - Public disclosure (if applicable)

## Security Changelog

### Version 1.0.0
- Initial release
- Basic authentication
- API key validation
- Database encryption

## Dependencies & Third-Party Security

We regularly audit:
- NuGet packages
- Third-party APIs
- External services
- Database drivers

Check `DEPENDENCIES.md` for detailed information.

## Compliance

TimeTracker complies with:
- OWASP Top 10
- CWE/SANS Top 25
- GDPR requirements
- PCI DSS (if handling payments)

## Contact & Attribution

- **Security Lead**: Dipesh Pansuriya
- **Email**: dipeshpansuriya@ymail.com
- **Response Time**: Best effort basis
- **Disclosure Timeline**: 90-day standard + extension if needed

### Security Researchers

Thank you to all security researchers who responsibly disclose vulnerabilities to help keep TimeTracker secure.

## Additional Resources

- [OWASP Top 10](https://owasp.org/www-project-top-ten/)
- [CWE/SANS Top 25](https://cwe.mitre.org/top25/)
- [Microsoft Security Best Practices](https://docs.microsoft.com/en-us/dotnet/fundamentals/code-analysis/security)
- [NIST Cybersecurity Framework](https://www.nist.gov/cyberframework)

---

**Last Updated**: 2026-09-06
**Next Review**: 2026-12-06
