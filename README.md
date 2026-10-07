# Playwright Test Generator

A Windows desktop tool that turns a web page into Playwright test code and writes it straight into
your test-automation solution.

Point it at a URL, then either describe what you want verified or click through the scenario
yourself. It captures the page, asks Claude for the test, and saves the result into your solution in
that solution's own style — Gherkin feature files and step definitions for a Reqnroll suite, or an
NUnit fixture for a plain one.

---

## Requirements

- **Windows** with the WebView2 runtime (present on Windows 11 by default)
- **.NET 9 SDK**
- **Playwright browsers** — installed automatically on first use, or run `playwright install chromium`
- **Access to Claude**, by either route below

## Connecting to Claude

Open **Settings** and pick one:

**Claude Code CLI** *(default)* — uses the Claude Code login you already have, so no API key is
needed. This is the route to use if your organisation provides Claude Code seats but not API access.
Install the CLI once:

```bash
npm install -g @anthropic-ai/claude-code
```

Each generation consumes your normal Claude Code usage. The app never sees or stores your Claude
credentials.

**The connection is checked every time the app starts.** Until it passes, every button except
**Settings** is disabled, so an expired session shows up straight away rather than after you have
recorded a whole scenario. It takes a few seconds and needs nothing from you when your login is good.
If the login has expired, a terminal running `claude` opens for you to sign in; then open **Settings**
and press **Test Connection** (or just close Settings, which checks again) and the window unlocks. If
it fails for another reason, the status bar says why.

**Anthropic API key** — paste a key from the Anthropic Console. It is encrypted with Windows DPAPI
for your user account and stored in `%APPDATA%\PlaywrightAgentAI\settings.json`; it never touches the
repository. The model list is fetched live from your account, so you only ever see models you can
actually use.

## Linking your test solution

In **Settings**, set **Test Solution** to the root folder of your automation project. The tool reads
it to learn your conventions and reports what it found:

- the base class your tests derive from, and the namespace they live in
- helper classes available to tests
- whether Reqnroll is in use, plus every `.feature` file and existing step binding

Everything it generates is shaped to match. Nothing is sent anywhere except to Claude through the
provider you chose.

---

## Using it

### From a description

1. Enter the **URL**.
2. Choose where the output goes with the three pickers: **Feature file**, **Page object** and
   **Step definitions**. Pick an existing file to extend it, or leave *New ...* to create one.
3. Describe the **Test Objective**, e.g. *"Verify the Skills section lists at least one skill."*
   If you name the section in quotes — *Check the "Connected Devices" section* — that name is matched
   to the page's headings directly, which is more reliable than relying on the surrounding words.
4. **Generate Test**.

### From your own clicks

1. Enter the **URL** and leave the objective empty.
2. Press **Record**. The preview pane reloads and starts capturing.
3. Perform the scenario in the preview — click, type, select, navigate.
4. Press **Done**. Recording stops and generation starts immediately from what you did.

The recorded steps become the specification, so no written objective is required. Values typed into
password fields are recorded as `<redacted>`.

**Refresh** (top right of the preview, or **F5**) reloads the page you are on, wherever you have
clicked through to, so a session that expired while you were idle can be renewed without losing your
place. It works during a recording too: reloading the same page is not recorded as a step, though
being sent back to a login page is, since that is what happened. The **Preview** button next to the
URL is different - it always goes back to the address typed there.

### What happens next

The result appears in tabs for you to review. Nothing is written to your solution until you press
**Insert**, and **Edit** opens the active tab's code in its own window if you want to change it first.

**Clear** empties all three generated files (and the debug screenshots), whichever tab is showing, after asking you to confirm; answer *No* and the session carries on untouched. The Log tab is separate: Clear there only empties the log. After **Insert** you are asked whether to clear the generated code too, so the next test starts from a clean slate; answer *No* to keep it on screen.

| Suite type | Files produced |
|---|---|
| Reqnroll / Gherkin | `Features/<Name>.feature`, a page object (`Pages/<Name>Page.cs`) and `StepDefinitions/<Name>Steps.cs` |
| NUnit | one `[TestFixture]` class in your test folder |

For a Gherkin suite the three files have separate jobs: the **page object** holds every locator and
the methods that act on or read the page, the **step definitions** hold only the assertions, and the
**feature** holds the scenario. A `Pages` folder is created if your solution doesn't have one yet.

**Open Folder** reveals where they landed. Then build and run them with your normal tooling:

```bash
dotnet test --filter "FullyQualifiedName~YourScenarioName"
```

---

## How it protects your code

Your test solution may not be under version control, so writes are guarded:

- Any file about to be overwritten is first copied to a timestamped `.bak`. If the backup cannot be
  made, the write is refused.
- Feature files are rewritten in full — that is how a new scenario is appended, and existing
  scenarios are preserved.
- **Step-definition files are never overwritten by accident.** Generation emits only genuinely new
  bindings, so a same-named collision gets a numbered new file beside the old one. The one exception
  is a file you explicitly pick in the *Step definitions* list: it is reproduced in full with the new
  bindings added, after a `.bak` backup.
- Existing step wordings are given to Claude and reuse is required wherever one fits, which keeps a
  BDD suite from filling up with near-duplicate steps.

---

## Optional extras

**Debug screenshots** — set a folder under *Settings → Debug Screenshots*. While recording, every
click is captured; for a typed objective, the section that was matched is captured, so you can see
whether the right part of the page was tested. The **Screenshots** button opens them in order. They
live only in that folder, are never written into your solution, and are deleted when you start a new
recording, or when you clear the generated code (**Clear**, or answering *Yes* to the prompt after **Insert**).

**Azure DevOps PBIs** — name a PBI in the objective, e.g. *"Check PBI 4242 and write the tests"* (or
paste its work-item link), and the PBI and its linked Test Cases are read from Azure DevOps so the
scenarios follow the real test steps rather than just the acceptance criteria. The result is marked in
the status bar ("Built from PBI 4242 (2 linked test case(s))") and each scenario carries a
`# ADO Test Case <id>` comment, unless your solution already puts IDs in its scenario titles, in which
case that format is followed. The reading is done by Claude using your own `gherkin-to-ado-testcases`
and `specforge-reqnroll` skills (the Log says which were consulted), so it reaches Azure DevOps the same
way those skills already do for you, and it runs from your linked solution's folder so that repo's
`CLAUDE.md` is found. This needs the Claude Code CLI provider and the Azure CLI (`az`, with
its `azure-devops` extension, signed in via `az login`); set a default organization and project under
*Settings → Azure DevOps* if you refer to PBIs by number only. It is **read-only** — nothing is created
or changed in Azure DevOps — and no Azure DevOps credential is stored by this app. If the lookup fails
for any reason, the Log says why and generation continues from your objective as usual.

**Step reuse check** — if SpecForge (the dotnet tool that converts test cases to Gherkin and matches
steps against existing bindings) is installed on `PATH`, or you point *Settings → Step Reuse Check* at
it, each generation is followed by a check of which new steps reuse bindings that already exist, e.g.
*"3 of 5 steps reuse existing bindings"*, with the new ones listed in the Log. It is purely advisory:
without SpecForge, or if it fails, generation is unaffected.

---

## When something looks wrong

The **Log** tab narrates every stage: page capture, which section was matched to your wording,
whether your solution's style was applied, and the call to Claude.

The exact prompt from the last run is saved to:

```
%TEMP%\PlaywrightAgentAI.cli\last-prompt.txt
```

Reading that file answers most "why did it generate *that*?" questions faster than anything else.

**Common issues**

| Symptom | Cause |
|---|---|
| *Claude Code CLI not found* | Install it with npm (above), then re-test the connection |
| Generation times out | The target site was unreachable; the log shows the navigation error |
| Output ignores your conventions | The solution link is missing or the folder has no recognisable tests |
| Web preview unavailable | The WebView2 runtime is not installed |

Generation typically takes 30–60 seconds. **Sonnet is the default**: it is faster and cheaper and is
usually enough. Switch to Opus in Settings for a hard scenario, at the cost of time and usage. A model
you have already chosen is kept; the default only applies to a new settings file.
