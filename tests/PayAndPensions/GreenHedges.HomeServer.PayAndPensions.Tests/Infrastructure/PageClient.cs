using System.Net;
using System.Text.RegularExpressions;

namespace GreenHedges.HomeServer.PayAndPensions.Tests.Infrastructure;

/// <summary>Loads pages and submits their forms, like a browser would.</summary>
public sealed class PageClient(HttpClient client)
{
    public async Task<PageResponse> GetAsync(string url) => await ReadAsync(await client.GetAsync(url));

    /// <summary>The raw response, e.g. for a file download.</summary>
    public Task<HttpResponseMessage> GetResponseAsync(string url) => client.GetAsync(url);

    /// <summary>
    /// Submits the form on <paramref name="page"/> to <paramref name="url"/>: its current values
    /// (enabled inputs, including the anti-forgery token and hidden fields, selected options and text areas),
    /// with <paramref name="fields"/> changed or added.
    /// </summary>
    public async Task<PageResponse> PostFormAsync(string url, PageResponse page, Dictionary<string, string>? fields = null) =>
        await ReadAsync(await client.PostAsync(url, new FormUrlEncodedContent(FormValues(page, fields))));

    /// <summary>Like <see cref="PostFormAsync"/>, but as multipart form data with a file in <paramref name="fileField"/>.</summary>
    public async Task<PageResponse> PostFileAsync(string url, PageResponse page, string fileField, string fileName, byte[] file,
        Dictionary<string, string>? fields = null)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in FormValues(page, fields)) content.Add(new StringContent(value), name);
        content.Add(new ByteArrayContent(file), fileField, fileName);
        return await ReadAsync(await client.PostAsync(url, content));
    }

    /// <summary>A form's current values, with <paramref name="fields"/> changed or added.</summary>
    private static Dictionary<string, string> FormValues(PageResponse page, Dictionary<string, string>? fields)
    {
        var data = new Dictionary<string, string>();
        void Add(string name, string value) => data.TryAdd(name, WebUtility.HtmlDecode(value));

        // Attributes can come in any order (an explicit value="" is written before the tag helper's name="").
        foreach (Match m in Regex.Matches(page.Html, "<input [^>]*>"))
            if (Attribute(m.Value, "name") is { } name && Attribute(m.Value, "value") is { } value &&
                !Regex.IsMatch(m.Value, @"\sdisabled\b") &&   // disabled fields aren't sent
                !(Attribute(m.Value, "type") is "checkbox" && !Regex.IsMatch(m.Value, @"\schecked\b")))   // unticked boxes aren't sent
                Add(name, value);
        foreach (Match m in Regex.Matches(page.Html, "<select[^>]*name=\"([^\"]+)\"[^>]*>(.*?)</select>", RegexOptions.Singleline))
            if (Regex.Match(m.Groups[2].Value, "<option[^>]*selected[^>]*>") is { Success: true } option)
                Add(m.Groups[1].Value, Regex.Match(option.Value, "value=\"([^\"]*)\"").Groups[1].Value);
        foreach (Match m in Regex.Matches(page.Html, "<textarea[^>]*name=\"([^\"]+)\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline))
            Add(m.Groups[1].Value, m.Groups[2].Value.TrimStart('\r', '\n'));

        foreach (var (name, value) in fields ?? []) data[name] = value;
        return data;
    }

    private static string? Attribute(string tag, string name) =>
        Regex.Match(tag, $"\\s{name}=\"([^\"]*)\"") is { Success: true } m ? m.Groups[1].Value : null;

    private static async Task<PageResponse> ReadAsync(HttpResponseMessage response) =>
        new(response.StatusCode, await response.Content.ReadAsStringAsync(), response.Headers.Location?.OriginalString);
}
