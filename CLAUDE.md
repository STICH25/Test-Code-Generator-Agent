# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
dotnet build                                    # build (treat any warning as a regression: this repo builds clean)
dotnet run                                      # build and launch the WinForms app
```

`dotnet run` holds the console until the window closes. To leave the app running while you keep
working, launch the built binary instead:

```bash
powershell -c "Start-Process bin\Debug\net9.0-windows\PlaywrightAgentAI.exe -WorkingDirectory bin\Debug\net9.0-windows"
powershell -c "Stop-Process -Name PlaywrightAgentAI -Force"
```

**The binary is locked while the app runs** — `dotnet build` fails with MSB3026 if you forget to stop
it first.

There is no test project here. Verification happens against the linked test solution (see below):
build it, then `dotnet test --filter "FullyQualifiedName~SomeName"` for a single test or scenario.

## Architecture

A WinForms desktop app that turns a URL plus either a typed objective or a recorded click-through
into Playwright test code, written directly into a separate test-automation solution.

### The pipeline

`MainForm` → `ExplorationAgent` → the generated files:

1. **`Tools/DomExplorer`** loads the page in headless Playwright and returns raw HTML.
2. **`Services/HtmlAnalyzer`** parses it with AngleSharp into a `DomSnapshot`. It scores headings by
   how many meaningful words they share with the objective to find the relevant section. This matters
   more than it looks: an earlier version searched for the whole objective sentence as a literal
   substring, matched nothing, and fell back to dumping every section on the page.
3. **`Services/PromptBuilder`** assembles the prompt. **Order is load-bearing** — the output contract
   goes *before* the page markup. With it placed after, the model reads past a large DOM dump and
   ignores the contract entirely.
4. **`ITestCodeGenerator`** (in `ClaudeCodeGenerator.cs`) is the provider seam.
5. **`Models/ArtifactParser`** splits the response on `FILE:` marker lines into `GeneratedArtifact`s
   (`Feature`, `PageObject`, `StepDefinitions`, or a plain `Test`).
6. **`Services/SpecForgeReuseCheck`** (optional, see below) asks SpecForge which of the new steps
   reuse existing bindings. Advisory only.
7. **`Services/SolutionWriter`** writes the artifacts into the linked solution — but only when the
   user presses **Insert**. Nothing is written during generation; the three tabs (Feature / Page
   Object / Steps) are a preview that Edit can change first.

### Two Claude providers

`AppSettings.Provider` selects between them, and both implement `ITestCodeGenerator`:

- **`ClaudeCliCodeGenerator`** (default) shells out to the local `claude` CLI. This exists because
  organisations often supply a Claude Code seat but no API key. The prompt goes in on **stdin**, not
  argv — it embeds page markup and would exceed the Windows command-line limit. Note that
  `CreateProcess` cannot execute a `.cmd` directly, and npm's global shim is `claude.cmd`, so the
  generator routes through `cmd.exe /c`.
- **`ClaudeCodeGenerator`** uses the Anthropic SDK with an API key. It **streams**: with thinking
  enabled the first content block is a thinking block, so reading `content[0]` returns the wrong
  block. Streaming yields only text deltas and sidesteps that.

Every run writes the exact prompt to `%TEMP%\PlaywrightAgentAI.cli\last-prompt.txt`. When output
looks wrong, **read that file before theorising** — a bug once had the agent logging "house style
applied" while passing no profile to the builder.

### Read the linked solution before changing generation

This tool's output is only correct relative to a *specific* target solution, so before
touching `PromptBuilder`, `SolutionScanner`, `SolutionWriter` or anything else that shapes
generated code, **open the solution the user has linked and read it.** Find it in
`%APPDATA%\PlaywrightAgentAI\settings.json` under `TestSolutionPath`, or ask.

What to look at, and why it changes your work:

- **`.feature` files and `StepDefinitions/`** — if these exist the tool must emit Gherkin,
  not an NUnit fixture, and must reuse the step wordings already bound there.
- **The test base class** — generated tests inherit it, so its members define what the
  generated code is allowed to call.
- **An existing test** — the house style the output has to match.

Reasoning about generation from this repo alone produces output that compiles here in
theory and fails in the user's solution in practice. The `reqnroll-step-reuse` skill
automates the inventory step; use it rather than grepping by hand.

### Output shape depends on the linked solution

`SolutionScanner` reads the target solution and produces a `SolutionProfile`. Three contracts, chosen
in `PromptBuilder.Build`:

| Condition | Output |
|---|---|
| `CanWriteGherkin` (Reqnroll detected) | a `.feature` file, a **page object**, and a step-definitions file |
| `IsUsable` (a test base class found) | one `[TestFixture]` class in that solution's house style |
| neither | a self-contained test that owns its browser |

For Gherkin, existing step bindings are listed verbatim in the prompt and reuse is the first rule —
a BDD suite decays fast when every generated scenario invents a near-duplicate step.

**The three files have strict jobs.** The page object holds every locator and every action/query
method for a page; the step definitions hold only assertions (they call page-object queries and
`Assert.That`, never a selector); the feature holds the scenario. A page-object query waits for its
value to settle before returning it. This split is the `new-automation-project` skill's
`DynabyteHomePage`/`DynabyteHomeSteps` templates, copied into `PromptBuilder`'s contract text. The
three pickers in the input card (Feature file / Page object / Step definitions) either pick an
existing file to extend — the prompt then carries that file's full content and the writer overwrites
exactly that path, with a `.bak` — or leave "New ..." to create one. A missing `Pages` folder is
created on Insert.

**`IsUsable` and `CanWriteGherkin` are independent, not a fallback chain** — a solution can be
`CanWriteGherkin: true` and `IsUsable: false` at the same time. `IsUsable` only means "a plain
`[TestFixture]` example test was found"; a solution that is purely Reqnroll, with no hand-written
test fixture at all (normal, since Reqnroll generates its own runner from `.feature` files), will
never set it. Anything that gates on "is there a linked solution ready to receive output" — Insert,
the Folder button — must check `SolutionProfile.CanInsert` (`CanWriteGherkin || IsUsable`), not
`IsUsable` alone. Gating on `IsUsable` alone was a real bug: the Feature/Page Object/Steps pickers
correctly showed "New feature file" etc. (gated on `CanWriteGherkin`), while Insert stayed
permanently disabled for exactly that kind of solution.

### Skills, and the optional SpecForge reuse check

**The app does not invoke any Claude skill, and does not depend on one.** Generation is a single
`claude --print --max-turns 1` call, which would never load a skill anyway. The skills in
`~/.claude/skills` (`new-automation-project`, `reqnroll-step-reuse`, `specforge-reqnroll`, ...) are
for Claude Code sessions working *on* a test solution. Where one informed the app, its idea was
copied into code (the page-object/steps split into `PromptBuilder`; the step inventory into
`SolutionScanner`), so a later change to a skill does not change the app's behaviour — re-sync by
hand if you want it to.

The one external tool the app *can* use is **SpecForge** (`~/source/repos/SpecForge`, a dotnet tool),
and only as a second opinion after generation. `SpecForgeLocator` finds `specforge.exe` on PATH or
the `.dotnet/tools` folder, or an explicit **Settings → Step Reuse Check** path (a `specforge.exe`, or
`SpecForge.Cli.dll` from a source build, run via `dotnet`). `SpecForgeReuseCheck` then parses the
generated `.feature` into SpecForge's `scenarios.json` shape, runs `specforge scan` over the linked
solution and `specforge match`, and reports "N of M steps reuse existing bindings" in the status bar
plus the unmatched steps in the Log. When appending to an existing feature file only the *new*
scenarios are checked, otherwise the file's old steps would inflate the tally.

Rules for anything built on this: **it is advisory and never required.** Absent SpecForge means a
skipped check and one log line; a SpecForge that errors, hangs (60s timeout) or returns garbage is
logged and swallowed — it must not reach the surrounding AI-call `catch`, which would replace a good
generation with the static template. The in-app `SolutionScanner` still shapes the prompt; SpecForge
does not replace it. Everything runs locally; scratch files are kept in
`%TEMP%\PlaywrightAgentAI.specforge` (`scenarios.json`, `inventory.json`, `match.json`) — read those
before theorising about a surprising tally. SpecForge targets net8.0; the machine needs that runtime.

### Writing into another repo

`SolutionWriter` backs up any file it is about to overwrite to a timestamped `.bak`, and refuses to
write if the backup fails. The linked solution may not be under version control. Feature files are
overwritten by design (that is how a scenario is appended); **step-definition files are never
overwritten** — the model emits only *new* bindings, so overwriting would silently delete existing
ones. The writer auto-suffixes instead.

### Recording

`Tools/PreviewRecorder` injects `Tools/RecorderScript` into the WebView2 preview via
`AddScriptToExecuteOnDocumentCreatedAsync` and receives actions over `WebMessageReceived`. Listeners
are registered in the **capture phase** so a site calling `stopPropagation` cannot hide interactions.
Each action reports several ways to address the element (test id, role + accessible name, text, CSS
path) and the model picks the most durable. Password fields record as `<redacted>`.

### Debug screenshots

Optional, enabled by **Settings → Debug Screenshots** (a local folder; blank disables it). Two paths,
both writing numbered PNGs there and listing them in the **Screenshots** viewer in order:

- **Recording:** `PreviewRecorder` captures one per recorded action via WebView2's
  `CapturePreviewAsync` (navigation shots wait for load; click shots are taken as recorded).
- **Typed objective:** `ExplorationAgent` screenshots the section the objective matched, from the
  same headless Playwright page that read the DOM (`DomExplorer.Capture` / `DomCapture` keep it open
  past the HTML read). The section is re-found **by its heading's text**, not by
  `DomSnapshot.SectionSelectors` — an unclassed container's selector is just a tag name like
  `section`, which matches every sibling and silently screenshots the first one. And when the
  objective *quotes* a section name, `HtmlAnalyzer` matches the quote against headings before any
  keyword scoring, because generic words ("device", "value", "online") otherwise let a short
  unrelated heading beat the real one.

Screenshots are debug-only and are deleted when a new recording starts, on Clear, and after Insert.
They are never written into the linked solution.

## UI conventions

Everything is hand-built in code; the designer file only carries form-level scaling. `UI/Theme.cs`
holds the palette and caches fonts — WinForms does not take ownership of an assigned `Font`, so
handing out fresh instances leaks.

Stock controls cannot be themed for a dark UI, hence the custom-painted set in `UI/`. Two traps worth
knowing:

- **`ComboBox` is a native control.** Overriding `OnPaint` does not stop the OS drawing its face and
  drop button. `DarkComboBox` overpaints after the base `WM_PAINT`.
- **`SplitContainer` abandons its layout pass** when `Panel1MinSize + Panel2MinSize + SplitterWidth`
  exceeds its own extent, leaving both panels at stale bounds — including a stale *width* for a
  nested splitter, so cards inside stop reflowing. `MainForm.ConfigureSplit` re-evaluates the
  minimums on every resize for this reason. If you add rows to the input card, raise its
  `minPanel1` to match or the objective field collapses to a sliver.

- **A `SplitContainer`'s `SplitterDistance` setter validates against its *current* width**, which is
  still the 150px default before Dock=Fill layout has run, so setting it once at construction throws
  "SplitterDistance must be between Panel1MinSize and Width - Panel2MinSize". Use
  `UI/SplitLayout.Configure` for any new splitter: it relaxes the minimums, clamps, and re-applies on
  every resize (the same logic as `MainForm.ConfigureSplit`). A `try/catch` around `ShowDialog` does
  **not** catch this — the exception is raised in the dialog's own message loop and surfaces as
  WinForms' "Unhandled exception" dialog.
- **`SegmentedTabs` shrinks its chip padding to fit** (28px down to 10px). With fixed padding the last
  tab, Log, fell off the end of the narrow output card — unreachable, and invisible when the app
  switched to it.
- **The Settings dialog scrolls.** Its sections stack taller than a sensible window (more so with the
  API-key fields shown), and squeezing rows made them overlap. The table lives in a scrolling host
  with Save/Cancel pinned below it. When adding a section, add rows to the table and its
  `RowStyles`; `UpdateScrollExtent` sums them (row 14, the solution notes, is the one flexible row).
  Check both providers — the CLI layout hides rows the API layout shows.
- **`PillButton` draws its focus ring only when the style has no border of its own.** On an Outline
  button (Settings regains focus when its dialog closes) the ring sat beside the border and read as a
  double border.

Title bar and scrollbars are darkened through `UI/NativeDark` (undocumented `uxtheme` ordinals, all
best-effort). Resizability is a hard requirement from the user: dock or anchor everything, never
hardcode control widths.

## Secrets

The API key is encrypted with Windows DPAPI (`Services/SecretStore`) and stored under
`%APPDATA%\PlaywrightAgentAI\settings.json`. An earlier design kept it in a git-tracked
`appsettings.json`; that file has been removed and should not come back.

## Gotchas

- The repo's source files use **LF** endings, so verbatim string literals emit `\n`. A multi-line
  WinForms `TextBox` only breaks on CRLF — call `ReplaceLineEndings()` before display or before
  writing a file, or the whole test collapses onto one line.
- When the Claude CLI reports an expired login, the app opens a real `cmd` window running `claude`
  (`ClaudeCliCodeGenerator.OpenSignInTerminal`) so the user signs in themselves. The app never stores
  or handles Claude credentials; do not add a way to log in on their behalf.
- When driving the app for verification, a bad JSON path in `%APPDATA%\PlaywrightAgentAI\settings.json`
  fails silently: `SettingsStore.Load` swallows the parse error and returns defaults, so the app
  appears to have "no solution linked". Backslashes must be doubled (or use forward slashes).
- When editing with bulk find-and-replace, **verify the replacement actually matched**. Several bugs
  in this repo's history came from a silent no-match leaving old code in place while surrounding
  edits landed.

## Repository notes

`.github/copilot-instructions.md` contains only Azure tooling rules that are unrelated to this
project and can be ignored.
