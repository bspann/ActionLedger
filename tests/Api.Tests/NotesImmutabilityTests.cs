using System.Text.Json;
using ActionLedger.Api.OpenApi;
using ActionLedger.Api.Routing;
using Xunit;

namespace ActionLedger.Api.Tests;

/// <summary>
/// ADR-002 / AD-5 — no endpoint updates or deletes notes, and nothing ever will without this test
/// going red. It is asserted against the generated contract rather than against a list of routes
/// kept here, so a controller added in a later story is covered the day it is written: the walk
/// takes every operation under the meetings prefix and every operation on any path that names
/// notes, so moving notes out from under <c>/meetings</c> does not move them out of this rule.
/// </summary>
/// <remarks>
/// Each assertion also proves it found something. A walk over a document with no Meeting
/// operations in it would otherwise satisfy "no DELETE exists" by visiting nothing at all.
/// </remarks>
public sealed class NotesImmutabilityTests
{
    private static readonly string MeetingsPrefix = $"/{ApiRoutes.Prefix}/meetings";

    private static readonly string[] Methods =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    [Fact]
    public async Task No_meeting_operation_deletes_or_patches_anything()
    {
        IReadOnlyList<(string Path, string Method)> operations = await MeetingOperationsAsync();

        Assert.NotEmpty(operations);

        string[] mutators =
        [
            .. operations
                .Where(operation => operation.Method is "delete" or "patch")
                .Select(operation => $"{operation.Method.ToUpperInvariant()} {operation.Path}"),
        ];

        Assert.True(
            mutators.Length == 0,
            $"Notes are write-once (AD-5, ADR-002): to change them a user creates a new meeting. "
                + $"Remove: {string.Join(", ", mutators)}");
    }

    [Fact]
    public async Task The_only_operation_on_a_notes_path_is_the_write_once_put()
    {
        IReadOnlyList<(string Path, string Method)> operations = await MeetingOperationsAsync();

        (string Path, string Method)[] onNotes =
        [
            .. operations.Where(operation => operation.Path.EndsWith("/notes", StringComparison.Ordinal)),
        ];

        // Finding the PUT is what stops this passing by visiting nothing.
        (string Path, string Method) single = Assert.Single(onNotes);

        Assert.Equal("put", single.Method);
        Assert.Equal($"{MeetingsPrefix}/{{id}}/notes", single.Path);
    }

    [Fact]
    public async Task The_contract_publishes_exactly_the_five_meeting_operations_that_exist_today()
    {
        IReadOnlyList<(string Path, string Method)> operations = await MeetingOperationsAsync();

        string[] published = [.. operations.Select(operation => $"{operation.Method} {operation.Path}").Order(StringComparer.Ordinal)];

        // The exact set, so a sixth operation under this prefix has to arrive through a decision
        // rather than through a controller someone extended. Story 2.5 added the runs POST; it is
        // a create, not a mutation of the notes, which is why the rules above still hold.
        Assert.Equal(
            [
                $"get {MeetingsPrefix}",
                $"get {MeetingsPrefix}/{{id}}",
                $"post {MeetingsPrefix}",
                $"post {MeetingsPrefix}/{{id}}/runs",
                $"put {MeetingsPrefix}/{{id}}/notes",
            ],
            published);
    }

    private static async Task<IReadOnlyList<(string Path, string Method)>> MeetingOperationsAsync()
    {
        await using TestApi api = new();

        // Boot the host: endpoints are attached to routing as it starts, and the document is
        // generated from them.
        using HttpClient _ = api.CreateClient();

        string json = await OpenApiExport.GenerateAsync(api.Services, TestContext.Current.CancellationToken);

        return
        [
            .. JsonDocument.Parse(json).RootElement
                .GetProperty("paths")
                .EnumerateObject()
                .Where(path => path.Name.Equals(MeetingsPrefix, StringComparison.Ordinal)
                    || path.Name.StartsWith(MeetingsPrefix + "/", StringComparison.Ordinal)
                    // Anything that names notes, wherever it is routed. The rule is about notes,
                    // not about a path prefix: a DELETE /api/v1/notes/{id} added by a later story
                    // sits outside the meetings prefix and has to fail this just the same.
                    || path.Name.Contains("notes", StringComparison.OrdinalIgnoreCase))
                .SelectMany(path => path.Value
                    .EnumerateObject()
                    .Where(operation => Methods.Contains(operation.Name, StringComparer.Ordinal))
                    .Select(operation => (path.Name, operation.Name))),
        ];
    }
}
