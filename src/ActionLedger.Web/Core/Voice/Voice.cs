namespace ActionLedger.Web.Core;

/// <summary>
/// UX-DR20 — the client-side string vocabulary. EXPERIENCE.md specifies this copy verbatim, so it
/// is declared once here and used through these constants; <c>VoiceAndFormatsTests</c> pins every
/// one of them to its literal, ordinally, and fails naming the constant that drifted.
/// </summary>
/// <remarks>
/// The namespace is <c>ActionLedger.Web.Core</c> rather than <c>ActionLedger.Web.Core.Voice</c>,
/// although the file sits in <c>Core/Voice/</c>. A namespace and a type that share a name do not
/// coexist: from inside <c>ActionLedger.Web.Core.Errors</c> the simple name <c>Voice</c> binds to
/// the enclosing namespace's child namespace before it ever reaches a using-imported type, and
/// <c>Voice.UnexpectedFailureTitle</c> stops compiling. Declaring the class one level up removes
/// the ambiguity entirely — there is no namespace named <c>Voice</c> to lose to.
/// </remarks>
public static class Voice
{
    /// <summary>The product name. The toolbar's home link and the login heading.</summary>
    public const string ProductName = "ActionLedger";

    /// <summary>The first toolbar destination (EXPERIENCE.md, Information Architecture).</summary>
    public const string Meetings = "Meetings";

    /// <summary>The second toolbar destination.</summary>
    public const string Actions = "Actions";

    /// <summary>The login button. EXPERIENCE.md: "The PRD's 'log in' is the same act."</summary>
    public const string SignIn = "Sign in";

    /// <summary>The only item in the user menu.</summary>
    public const string SignOut = "Sign out";

    /// <summary>
    /// UX-DR18 — shown under the button on a refusal. It never says which half was wrong, which
    /// is the same discipline the server's own 401 detail keeps.
    /// </summary>
    public const string SignInFailed = "Sign-in failed. Check your username and password.";

    /// <summary>The username field's visible label.</summary>
    public const string Username = "Username";

    /// <summary>The password field's visible label.</summary>
    public const string Password = "Password";

    /// <summary>The action on the load-failure notice, and on a failed sign-in submit.</summary>
    public const string Retry = "Retry";

    /// <summary>The unmatched-route notice (EXPERIENCE.md, State Patterns, "Not found").</summary>
    public const string NotFound = "Not found.";

    /// <summary>
    /// UX-DR19 — the load-failure sentence, completed by the server's problem title. The
    /// apostrophe is U+2019 RIGHT SINGLE QUOTATION MARK, exactly as EXPERIENCE.md writes it, and
    /// the pinning test compares ordinally so an ASCII <c>'</c> fails.
    /// </summary>
    public const string LoadFailurePrefix = "Couldn\u2019t load. ";

    /// <summary>
    /// The title used when a failure carries no problem body. A 500 arrives with neither
    /// <c>type</c> nor <c>title</c>, and a transport failure is not a response at all, so this
    /// completes <see cref="LoadFailurePrefix"/> in both cases rather than leaving it dangling.
    /// </summary>
    public const string UnexpectedFailureTitle = "An unexpected error occurred.";

    /// <summary>UX-DR18 — the snackbar raised when a request that carried a token answers 401.</summary>
    public const string SessionExpired = "Session expired. Sign in again.";

    /// <summary>UX-DR19 — the snackbar raised on 403.</summary>
    public const string RoleNotAllowed = "Your role does not allow this.";

    /// <summary>UX-DR19 — the snackbar raised on 409. No Retry is offered with it.</summary>
    public const string AlreadyChanged = "Already changed. Reloading.";

    /// <summary>
    /// EXPERIENCE.md, Meeting List — the button that opens the New meeting dialog. It appears in
    /// the list header and again in the empty state, where it is the primary action.
    /// </summary>
    public const string NewMeeting = "New meeting";

    /// <summary>EXPERIENCE.md, State Patterns, "No Meetings" — the Meeting List empty state.</summary>
    public const string NoMeetings = "No meetings yet.";

    /// <summary>EXPERIENCE.md, Meeting table — the first column, and the dialog's title field.</summary>
    public const string Title = "Title";

    /// <summary>EXPERIENCE.md, Meeting table — the second column, and the dialog's date field.</summary>
    public const string Date = "Date";

    /// <summary>EXPERIENCE.md, Meeting table — the run count column. Zero until Story 2.5.</summary>
    public const string Runs = "Runs";

    /// <summary>
    /// EXPERIENCE.md, Meeting table — the tracked-action count column. The Glossary spells it
    /// "Tracked Action", so the column keeps both capitals.
    /// </summary>
    public const string TrackedActions = "Tracked Actions";

    /// <summary>EXPERIENCE.md, New meeting dialog — the chip input's visible label.</summary>
    public const string Attendees = "Attendees";

    /// <summary>EXPERIENCE.md, New meeting dialog — the confirming button.</summary>
    public const string Create = "Create";

    /// <summary>
    /// The dismissing button on every dialog. EXPERIENCE.md's Interaction Primitives give every
    /// confirm dialog a way out that is not the Escape key.
    /// </summary>
    public const string Cancel = "Cancel";

    /// <summary>
    /// Meeting Detail's heading while the meeting is still loading, and when the load failed. The
    /// page has to render an h1 in every state it can be in: App.razor's
    /// <c>&lt;FocusOnNavigate Selector="h1" /&gt;</c> looks once, after the first render following
    /// the navigation, and the title is not known yet.
    /// </summary>
    public const string Meeting = "Meeting";

    /// <summary>EXPERIENCE.md, Notes paste area — the region heading and the textarea's label.</summary>
    public const string Notes = "Notes";

    /// <summary>EXPERIENCE.md, Notes paste area — the write-once action, and its confirm button.</summary>
    public const string SaveNotes = "Save notes";

    /// <summary>
    /// Announced through the snackbar when the write succeeds. The notes region swaps to its
    /// read-only branch in the same render, which takes the Save button — the control MudBlazor
    /// returns focus to — out of the tree, so without this the one irreversible write in the app
    /// is the only outcome that says nothing.
    /// </summary>
    public const string NotesSaved = "Notes saved.";

    /// <summary>
    /// ADR-002 made visible. EXPERIENCE.md, Notes paste area: the caption beside the button, and
    /// the same sentence repeated by the confirm dialog, which is why it is one constant.
    /// </summary>
    public const string NotesImmutable = "Notes cannot be changed after saving";

    /// <summary>
    /// EXPERIENCE.md, Notes paste area — the read-only caption, opened by this and closed by
    /// <see cref="NotesSavedSuffix"/> around <c>Formats.Instant</c>. Declared as a pair for the
    /// same reason <see cref="LoadFailurePrefix"/> is: the sentence is assembled in one place.
    /// </summary>
    public const string NotesSavedPrefix = "Saved ";

    /// <summary>The second half of the read-only notes caption. See <see cref="NotesSavedPrefix"/>.</summary>
    public const string NotesSavedSuffix = ", immutable";

    /// <summary>
    /// EXPERIENCE.md, New meeting dialog — "Validation messages under each field". Declarative
    /// and per-field, so the message names what is missing rather than saying the form is wrong.
    /// </summary>
    public const string TitleRequired = "Title is required.";

    /// <summary>The Title field's length message. The contract caps it at 200 characters.</summary>
    public const string TitleTooLong = "Title must be 200 characters or fewer.";

    /// <summary>The Date field's message. EXPERIENCE.md makes the date required.</summary>
    public const string DateRequired = "Date is required.";

    /// <summary>
    /// The attendee chip input's message. EXPERIENCE.md bounds each chip at 1 to 100 characters;
    /// the empty case is silent because an empty entry is simply not added.
    /// </summary>
    public const string AttendeeTooLong = "An attendee must be 100 characters or fewer.";

    /// <summary>
    /// The placeholder for a field with no value, used by Meeting Detail for a meeting nobody
    /// attended. An ASCII hyphen rather than an em dash: this vocabulary is ASCII plus U+2019 and
    /// U+00B7.
    /// </summary>
    public const string NoValue = "-";

    /// <summary>
    /// EXPERIENCE.md, Voice and Tone — the button that starts a run on Meeting Detail. "Run
    /// extraction", never "Let AI find your actions".
    /// </summary>
    public const string RunExtraction = "Run extraction";

    /// <summary>
    /// EXPERIENCE.md, Run extraction button — the visible caption beneath the disabled button when
    /// the Meeting has no notes. Visible text, never a tooltip: tooltips on disabled controls are banned.
    /// </summary>
    public const string AddNotesFirst = "Add notes first";

    /// <summary>
    /// The in-flight caption, opened by this, completed by the provider,
    /// <see cref="ProviderModelSeparator"/>, the model, and <see cref="ExtractingSuffix"/>. The
    /// provider and model are runtime values from <c>GET /api/v1/ai/provider</c>, so the sentence is
    /// three constants rather than one.
    /// </summary>
    public const string ExtractingWithPrefix = "Extracting with ";

    /// <summary>
    /// Between an AI Provider and its model, in the in-flight caption and the run list. U+00B7 MIDDLE
    /// DOT, exactly as EXPERIENCE.md writes it; the one code point above ASCII besides U+2019.
    /// </summary>
    public const string ProviderModelSeparator = " \u00B7 ";

    /// <summary>The close of the in-flight caption. See <see cref="ExtractingWithPrefix"/>.</summary>
    public const string ExtractingSuffix = ". This can take up to a minute with a local model.";

    /// <summary>
    /// The in-flight caption when the provider read failed. The page still loads and the run can
    /// still start; only the two names are missing, so the sentence drops them rather than a blank.
    /// </summary>
    public const string ExtractingWithoutProvider = "Extracting. This can take up to a minute with a local model.";

    /// <summary>EXPERIENCE.md, State Patterns, "Meeting without notes" — the run list's empty state.</summary>
    public const string NoRuns = "No extraction runs. Add notes, then run extraction.";

    /// <summary>EXPERIENCE.md, "Extraction Failed" — the button a failed run offers on Run Detail.</summary>
    public const string RunAgain = "Run again";

    /// <summary>EXPERIENCE.md, Low Confidence badge — the text beside the icon. Never color alone.</summary>
    public const string LowConfidence = "Low confidence";

    /// <summary>EXPERIENCE.md, State Patterns, "Zero Proposed Actions".</summary>
    public const string NoProposals = "The AI found no actions in these notes.";

    /// <summary>
    /// EXPERIENCE.md, Voice and Tone — "Extraction failed. {server-supplied reason}". The reason
    /// follows verbatim; nothing here rewords it.
    /// </summary>
    public const string ExtractionFailedPrefix = "Extraction failed. ";

    /// <summary>Meeting Detail's run-list heading. The Glossary's "Extraction Run", in sentence case.</summary>
    public const string ExtractionRuns = "Extraction runs";

    /// <summary>Run Detail's heading, and what the page is while the run is still loading.</summary>
    public const string ExtractionRun = "Extraction run";

    /// <summary>Run Detail's link back to the Meeting the run belongs to.</summary>
    public const string BackToMeeting = "Back to meeting";

    /// <summary>The run list's first column, and Run Detail's start-time term.</summary>
    public const string Started = "Started";

    /// <summary>The Glossary's "Prompt Version", capitals kept, as a column and a term.</summary>
    public const string PromptVersion = "Prompt Version";

    /// <summary>The Glossary's "AI Provider", as a column and a term.</summary>
    public const string AiProvider = "AI Provider";

    /// <summary>
    /// The run list's column whose cell reads "{provider} · {model}" (EXPERIENCE.md, Run list).
    /// </summary>
    public const string AiProviderAndModel = "AI Provider and model";

    /// <summary>Run Detail's model term.</summary>
    public const string Model = "Model";

    /// <summary>Run Detail's schema-version term.</summary>
    public const string SchemaVersion = "Schema version";

    /// <summary>Run Detail's duration term.</summary>
    public const string Duration = "Duration";

    /// <summary>What follows the millisecond count, as <c>{n} ms</c>.</summary>
    public const string MillisecondsSuffix = " ms";

    /// <summary>Run Detail's input-token term. The value renders "0" for the Fake, never blank.</summary>
    public const string InputTokens = "Input tokens";

    /// <summary>Run Detail's output-token term.</summary>
    public const string OutputTokens = "Output tokens";

    /// <summary>The run list's outcome column, and Run Detail's outcome term.</summary>
    public const string Outcome = "Outcome";

    /// <summary>The outcome of a run whose output validated.</summary>
    public const string Succeeded = "Succeeded";

    /// <summary>The outcome of a run whose output did not.</summary>
    public const string Failed = "Failed";

    /// <summary>Run Detail's failure-reason term, shown only on a Failed run.</summary>
    public const string FailureReason = "Failure reason";

    /// <summary>Run Detail's warnings term. Its value is the count.</summary>
    public const string Warnings = "Warnings";

    /// <summary>The expansion that lists each dropped Source Excerpt, one line per warning.</summary>
    public const string DroppedExcerpts = "Dropped excerpts";

    /// <summary>The run list's proposal-count column, and Run Detail's proposals heading.</summary>
    public const string Proposals = "Proposals";

    /// <summary>
    /// The run list's Pending-count column, and the Pending Review State's name on its chip. One
    /// word, one constant.
    /// </summary>
    public const string Pending = "Pending";

    /// <summary>The Approved Review State's name.</summary>
    public const string Approved = "Approved";

    /// <summary>The Edited Review State's name.</summary>
    public const string Edited = "Edited";

    /// <summary>The Rejected Review State's name.</summary>
    public const string Rejected = "Rejected";

    /// <summary>Run Detail's proposal description column.</summary>
    public const string Description = "Description";

    /// <summary>Run Detail's Confidence Score column.</summary>
    public const string Confidence = "Confidence";

    /// <summary>Run Detail's Review State column.</summary>
    public const string ReviewState = "Review state";

    /// <summary>Run Detail's decider column. Empty while a proposal is Pending.</summary>
    public const string DecidedBy = "Decided by";

    /// <summary>
    /// Run Detail's decision-timestamp column, and the heading of an Edited card's decided-values
    /// column on the Review Screen. Empty on Run Detail while a proposal is Pending.
    /// </summary>
    public const string Decided = "Decided";

    /// <summary>Run Detail's rejection-reason column. Empty unless a rejection gave a reason.</summary>
    public const string RejectionReason = "Rejection reason";

    /// <summary>The Review Screen's heading, in every state it can be in (EXPERIENCE.md mockup).</summary>
    public const string ReviewProposals = "Review proposals";

    /// <summary>Run Detail's link to a Succeeded run's Review Screen (EXPERIENCE.md, Information Architecture).</summary>
    public const string Review = "Review";

    /// <summary>
    /// Between the items of the Review Screen's meta line: Meeting, start, provider, model, Prompt
    /// Version. U+00B7 MIDDLE DOT, as the mockup writes it.
    /// </summary>
    public const string MetaSeparator = " \u00B7 ";

    /// <summary>DESIGN.md, Provenance chip — the AI variant's text, always exactly this.</summary>
    public const string ProposedByAi = "Proposed by AI";

    /// <summary>
    /// DESIGN.md, Provenance chip — the human variant, completed by the decider's display name. The
    /// timestamp sits beside the chip, never inside it.
    /// </summary>
    public const string DecidedByPrefix = "Decided by ";

    /// <summary>A proposal card's owner label.</summary>
    public const string Owner = "Owner";

    /// <summary>A proposal card's due-date label.</summary>
    public const string DueDate = "Due date";

    /// <summary>EXPERIENCE.md, Proposal card — the owner when nobody was matched or assigned.</summary>
    public const string Unassigned = "Unassigned";

    /// <summary>
    /// EXPERIENCE.md, Proposal card — the always-visible owner hint, completed by the suggested
    /// owner's free text, or by <see cref="AiSuggestedNone"/> when the notes named nobody.
    /// </summary>
    public const string AiSuggestedPrefix = "AI suggested: ";

    /// <summary>What completes <see cref="AiSuggestedPrefix"/> when the suggested owner is empty.</summary>
    public const string AiSuggestedNone = "none";

    /// <summary>EXPERIENCE.md, Proposal card — a Pending proposal with no suggested due date.</summary>
    public const string NoDueDateProposed = "No due date proposed";

    /// <summary>An approved or edited card whose Tracked Action has no due date.</summary>
    public const string NoDueDate = "No due date";

    /// <summary>EXPERIENCE.md, decided card — the heading of an Edited card's original-values column.</summary>
    public const string Proposed = "Proposed";

    /// <summary>EXPERIENCE.md, decided card — a Rejected card's reason, completed by the reason's text.</summary>
    public const string ReasonPrefix = "Reason: ";

    /// <summary>EXPERIENCE.md, decided card — a Rejected card whose rejection gave no reason.</summary>
    public const string NoReasonGiven = "No reason given";

    /// <summary>EXPERIENCE.md, decided card — the link from an Approved or Edited card to its Tracked Action.</summary>
    public const string ViewAction = "View action";

    /// <summary>EXPERIENCE.md, Pending counter — the link to the Action List filtered to this Meeting.</summary>
    public const string ViewActions = "View actions";

    /// <summary>DESIGN.md, Pending counter — "{n} proposals pending", completing the count.</summary>
    public const string ProposalsPendingSuffix = " proposals pending";

    /// <summary>The Pending counter at exactly one, where "1 proposals pending" would be wrong.</summary>
    public const string OneProposalPending = "1 proposal pending";

    /// <summary>DESIGN.md, Pending counter — the counter at zero.</summary>
    public const string AllProposalsDecided = "All proposals decided";

    /// <summary>A Pending card's filled button. EXPERIENCE.md: the verbs are exactly Approve, Edit, Reject.</summary>
    public const string Approve = "Approve";

    /// <summary>A Pending card's outlined button.</summary>
    public const string Edit = "Edit";

    /// <summary>A Pending card's text button, and the Reject dialog's confirm.</summary>
    public const string Reject = "Reject";

    /// <summary>
    /// EXPERIENCE.md, Proposal card — the edit-mode primary while at least one field differs from
    /// the proposed value. It reads <see cref="Approve"/> again when nothing differs.
    /// </summary>
    public const string ApproveWithEdits = "Approve with edits";

    /// <summary>The Reject dialog's title.</summary>
    public const string RejectDialogTitle = "Reject this proposal?";

    /// <summary>The Reject dialog's reason field label. The reason is never required.</summary>
    public const string RejectReasonLabel = "Reason (optional)";
}
