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

Then press **Test Connection**. Each generation consumes your normal Claude Code usage.

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
2. Pick a **Feature file** — an existing one to append a scenario to, or *New feature file*.
3. Describe the **Test Objective**, e.g. *"Verify the Skills section lists at least one skill."*
4. **Generate Test**.

### From your own clicks

1. Enter the **URL** and leave the objective empty.
2. Press **Record**. The preview pane reloads and starts capturing.
3. Perform the scenario in the preview — click, type, select, navigate.
4. Press **Done**. Recording stops and generation starts immediately from what you did.

The recorded steps become the specification, so no written objective is required. Values typed into
password fields are recorded as `<redacted>`.

### What happens next

Generated files are written into your solution automatically:

| Suite type | Files produced |
|---|---|
| Reqnroll / Gherkin | `Features/<Name>.feature` and `StepDefinitions/<Name>Steps.cs` |
| NUnit | one `[TestFixture]` class in your test folder |

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
- **Step-definition files are never overwritten.** Generation emits only genuinely new bindings, so
  overwriting would delete the ones already there. New bindings go into a new file beside them.
- Existing step wordings are given to Claude and reuse is required wherever one fits, which keeps a
  BDD suite from filling up with near-duplicate steps.

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

Generation typically takes 30–60 seconds on Opus. Switching to Sonnet in Settings is noticeably
faster and cheaper, and is usually enough for straightforward scenarios.
