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
}
