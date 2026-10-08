using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI:
// https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters.ValidateIssuerSigningKey = true;
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    ExceptionHandler = async context =>
    {
        var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        if (exception is BadHttpRequestException badRequest)
        {
            var statusCode = badRequest.StatusCode is >= 400 and < 500
                ? badRequest.StatusCode
                : StatusCodes.Status400BadRequest;

            await Results.Problem(
                statusCode: statusCode,
                title: "Invalid request",
                detail: "The request could not be processed.").ExecuteAsync(context);
            return;
        }

        app.Logger.LogError(exception, "Unhandled exception while processing the request.");
        await Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "An unexpected error occurred.",
            detail: "Please try again later.").ExecuteAsync(context);
    },
    SuppressDiagnosticsCallback = _ => true
});
app.UseRouting();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var path = context.Request.Path;
    var traceId = context.TraceIdentifier;

    app.Logger.LogInformation("HTTP {Method} {Path} started. TraceId: {TraceId}", method, path, traceId);
    context.Response.OnStarting(() =>
    {
        app.Logger.LogInformation(
            "HTTP {Method} {Path} completed with status {StatusCode}. TraceId: {TraceId}",
            method,
            path,
            context.Response.StatusCode,
            traceId);
        return Task.CompletedTask;
    });
    await next(context);
});
app.UseHttpsRedirection();
app.UseStatusCodePages();
app.UseAuthorization();

var users = new ConcurrentDictionary<int, UserResponse>();
var nextId = 0;
var userRoutes = app.MapGroup("/api/users").WithTags("Users").RequireAuthorization();

userRoutes.MapGet("/", (CancellationToken cancellationToken) =>
    TypedResults.Ok(users.Values.OrderBy(user => user.Id).ToArray()))
    .WithName("GetUsers")
    .WithSummary("Get all users")
    .WithDescription("Returns every user currently held in the in-memory store.");

userRoutes.MapGet("/{id:int}", Results<Ok<UserResponse>, NotFound> (int id, CancellationToken cancellationToken) =>
{
    return users.TryGetValue(id, out var user)
        ? TypedResults.Ok(user)
        : TypedResults.NotFound();
})
    .WithName("GetUserById")
    .WithSummary("Get a user by ID")
    .Produces<UserResponse>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

userRoutes.MapPost("/", Results<Created<UserResponse>, ValidationProblem> (CreateUserRequest request, CancellationToken cancellationToken) =>
{
    var errors = Validate(request);
    if (errors.Count > 0)
    {
        return TypedResults.ValidationProblem(errors);
    }

    var id = Interlocked.Increment(ref nextId);
    var user = new UserResponse(id, request.Name!.Trim(), request.Email!.Trim());
    users[id] = user;

    return TypedResults.Created($"/api/users/{id}", user);
})
    .WithName("CreateUser")
    .WithSummary("Create a user")
    .WithDescription("Creates a user after validating the name and email address.");

userRoutes.MapPut("/{id:int}", Results<Ok<UserResponse>, NotFound, ValidationProblem> (int id, UpdateUserRequest request, CancellationToken cancellationToken) =>
{
    var errors = Validate(request);
    if (errors.Count > 0)
    {
        return TypedResults.ValidationProblem(errors);
    }

    while (users.TryGetValue(id, out var currentUser))
    {
        var updatedUser = new UserResponse(id, request.Name!.Trim(), request.Email!.Trim());
        if (users.TryUpdate(id, updatedUser, currentUser))
        {
            return TypedResults.Ok(updatedUser);
        }
    }

    return TypedResults.NotFound();
})
    .WithName("UpdateUser")
    .WithSummary("Replace a user")
    .WithDescription("Replaces the name and email address for the specified user.");

userRoutes.MapDelete("/{id:int}", Results<NoContent, NotFound> (int id, CancellationToken cancellationToken) =>
    users.TryRemove(id, out _)
        ? TypedResults.NoContent()
        : TypedResults.NotFound())
    .WithName("DeleteUser")
    .WithSummary("Delete a user")
    .WithDescription("Deletes the specified user from the in-memory store.");

app.Run();

static Dictionary<string, string[]> Validate(object request)
{
    var validationResults = new List<ValidationResult>();
    Validator.TryValidateObject(request, new ValidationContext(request), validationResults, validateAllProperties: true);

    return validationResults
        .GroupBy(result => result.MemberNames.FirstOrDefault() ?? "Request")
        .ToDictionary(
            group => group.Key,
            group => group.Select(result => result.ErrorMessage ?? "The value is invalid.").ToArray());
}

/// <summary>
/// Request body for creating a user.
/// </summary>
sealed record CreateUserRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string? Name { get; init; }

    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; init; }
}

/// <summary>
/// Request body for replacing a user's name and email address.
/// </summary>
sealed record UpdateUserRequest
{
    [Required, StringLength(100, MinimumLength = 1)]
    public string? Name { get; init; }

    [Required, EmailAddress, StringLength(254)]
    public string? Email { get; init; }
}

/// <summary>
/// A user returned by the API.
/// </summary>
sealed record UserResponse(int Id, string Name, string Email);
