# UserManagementAPI

A .NET 10 minimal API for in-memory user CRUD. The `/api/users` endpoints require a JWT bearer token. See [MIDDLEWARE.md](MIDDLEWARE.md) for pipeline behavior, deployment configuration, and verification results.

## Run Locally

Restore and build, then start the HTTP launch profile:

```sh
dotnet restore
dotnet build
dotnet run --launch-profile http
```

The API listens at `http://localhost:5174`.

In another terminal, create a short-lived Development token with the .NET SDK tool:

```sh
dotnet user-jwts create --name local-dev --valid-for 1h --output token
```

The tool keeps its signing key in your local user-secrets store and configures Development issuer/audience settings. Keep the printed token private and do not commit it.

## Test Requests

Set the token and API base URL in the second terminal (replace the placeholder with the command output):

```sh
TOKEN='<local token>'
BASE='http://localhost:5174/api/users'
```

A request without a token should return 401; an authorized list request should return 200:

```sh
curl -i "$BASE"
curl -i -H "Authorization: Bearer $TOKEN" "$BASE"
```

Create a user (201), then use its returned `id` for GET, PUT, and DELETE:

```sh
curl -i -X POST "$BASE" \
  -H "Authorization: Bearer $TOKEN" \
  -H 'Content-Type: application/json' \
  -d '{"name":"Jordan Lee","email":"jordan.lee@example.com"}'
```

PUT accepts the same JSON shape and returns 200 for an existing ID. DELETE returns 204. An authenticated request with invalid data returns 400; an authenticated request for a nonexistent integer ID returns 404. An unauthenticated write returns 401 and does not modify the collection.

JWT validation cases (malformed, expired, incorrectly signed, wrong issuer, or wrong audience) all return 401. Request logs include method, path, final status, and trace identifier; they do not include credentials or bodies.

There is no separate automated test project in this repository. `dotnet build` and the manual HTTP checks above are the current verification workflow.
