using System.Net.Http.Json;
using TelegramGroupsAdmin.Ui.Models;

namespace TelegramGroupsAdmin.Ui.Extensions;

/// <summary>
/// Extension methods for HttpResponseMessage to simplify API response handling.
/// </summary>
public static class HttpResponseMessageExtensions
{
    /// <summary>
    /// Reads the API response, handling both success and error cases.
    /// Returns an ApiResult containing either the successful response or an error message.
    /// </summary>
    /// <typeparam name="T">The response type implementing IApiResponse</typeparam>
    /// <param name="response">The HTTP response message</param>
    /// <param name="fallbackError">Error message to use if response body can't be read</param>
    /// <returns>ApiResult with either the successful response or an error.</returns>
    public static async Task<ApiResult<T>> ReadApiResponseAsync<T>(
        this HttpResponseMessage response,
        string fallbackError = "An unexpected error occurred. Please try again.") where T : class, IApiResponse
    {
        T? result = null;

        try
        {
            result = await response.Content.ReadFromJsonAsync<T>();
        }
        catch
        {
            // Response body wasn't valid JSON (e.g., 500 error with HTML)
        }

        if (response.IsSuccessStatusCode && result?.Success == true)
        {
            return ApiResult<T>.Success(result);
        }

        // Use API error if available, otherwise use fallback
        var error = result?.Error ?? fallbackError;
        return ApiResult<T>.Failure(error);
    }
}
