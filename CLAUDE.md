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
5. **`Models/ArtifactParser`** splits the response on `FILE:` marker lines into `GeneratedArtifact`s.
6. **`Services/SolutionWriter`** writes them into the linked solution.

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
| `CanWriteGherkin` (Reqnroll detected) | a `.feature` file **and** a step-definitions file |
| `IsUsable` (a test base class found) | one `[TestFixture]` class in that solution's house style |
| neither | a self-contained test that owns its browser |

For Gherkin, existing step bindings are listed verbatim in the prompt and reuse is the first rule —
a BDD suite decays fast when every generated scenario invents a near-duplicate step.

**`IsUsable` and `CanWriteGherkin` are independent, not a fallback chain** — a solution can be
`CanWriteGherkin: true` and `IsUsable: false` at the same time. `IsUsable` only means "a plain
`[TestFixture]` example test was found"; a solution that is purely Reqnroll, with no hand-written
test fixture at all (normal, since Reqnroll generates its own runner from `.feature` files), will
never set it. Anything that gates on "is there a linked solution ready to receive output" — Insert,
the Folder button — must check `SolutionProfile.CanInsert` (`CanWriteGherkin || IsUsable`), not
`IsUsable` alone. Gating on `IsUsable` alone was a real bug: the Feature/Page Object/Steps pickers
correctly showed "New feature file" etc. (gated on `CanWriteGherkin`), while Insert stayed
permanently disabled for exactly that kind of solution.

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
- When editing with bulk find-and-replace, **verify the replacement actually matched**. Several bugs
  in this repo's history came from a silent no-match leaving old code in place while surrounding
  edits landed.

## Repository notes

`.github/copilot-instructions.md` contains only Azure tooling rules that are unrelated to this
project and can be ignored.
