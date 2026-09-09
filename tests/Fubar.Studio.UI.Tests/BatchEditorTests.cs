using Fubar.Studio.Core.Models;
using Fubar.Studio.Core.Workspaces;
using Fubar.Studio.UI.ViewModels;

namespace Fubar.Studio.UI.Tests;

/// <summary>
/// Editing a batch without opening the file.
///
/// <para>The two facts under test are the two mistakes a hand-edited batch invites: a step naming an
/// endpoint that is not there, and a document whose name no longer matches the file it lives in -
/// which is what <c>@name</c> resolves against, so the second one makes a batch unrunnable by the
/// name it displays.</para>
/// </summary>
public class BatchEditorTests
{
    private const string Root = "/w";

    // ---- Fakes --------------------------------------------------------------------------------

    /// <summary>
    ///   collections/
    ///     auth/login/         (no cases)
    ///     orders/get-order/   (default, not-found)
    /// </summary>
    private sealed class Tree : IRequestStore
    {
        public event Action<string, IReadOnlyList<string>>? RequestMigrated { add { } remove { } }

        public IReadOnlyList<WorkspaceTreeNode> BuildCollectionsTree(string rootPath) =>
        [
            new WorkspaceTreeNode("auth", $"{Root}/collections/auth", true,
            [
                new WorkspaceTreeNode("login", $"{Root}/collections/auth/login", true, [])
                {
                    Kind = WorkspaceNodeKind.Endpoint,
                },
            ]),

            new WorkspaceTreeNode("orders", $"{Root}/collections/orders", true,
            [
                new WorkspaceTreeNode("get-order", $"{Root}/collections/orders/get-order", true,
                [
                    Case("default"),
                    Case("not-found"),
                ])
                {
                    Kind = WorkspaceNodeKind.Endpoint,
                },
            ]),
        ];

        private static WorkspaceTreeNode Case(string name) =>
            new(name, $"{Root}/collections/orders/get-order/cases/{name}.json", false, [])
            {
                Kind = WorkspaceNodeKind.Case,
            };

        public Task<RequestModel> LoadRequestAsync(string path, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveRequestAsync(string path, RequestModel request, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public string CreateRequest(string parentDirectory, string requestName) =>
            throw new NotSupportedException();

        public string CreateFolder(string parentDirectory, string folderName) =>
            throw new NotSupportedException();

        public string DuplicatePath(string path) => throw new NotSupportedException();

        public string RenamePath(string path, string newName) => throw new NotSupportedException();

        public void DeletePath(string path) => throw new NotSupportedException();
    }

    private sealed class Cases : IEndpointStore
    {
        public bool IsEndpoint(string directory) => directory.EndsWith("login") || directory.EndsWith("get-order");

        public string? EndpointDirectoryOf(string path) => null;

        public IReadOnlyList<CaseSummary> ListCases(string endpointDirectory) =>
            endpointDirectory.EndsWith("get-order")
                ? [new CaseSummary("default", $"{endpointDirectory}/cases/default.json"),
                   new CaseSummary("not-found", $"{endpointDirectory}/cases/not-found.json")]
                : [];

        public Task<EndpointCase> LoadCaseAsync(string caseFilePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveCaseAsync(string caseFilePath, EndpointCase endpointCase, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public string CreateCase(string endpointDirectory, string caseName) => throw new NotSupportedException();

        public string ProposeCasePath(string endpointDirectory, string caseName) =>
            throw new NotSupportedException();

        public string RenameCase(string caseFilePath, string newName) =>
            throw new NotSupportedException();

        public string CreateEndpoint(string parentDirectory, string endpointName) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingBatchStore : IBatchStore
    {
        public Batch? Saved { get; private set; }

        public string? SavedTo { get; private set; }

        public (string From, string To)? Renamed { get; private set; }

        public Exception? RenameFails { get; set; }

        public IReadOnlyList<BatchSummary> ListBatches(string workspaceRoot) => [];

        public Task<Batch> LoadBatchAsync(string batchFilePath, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Batch?> FindBatchAsync(string workspaceRoot, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task SaveBatchAsync(string batchFilePath, Batch batch, CancellationToken ct = default)
        {
            SavedTo = batchFilePath;
            Saved = batch;
            return Task.CompletedTask;
        }

        public string CreateBatch(string workspaceRoot, string name) => throw new NotSupportedException();

        public string ProposeBatchPath(string owner, string name) => throw new NotSupportedException();

        public string RenameBatch(string batchFilePath, string newName)
        {
            if (RenameFails is { } failure)
            {
                throw failure;
            }

            Renamed = (batchFilePath, newName);
            return $"{Root}/batches/{newName}.json";
        }
    }

    private static (BatchEditorViewModel Editor, RecordingBatchStore Store, StatusLogViewModel Log) Open(
        Batch batch, string fileName = "smoke")
    {
        var store = new RecordingBatchStore();
        var log = new StatusLogViewModel();

        var editor = new BatchEditorViewModel(
            batch,
            $"{Root}/batches/{fileName}.json",
            new Workspace { RootPath = Root, Manifest = new AppManifest { Name = "w" } },
            store,
            ["Staging", "Production"],
            new Tree(),
            new Cases(),
            log);

        return (editor, store, log);
    }

    // ---- What a step can name ------------------------------------------------------------------

    [Fact]
    public void Every_endpoint_and_folder_is_offered_as_a_target()
    {
        var (editor, _, _) = Open(new Batch { Name = "smoke" });

        Assert.Equal(
            ["auth", "auth/login", "orders", "orders/get-order"],
            editor.Targets);
    }

    [Fact]
    public void An_endpoints_cases_are_offered_beside_it()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/get-order")],
        });

        var step = editor.Steps.Single();

        Assert.True(step.CanChooseCase);
        Assert.Equal([BatchStepRowViewModel.EveryCase, "default", "not-found"], step.Cases);
        Assert.Equal(BatchStepRowViewModel.EveryCase, step.SelectedCase);
    }

    /// <summary>A folder step runs everything under it, which is not a case to choose.</summary>
    [Fact]
    public void A_folder_has_no_case_to_choose()
    {
        var (editor, _, _) = Open(new Batch { Name = "smoke", Steps = [new BatchStep("orders")] });

        var step = editor.Steps.Single();

        Assert.False(step.CanChooseCase);
        Assert.Equal("everything under it", step.WholeTargetDescription);
        Assert.Null(step.ToModel().Case);
    }

    [Fact]
    public void An_endpoint_with_no_cases_is_sent_as_it_stands()
    {
        var (editor, _, _) = Open(new Batch { Name = "smoke", Steps = [new BatchStep("auth/login")] });

        Assert.False(editor.Steps.Single().CanChooseCase);
        Assert.Equal("as it stands", editor.Steps.Single().WholeTargetDescription);
    }

    /// <summary>The one the file cannot tell you about.</summary>
    [Fact]
    public void A_step_naming_something_that_is_not_there_is_marked()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/get-order"), new BatchStep("orders/deleted-endpoint")],
        });

        Assert.False(editor.Steps[0].IsUnresolved);
        Assert.True(editor.Steps[1].IsUnresolved);
        Assert.Equal("not in this workspace", editor.Steps[1].WholeTargetDescription);
    }

    /// <summary>
    /// And it still SHOWS what it names.
    /// </summary>
    /// <remarks>
    /// A ComboBox whose selection is not among its items renders empty, so without the step's own name
    /// in its own list the one row a reader has to look at is the one blank box on the screen - which
    /// is how the first build of this shipped past a screenshot.
    /// </remarks>
    [Fact]
    public void An_unresolved_step_can_still_be_read()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/deleted-endpoint")],
        });

        var step = editor.Steps.Single();

        Assert.Contains("orders/deleted-endpoint", step.Targets);
        Assert.Equal("orders/deleted-endpoint", step.Target);

        // And it is not offered to any OTHER step - the workspace does not have it.
        Assert.DoesNotContain("orders/deleted-endpoint", editor.Targets);
    }

    /// <summary>Saving a batch you did not fix keeps the step it could not resolve, rather than
    /// dropping it - a batch that quietly shrank is one that keeps passing while testing less.</summary>
    [Fact]
    public void An_unresolved_step_survives_a_save()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/get-order", "default"), new BatchStep("orders/deleted-endpoint")],
        });

        Assert.Equal(
            ["orders/get-order", "orders/deleted-endpoint"],
            editor.ToModel().Steps.Select(s => s.Endpoint));
    }

    /// <summary>Retargeting a step keeps a case the new endpoint does not have, rather than silently
    /// pointing the step at a different one - it is wrong, and it has to keep looking wrong.</summary>
    [Fact]
    public void Retargeting_keeps_a_case_the_new_endpoint_does_not_have()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/get-order", "not-found")],
        });

        editor.Steps[0].Target = "auth/login";

        Assert.Equal("not-found", editor.Steps[0].SelectedCase);
    }

    // ---- Order ---------------------------------------------------------------------------------

    [Fact]
    public void Moving_a_step_changes_the_order_it_saves_in()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("auth/login"), new BatchStep("orders/get-order", "default")],
        });

        editor.MoveDownCommand.Execute(editor.Steps[0]);

        Assert.Equal(["orders/get-order", "auth/login"], editor.ToModel().Steps.Select(s => s.Endpoint));
    }

    [Fact]
    public void Cleanup_stays_in_its_own_list()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/get-order", "default")],
            Teardown = [new BatchStep("auth/login")],
        });

        editor.AddTeardownCommand.Execute(null);

        var model = editor.ToModel();

        Assert.Single(model.Steps);
        Assert.Equal(2, model.Teardown.Count);
    }

    // ---- What is written -----------------------------------------------------------------------

    [Fact]
    public void An_unedited_batch_round_trips()
    {
        var original = new Batch
        {
            Id = "abc",
            Name = "smoke",
            Description = "The morning check",
            Steps = [new BatchStep("orders/get-order", "default"), new BatchStep("auth/login")],
            Teardown = [new BatchStep("orders/get-order", "not-found")],
            Oracle = new BatchOracle(BatchOracleKind.Snapshot),
            Environments = ["Staging"],
            Options = new BatchOptions { StopOnFailure = true, DelayMs = 250 },
        };

        var written = Open(original).Editor.ToModel();

        Assert.Equal("abc", written.Id);
        Assert.Equal("smoke", written.Name);
        Assert.Equal("The morning check", written.Description);
        Assert.Equal(
            [("orders/get-order", "default"), ("auth/login", null)],
            written.Steps.Select(s => (s.Endpoint, s.Case)));
        Assert.Equal([("orders/get-order", "not-found")], written.Teardown.Select(s => (s.Endpoint, s.Case)));
        Assert.Equal(BatchOracleKind.Snapshot, written.Oracle?.Kind);
        Assert.Equal(["Staging"], written.Environments);
        Assert.True(written.Options?.StopOnFailure);
        Assert.Equal(250, written.Options?.DelayMs);
    }

    /// <summary>An overlay is the occasion's own comparison rules. This screen does not show them, and
    /// dropping what it does not show would be a silent deletion.</summary>
    [Fact]
    public void An_overlay_the_editor_does_not_show_survives_a_save()
    {
        var overlay = new BatchOverlay { Tolerances = [] };

        var written = Open(new Batch { Name = "smoke", Overlay = overlay }).Editor.ToModel();

        Assert.Same(overlay, written.Overlay);
    }

    /// <summary>The second environment is written to both places it is read from, so a batch saved
    /// here runs the same way from the window and from the command line.</summary>
    [Fact]
    public void A_comparison_names_both_environments()
    {
        var (editor, _, _) = Open(new Batch { Name = "drift" });

        editor.SelectedOracle = editor.Oracles.Single(o => o.Kind == BatchOracleKind.Environment);
        editor.PrimaryEnvironment = "Staging";
        editor.OtherEnvironment = "Production";

        var written = editor.ToModel();

        Assert.Equal("Production", written.Oracle?.Environment);
        Assert.Equal(["Staging", "Production"], written.Environments);
    }

    /// <summary>Only when it is used: an environment left over from a comparison would otherwise send
    /// every step twice on a batch that is no longer comparing anything.</summary>
    [Fact]
    public void The_second_environment_is_dropped_when_nothing_compares()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "drift",
            Oracle = new BatchOracle(BatchOracleKind.Environment, "Production"),
            Environments = ["Staging", "Production"],
        });

        editor.SelectedOracle = editor.Oracles.Single(o => o.Kind == BatchOracleKind.Snapshot);

        Assert.Equal(["Staging"], editor.ToModel().Environments);
    }

    [Fact]
    public void No_environment_means_whichever_is_active()
    {
        var (editor, _, _) = Open(new Batch { Name = "smoke", Environments = ["Staging"] });

        editor.PrimaryEnvironment = "";

        Assert.Empty(editor.ToModel().Environments);
    }

    // ---- Dirty ---------------------------------------------------------------------------------

    [Fact]
    public void Opening_a_batch_does_not_dirty_it()
    {
        var (editor, _, _) = Open(new Batch
        {
            Name = "smoke",
            Steps = [new BatchStep("orders/get-order", "default")],
            Oracle = new BatchOracle(BatchOracleKind.Snapshot),
            Environments = ["Staging"],
            Options = new BatchOptions { StopOnFailure = true, DelayMs = 100 },
        });

        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void Changing_a_step_dirties_it()
    {
        var (editor, _, _) = Open(new Batch { Name = "smoke", Steps = [new BatchStep("auth/login")] });

        editor.Steps[0].Target = "orders/get-order";

        Assert.True(editor.IsDirty);
    }

    // ---- The name is the file's ----------------------------------------------------------------

    /// <summary>A batch opens on the FILE's name, not the document's: <c>@smoke</c> resolves against
    /// the directory listing, so the file name is the one anything can run it by.</summary>
    [Fact]
    public void The_name_shown_is_the_files()
    {
        var (editor, _, _) = Open(new Batch { Name = "something else" }, fileName: "smoke");

        Assert.Equal("smoke", editor.Name);
    }

    [Fact]
    public async Task Saving_an_unchanged_name_renames_nothing()
    {
        var (editor, store, _) = Open(new Batch { Name = "smoke" });

        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Null(store.Renamed);
        Assert.Equal($"{Root}/batches/smoke.json", store.SavedTo);
        Assert.False(editor.IsDirty);
    }

    /// <summary>The rename is the point: a document called "nightly" in a file called "smoke.json" is
    /// one that <c>@nightly</c> cannot find and <c>@smoke</c> runs under another name.</summary>
    [Fact]
    public async Task Saving_a_new_name_renames_the_file()
    {
        var (editor, store, _) = Open(new Batch { Name = "smoke" });

        editor.Name = "nightly";
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal(($"{Root}/batches/smoke.json", "nightly"), store.Renamed);
        Assert.Equal("nightly", store.Saved?.Name);
        Assert.Equal($"{Root}/batches/nightly.json", editor.FilePath);
    }

    /// <summary>Written first, renamed second - so a rename that fails leaves the batch where it was
    /// with its new contents, rather than a saved document nobody can find.</summary>
    [Fact]
    public async Task A_failed_rename_keeps_the_contents_and_puts_the_name_back()
    {
        var (editor, store, log) = Open(new Batch { Name = "smoke" });
        store.RenameFails = new IOException("There is already a batch called \"nightly\".");

        editor.Name = "nightly";
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Equal($"{Root}/batches/smoke.json", store.SavedTo);
        Assert.Equal("smoke", editor.Name);
        Assert.Contains(log.Entries, e => e.IsError && e.Message.Contains("already a batch"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("orders/smoke")]
    [InlineData("smoke?")]
    public async Task A_name_that_cannot_be_a_file_name_is_refused(string name)
    {
        var (editor, store, log) = Open(new Batch { Name = "smoke" });

        editor.Name = name;
        await editor.SaveCommand.ExecuteAsync(null);

        Assert.Null(store.Saved);
        Assert.Contains(log.Entries, e => e.IsError);
    }
}
