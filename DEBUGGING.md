# API Reliability Review

## Project and dependencies

The project targets .NET 10 with nullable reference types and implicit usings enabled. Its only direct package references are `Microsoft.AspNetCore.OpenApi` 10.0.9 and `Microsoft.OpenApi` 2.7.5. There is no test project or test framework dependency; the user store is in memory.

## Existing protections

- `POST` and `PUT` already validate request DTOs with DataAnnotations. Runtime checks confirmed missing, `null`, empty, and whitespace-only names, plus invalid email values, return 400 validation Problem Details. These validations were present before this activity and were not introduced as fixes.
- `GET`, `PUT`, and `DELETE` already return 404 for nonexistent integer IDs. Runtime checks confirmed all three behaviors before editing.
- The in-memory store already uses `ConcurrentDictionary`; ID allocation uses `Interlocked.Increment`, updates use `TryUpdate` in a retry loop, and deletion uses atomic `TryRemove`.
- The application already registered Problem Details and `UseExceptionHandler`. Before editing, malformed request-body binding exceptions were nevertheless treated as 500 responses.

## Confirmed issue and change

Minimal API body binding throws `BadHttpRequestException` for missing, malformed, and explicit `null` JSON bodies. Before the change, all three cases returned 500. The existing centralized exception-handler pipeline in `Program.cs` now maps those request exceptions to their 4xx status (400 for these cases) with generic Problem Details. Framework diagnostics for handled exceptions are suppressed to avoid logging expected bad requests as server errors. Other exceptions pass through the same centralized handler, are logged with `ILogger`, and return a generic 500 Problem Details response. There are no repetitive endpoint-level try/catch blocks, and validation responses and normal 404 results remain unchanged.

## GET list performance

`GET /api/users` sorts all values by ID and materializes an array. This costs `O(n log n)` sorting plus `O(n)` result storage, but gives clients a stable, deterministic ID order. Removing the sort could change observed response order; introducing pagination would change the response contract. No representative load benchmark or ordering requirement was provided, so sorting and response shape were preserved.

## Verification

`dotnet build` completed successfully after the code change.

Manual HTTP checks against the running API:

| Check | Result |
| --- | --- |
| POST with missing body, malformed JSON, or JSON `null` | All returned 400 Problem Details; before the change all returned 500. |
| PUT with missing body, malformed JSON, or JSON `null` | All returned 400 Problem Details. |
| POST and PUT with missing, `null`, empty, or whitespace-only name, or invalid email | All returned 400 validation responses. |
| Valid POST | Returned 201. |
| GET, PUT, and DELETE for a nonexistent integer ID | All returned 404. |
| 25 concurrent creates | All 25 returned 201 and had unique IDs. |
| 20 concurrent updates to one existing user | All 20 returned 200. |
| Eight concurrent updates racing with eight deletes | Returned only valid 200/204/404 outcomes; this run observed 404 and 204. A subsequent GET returned 404. |
| GET collection | Returned 200 with a JSON array. |
| Malformed-body response detail | Did not include exception class names or internal framework details. |
| Final logging adjustment | A malformed POST still returned 400 and emitted no error-level exception dump. |

Unexpected-exception handling was verified with a temporary `GET /__debug/throw` endpoint guarded by the Development environment. It threw `InvalidOperationException("controlled test exception detail")`. The response was HTTP 500 `application/problem+json` with the generic title `An unexpected error occurred.` and detail `Please try again later.`; it contained no exception type, controlled message, or stack trace. The server log contained the `Unhandled exception while processing the request.` entry, the exception type/message, and stack trace. The temporary endpoint was removed and the final application build succeeded. A request to the former route on the rebuilt application returned 404.

**Not run:** `dotnet test` was not run because the project contains no test project or test dependencies.

## Copilot assistance

Copilot inspected `Program.cs` and the project dependencies, exercised the baseline and updated HTTP behavior, identified the body-binding status-code defect, updated the existing centralized exception handler, ran the build and endpoint checks, and documented the results here. No new dependencies were added.
