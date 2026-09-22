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
    /// EXPERIENCE.md, Meeting table — the tracked-action count column. Zero until Story 3.1. The
    /// Glossary spells it "Tracked Action", so the column keeps both capitals.
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
    /// attended. An ASCII hyphen rather than an em dash: this vocabulary is ASCII plus U+2019.
    /// </summary>
    public const string NoValue = "-";
}
