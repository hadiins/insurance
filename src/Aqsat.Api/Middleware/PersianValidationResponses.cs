using Microsoft.AspNetCore.Mvc;

namespace Aqsat.Api.Middleware;

/// <summary>
/// Translates the framework's automatic model-binding/validation failures into Persian
/// ProblemDetails. Without this, an empty login body reaches the UI as "The Mobile field is
/// required." — English text inside an otherwise fully-Persian product (CLAUDE.md UI conventions).
/// Covers the two shapes [ApiController] emits: field errors (required/format/range) and whole-
/// request JSON parse failures (surfaced under the "$" and "request" keys, which are noise for the
/// user — the message "the request body is malformed" is the whole story).
/// </summary>
public static class PersianValidationResponses
{
    public static IActionResult InvalidModelStateResponse(ActionContext context)
    {
        var details = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "درخواست ارسالی نامعتبر است.",
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            Instance = context.HttpContext.Request.Path,
        };

        var messages = new List<string>();
        foreach (var (key, state) in context.ModelState)
        {
            if (state is null || state.Errors.Count == 0)
            {
                continue;
            }

            // "$" is the JSON-path root of a parse failure; "request" is the model binder's
            // fallback key for an unbindable body. Both mean "malformed request", not a field
            // problem, and the parse message itself is English framework text.
            if (key is "$" or "request")
            {
                messages.Add("ساختار درخواست ارسالی نامعتبر است.");
                continue;
            }

            foreach (var error in state.Errors)
            {
                messages.Add(Translate(key));
            }
        }

        if (messages.Count > 0)
        {
            details.Extensions["errors"] = messages.Distinct().ToList();
        }

        return new BadRequestObjectResult(details);
    }

    private static string Translate(string field) => $"مقدار ارسالی برای «{field}» نامعتبر است.";
}
