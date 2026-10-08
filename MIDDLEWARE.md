# Authentication and Middleware

## Authentication

The API uses `Microsoft.AspNetCore.Authentication.JwtBearer` 10.0.9, compatible with the project's `net10.0` target. JWT validation requires a valid signature, issuer, audience, and lifetime. Clock skew is set to zero. All endpoints in `/api/users` inherit `RequireAuthorization()` from the route group; a missing or invalid bearer token receives 401. The existing CRUD routes, response shapes, and request validation are unchanged.

No token or signing secret is hardcoded in the application. The local `dotnet user-jwts` signing key is stored in the developer's user-secrets store. The generated issuer and audience values in `appsettings.Development.json` are identifiers, not signing secrets.

## Pipeline Order

The configured request pipeline is:

1. `UseExceptionHandler` is first. It maps malformed/incomplete request bodies to 4xx Problem Details. Unexpected exceptions are logged server-side and produce a generic 500 Problem Details response without exception details.
2. `UseRouting` selects an endpoint and continues through the pipeline.
3. `UseAuthentication` validates the bearer token and sets the user. It does not challenge or stop the request itself.
4. Request logging records method, path, and trace identifier on entry. It registers one `OnStarting` callback to record the final status after downstream middleware and exception handling have set it. It does not log headers or bodies.
5. `UseHttpsRedirection` runs inside the logger, so redirects are audited too.
6. `UseStatusCodePages` supplies Problem Details for eligible bodyless error responses.
7. `UseAuthorization` enforces endpoint policies after logging, so rejected requests are audited.
8. Endpoint dispatch runs the matched handler. The `/api/users` group requires authorization.

There is no normal response-producing short circuit before request logging: routing continues, authentication records a principal without challenging, and HTTPS redirection and authorization are both downstream. The exception handler wraps the whole pipeline. The logging middleware emits one entry record and one completion record per request; the completion callback observes handled 400/500 responses at response start, preventing premature 200 logs or duplicate completion logs.

## Local Development Tokens

The installed .NET 10 SDK includes `dotnet user-jwts`. From the project directory, create a local token:

```sh
dotnet user-jwts create --name local-dev --valid-for 1h
```

The command prints a token and writes the local issuer/audience settings for the configured launch URLs. Keep the token out of source control and use it only for local testing. Start the API with:

```sh
dotnet run --launch-profile http
```

In another terminal, provide the token as a bearer credential:

```sh
curl -i \
  -H "Authorization: Bearer <paste-local-token>" \
  http://localhost:5174/api/users
```

Without the header, the same request returns 401. `dotnet user-jwts` stores signing material outside the repository in the local user-secrets infrastructure; do not copy it into `appsettings*.json` or commit tokens.

### Deployment Configuration

Do not use `dotnet user-jwts` as a production identity provider. Configure the deployed bearer scheme with the trusted issuer, audience, and signature-verification source (for example, an OIDC authority/JWKS endpoint or signing keys supplied by the deployment secret manager). Set these through deployment configuration or environment-specific secret storage, never source-controlled signing secrets. Keep production issuer/audience values distinct from local Development settings.

## Verification Results

The following checks were run against the Development API using local `dotnet user-jwts` tokens:

| Scenario | Observed result |
| --- | --- |
| Missing bearer token | 401 Problem Details. |
| Malformed token | 401. |
| Expired token | 401. |
| Modified/incorrect signature | 401. |
| Incorrect issuer | 401. |
| Incorrect audience | 401. |
| Valid token: GET, POST, PUT, DELETE | GET 200; create 201; update 200; delete 204. |
| Unauthenticated POST and PUT | 401; subsequent authorized GET confirmed existing data was unchanged. |
| Authenticated malformed JSON | 400 Problem Details. |
| Authenticated nonexistent user ID | 404. |
| Controlled unexpected exception | 500 Problem Details with generic title/detail; no exception type, message, or stack trace in the response. The server log contained the exception and stack trace. |
| HTTP-to-HTTPS redirect with a configured local redirect port | 307. The request produced exactly one start and one completion log with the same trace identifier. |
| Completion logging | Observed final 401, 400, 404, and 500 statuses, with method, path, and trace identifier. A handled exception logged 500 rather than the pre-handler 200. |
| Removed temporary exception route | Returned 404 in the rebuilt final application. |

The unexpected exception used a temporary Development-only endpoint. That endpoint was removed, and the final application was rebuilt. The final `dotnet build` succeeded. No token-issuing endpoint is part of the application.

**Not run:** `dotnet test` was not run because the repository has no test project or test framework. Endpoint behavior was verified with manual HTTP requests.

## Copilot Assistance

Copilot inspected the existing middleware and CRUD setup, added the compatible JWT bearer dependency and route-group authorization, configured request and final-status logging, ran the local token and endpoint checks, identified and corrected premature completion logging for handled exceptions, removed the temporary exception probe, and prepared these instructions and the README run/test guide.
