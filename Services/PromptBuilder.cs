using System.Text;
using PlaywrightAgentAI.Models;

namespace PlaywrightAgentAI.Services;

/// <summary>
/// Builds the generation prompt.
///
/// When a test solution is linked, the prompt asks for a test in that solution's house
/// style - deriving from its base class, using its helpers, in its namespace - with a real
/// existing test supplied as the example to imitate. Without a linked solution it falls
/// back to a self-contained test.
///
/// The previous prompt asked for code that could not work: it told the model to create its
/// own browser and page but forbade calling GotoAsync, so the generated test asserted
/// against a blank page. It also hardcoded selectors from one specific site while telling
/// the model not to invent selectors.
/// </summary>
public class PromptBuilder
{
    private const int MaxSections = 6;
    private const int MaxItemsPerList = 40;

    public string Build(
        UserPromptRequest request,
        DomSnapshot dom,
        SolutionProfile? profile = null,
        IReadOnlyList<RecordedAction>? recordedActions = null,
        FeatureFile? targetFeature = null,
        PageObjectFile? targetPageObject = null,
        StepDefinitionFile? targetStepDefinitions = null)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("Write a Playwright test in C# for the page described below.");
        prompt.AppendLine();
        prompt.AppendLine($"TARGET URL: {request.Url}");
        prompt.AppendLine();

        if (!string.IsNullOrWhiteSpace(request.Action))
        {
            prompt.AppendLine("TEST OBJECTIVE:");
            prompt.AppendLine(request.Action);
            prompt.AppendLine();
        }

        AppendRecordedSteps(prompt, recordedActions);

        // Order matters. The contract goes before the page markup: with it placed after,
        // the model read past a large DOM dump and produced a standalone test that
        // ignored the linked solution's base class and namespace entirely.
        if (profile is { CanWriteGherkin: true })
            AppendGherkinContract(prompt, profile, targetFeature, targetPageObject, targetStepDefinitions);
        else if (profile is { IsUsable: true })
            AppendHouseStyle(prompt, profile);
        else
            AppendStandaloneContract(prompt, request.Url);

        AppendPageFacts(prompt, dom);

        AppendRules(prompt, profile);

        return prompt.ToString();
    }

    /// <summary>
    /// The recorded steps are the specification when they exist: they are what the user
    /// actually did, so the test must reproduce that sequence rather than improvise.
    /// Each step lists several ways to address the element and the model picks the most
    /// durable one.
    /// </summary>
    private static void AppendRecordedSteps(StringBuilder prompt, IReadOnlyList<RecordedAction>? actions)
    {
        if (actions == null || actions.Count == 0)
            return;

        prompt.AppendLine("=== RECORDED USER STEPS (REPRODUCE THESE IN ORDER) ===");
        prompt.AppendLine("These were captured while the user performed the scenario in a real browser.");
        prompt.AppendLine("Write one Playwright call per step, in this order, then assert the end state.");
        prompt.AppendLine();

        for (var i = 0; i < actions.Count; i++)
            prompt.AppendLine($"{i + 1}. {actions[i].Describe()}");

        prompt.AppendLine();
        prompt.AppendLine("For each step choose the most durable locator available: a data-testid beats a");
        prompt.AppendLine("role plus accessible name, which beats visible text, which beats the CSS path.");
        prompt.AppendLine("A value shown as <redacted> was a password field - use a placeholder or config value.");
        prompt.AppendLine();
    }

    private static void AppendPageFacts(StringBuilder prompt, DomSnapshot dom)
    {
        prompt.AppendLine("=== ACTUAL PAGE STRUCTURE ===");
        prompt.AppendLine("Every selector you write must come from the markup below. Do not invent one.");
        prompt.AppendLine();

        AppendList(prompt, "Headings", dom.Headings);
        AppendList(prompt, "Buttons", dom.Buttons);
        AppendList(prompt, "Links", dom.Links);
        AppendList(prompt, "Input names/ids", dom.Inputs);

        // Cap the section markup. Every heading on a page used to become its own section
        // at up to 5000 characters each, with the same container captured more than once,
        // which made the prompt enormous and mostly duplicated.
        var sections = dom.SectionHtml.Take(MaxSections).ToList();
        if (sections.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine($"Section markup ({sections.Count} of {dom.SectionHtml.Count}):");

            foreach (var (name, html) in sections)
            {
                prompt.AppendLine();
                prompt.AppendLine($"--- section: {name} ---");

                if (dom.SectionSelectors.TryGetValue(name, out var selector))
                    prompt.AppendLine($"container selector: {selector}");

                if (dom.SectionItems.TryGetValue(name, out var items) && items.Count > 0)
                    prompt.AppendLine($"list items: {string.Join(" | ", items.Take(MaxItemsPerList))}");

                prompt.AppendLine(html);
            }
        }

        prompt.AppendLine();
    }

    private static void AppendList(StringBuilder prompt, string label, List<string> values)
    {
        var distinct = values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxItemsPerList)
            .ToList();

        if (distinct.Count > 0)
            prompt.AppendLine($"- {label}: {string.Join(", ", distinct)}");
    }

    /// <summary>
    /// Asks for a .feature file plus its step bindings.
    ///
    /// The existing step patterns are listed verbatim and reuse is made the first rule: a
    /// BDD suite decays quickly when every generated scenario invents a near-duplicate of
    /// a step that already exists.
    /// </summary>
    private static void AppendGherkinContract(
        StringBuilder prompt,
        SolutionProfile profile,
        FeatureFile? targetFeature,
        PageObjectFile? targetPageObject,
        StepDefinitionFile? targetStepDefinitions)
    {
        prompt.AppendLine("=== OUTPUT CONTRACT: GHERKIN (MANDATORY) ===");
        prompt.AppendLine("This is a Reqnroll BDD suite. Produce Gherkin plus C# bindings - never a bare NUnit test.");
        prompt.AppendLine();
        prompt.AppendLine("Return exactly three files, each introduced by its own marker line on its own line:");
        prompt.AppendLine();
        prompt.AppendLine("FILE: <Name>.feature");
        prompt.AppendLine("<the complete Gherkin>");
        prompt.AppendLine("FILE: <Name>Page.cs");
        prompt.AppendLine("<the complete page object>");
        prompt.AppendLine("FILE: <Name>Steps.cs");
        prompt.AppendLine("<the complete step definition class>");
        prompt.AppendLine();
        prompt.AppendLine("No prose before, between or after the files. No markdown fences.");
        prompt.AppendLine();
        prompt.AppendLine("=== HOW THE THREE FILES DIVIDE THE WORK ===");
        prompt.AppendLine("This split is the point of the exercise, not a formatting preference. A selector");
        prompt.AppendLine("written inline in a step can never be reused and has to be re-found by hand every");
        prompt.AppendLine("time the page changes; the same selector on a page object is fixed in one place.");
        prompt.AppendLine();
        prompt.AppendLine("PAGE OBJECT - everything that knows what the page looks like:");
        prompt.AppendLine("  - Selectors as private const strings, and locators as private ILocator properties.");
        prompt.AppendLine("  - An Actions section: methods that DO something (GotoAsync, SignInAsync, ClickX).");
        prompt.AppendLine("  - A Queries section: methods that RETURN a settled value for the step to judge");
        prompt.AppendLine("    (Task<bool> IsXVisibleAsync, Task<string> GetXTextAsync, Task<int> GetXCountAsync).");
        prompt.AppendLine("  - A query must WAIT for its value to settle before returning it - a WaitForAsync,");
        prompt.AppendLine("    WaitForURLAsync or WaitForFunctionAsync - then hand the value back. A one-shot read");
        prompt.AppendLine("    with no wait reintroduces exactly the race the wait exists to close.");
        prompt.AppendLine("  - Take IPage through the constructor. NO assertions anywhere in this file.");
        prompt.AppendLine();
        prompt.AppendLine("STEP DEFINITIONS - assertions and nothing else:");
        prompt.AppendLine("  - Construct or hold the page object, call its methods, assert on what comes back.");
        prompt.AppendLine("  - NO selectors, NO Locator(...) calls, NO GetByRole/GetByText, NO waits, NO try/catch.");
        prompt.AppendLine("  - If you find yourself needing a selector here, the page object is missing a method:");
        prompt.AppendLine("    add it there and call it from here.");
        prompt.AppendLine();
        prompt.AppendLine("FEATURE - business language only, no UI mechanics.");
        prompt.AppendLine();

        if (targetFeature != null)
        {
            prompt.AppendLine($"TARGET FEATURE FILE: {targetFeature.FileName}");
            prompt.AppendLine("Reproduce this file in full with your new scenario appended to it.");
            prompt.AppendLine("Keep the existing Feature header, Background and every existing scenario unchanged.");
            prompt.AppendLine($"Name the feature file exactly: {targetFeature.FileName}");
            prompt.AppendLine();
            prompt.AppendLine($"--- current contents of {targetFeature.FileName} ---");
            prompt.AppendLine(targetFeature.Source);
            prompt.AppendLine();
        }
        else
        {
            prompt.AppendLine("No existing feature file was selected, so create a new one named after the behaviour.");
            prompt.AppendLine();
        }

        if (profile.StepBindings.Count > 0)
        {
            prompt.AppendLine("=== STEP BINDINGS THAT ALREADY EXIST - REUSE THESE ===");
            prompt.AppendLine("Write your scenario using these exact step wordings wherever one fits.");
            prompt.AppendLine("Only invent a new step when nothing here expresses what you need.");
            prompt.AppendLine();

            foreach (var binding in profile.StepBindings)
                prompt.AppendLine($"  {binding}");

            prompt.AppendLine();
            prompt.AppendLine("In the steps file, include ONLY the genuinely new step methods.");
            prompt.AppendLine("Never redefine a step listed above - Reqnroll fails at runtime on duplicate bindings.");
            prompt.AppendLine();
        }

        if (targetStepDefinitions != null)
        {
            prompt.AppendLine($"TARGET STEP DEFINITIONS FILE: {targetStepDefinitions.FileName}");
            prompt.AppendLine("Reproduce this file in full, adding only your genuinely new step method(s) to it.");
            prompt.AppendLine("Keep every existing step method, using directive and attribute exactly as it is -");
            prompt.AppendLine("do not remove, reword or reorder anything already there.");
            prompt.AppendLine($"Name the class and file exactly: {targetStepDefinitions.ClassName} / {targetStepDefinitions.FileName}");
            prompt.AppendLine();
            prompt.AppendLine($"--- current contents of {targetStepDefinitions.FileName} ---");
            prompt.AppendLine(targetStepDefinitions.Source);
            prompt.AppendLine();
        }
        else
        {
            if (profile.StepClassSource != null)
            {
                prompt.AppendLine($"--- existing binding class to imitate: {profile.StepClassName} ---");
                prompt.AppendLine(profile.StepClassSource);
                prompt.AppendLine();
            }

            prompt.AppendLine("No existing step-definitions file was selected as a target, so give the new");
            prompt.AppendLine("bindings file a NEW name that does not collide with an existing step class -");
            prompt.AppendLine("that file must hold only the new bindings, nothing already written.");
            prompt.AppendLine();
        }

        if (targetPageObject != null)
        {
            prompt.AppendLine($"TARGET PAGE OBJECT FILE: {targetPageObject.FileName}");
            prompt.AppendLine("Reproduce this file in full, adding your new action/query method(s) to it.");
            prompt.AppendLine("Keep every existing selector, locator property and method exactly as it is.");
            prompt.AppendLine($"Name the class and file exactly: {targetPageObject.ClassName} / {targetPageObject.FileName}");
            prompt.AppendLine();
            prompt.AppendLine($"--- current contents of {targetPageObject.FileName} ---");
            prompt.AppendLine(targetPageObject.Source);
            prompt.AppendLine();
        }
        else
        {
            if (profile.PageObjects.Count > 0)
            {
                prompt.AppendLine("=== PAGE OBJECTS THAT ALREADY EXIST - EXTEND, DO NOT DUPLICATE ===");
                prompt.AppendLine("If one of these covers the page under test, add your method to it and return that");
                prompt.AppendLine("whole file as your page object. Two page objects for one screen, each holding half");
                prompt.AppendLine("the selectors, is the same decay as duplicate step bindings.");
                prompt.AppendLine();

                foreach (var page in profile.PageObjects)
                {
                    var methods = page.Methods.Count > 0 ? string.Join(", ", page.Methods.Take(12)) : "(no public methods)";
                    prompt.AppendLine($"  {page.ClassName}  [{page.FileName}]");
                    prompt.AppendLine($"    methods: {methods}");
                }

                prompt.AppendLine();
            }

            if (profile.ExamplePageObject != null)
            {
                prompt.AppendLine($"--- existing page object to imitate: {profile.ExamplePageObject.ClassName} ---");
                prompt.AppendLine(profile.ExamplePageObject.Source);
                prompt.AppendLine();
            }
            else
            {
                prompt.AppendLine("No existing page object was selected as a target, so create a new one named");
                prompt.AppendLine("after the page under test.");
                prompt.AppendLine();
            }
        }

        prompt.AppendLine($"- Step definition namespace: {profile.TestNamespace}.StepDefinitions");
        prompt.AppendLine("- Mark the class [Binding].");
        prompt.AppendLine("- Take ScenarioWorld through the constructor; it exposes Page, BaseUrl and CurrentSection.");
        prompt.AppendLine("- Hooks already start and stop the browser. Do not create one.");
        prompt.AppendLine("- Assert with Microsoft.Playwright.Assertions.Expect(...).");

        if (profile.BaseUrl != null)
            prompt.AppendLine($"- The configured base URL is {profile.BaseUrl}.");

        prompt.AppendLine();
    }

    private static void AppendHouseStyle(StringBuilder prompt, SolutionProfile profile)
    {
        prompt.AppendLine("=== HOUSE STYLE (MANDATORY) ===");
        prompt.AppendLine("This test is going into an existing solution. Match its conventions exactly.");
        prompt.AppendLine();
        prompt.AppendLine($"- Namespace: {profile.TestNamespace}");

        if (profile.BaseClassName != null)
        {
            prompt.AppendLine($"- The test class must be [TestFixture] and derive from {profile.BaseClassName}.");
            prompt.AppendLine($"- {profile.BaseClassName} already handles browser setup, teardown, failure screenshots and report logging.");
            prompt.AppendLine("  Do NOT create a Playwright instance, browser, context or page. Do NOT write try/catch for reporting.");
            prompt.AppendLine("  Do NOT add [SetUp] or [TearDown].");
        }

        if (profile.BaseUrl != null)
            prompt.AppendLine($"- The configured base URL is {profile.BaseUrl}; navigate with the base class helper, not a hardcoded address.");

        if (profile.Helpers.Count > 0)
            prompt.AppendLine($"- Helpers available: {string.Join(", ", profile.Helpers)}");

        if (profile.BaseClassSource != null)
        {
            prompt.AppendLine();
            prompt.AppendLine($"--- {profile.BaseClassName}.cs (the members you may call) ---");
            prompt.AppendLine(profile.BaseClassSource);
        }

        if (profile.ExampleTestSource != null)
        {
            prompt.AppendLine();
            prompt.AppendLine($"--- existing test to imitate: {profile.ExampleTestName} ---");
            prompt.AppendLine(profile.ExampleTestSource);
        }

        prompt.AppendLine();
    }

    private static void AppendStandaloneContract(StringBuilder prompt, string url)
    {
        prompt.AppendLine("=== OUTPUT CONTRACT ===");
        prompt.AppendLine("No test solution is linked, so produce a self-contained runnable test:");
        prompt.AppendLine("- a class with a public async Task Run() method");
        prompt.AppendLine("- create the Playwright instance, browser and page inside Run()");
        prompt.AppendLine($"- navigate with await page.GotoAsync(\"{url}\") as the first action");
        prompt.AppendLine("- close the browser at the end");
        prompt.AppendLine();
    }

    private static void AppendRules(StringBuilder prompt, SolutionProfile? profile)
    {
        prompt.AppendLine("=== RULES ===");
        prompt.AppendLine("1. Use only selectors that appear in the page structure above.");
        prompt.AppendLine("2. Prefer GetByRole, GetByText and GetByLabel over brittle CSS paths.");
        prompt.AppendLine("3. Assert something meaningful - visibility, text, count.");
        prompt.AppendLine("4. Name things after the behaviour they describe.");
        prompt.AppendLine("5. Include the using directives each file needs.");

        if (profile is { CanWriteGherkin: true })
        {
            prompt.AppendLine();
            prompt.AppendLine("=== THIS IS THE PART MOST OFTEN GOT WRONG - CHECK IT ===");
            prompt.AppendLine("- Output THREE files, each preceded by its own 'FILE: <name>' marker line:");
            prompt.AppendLine("  the .feature, then <Name>Page.cs, then <Name>Steps.cs.");
            prompt.AppendLine("- The page object holds every selector. The step file holds none - if a selector");
            prompt.AppendLine("  appears in the step file, the split has failed and the output is wrong.");
            prompt.AppendLine("- Do NOT emit a [TestFixture] or a [Test] method. This suite is Gherkin-driven.");
            prompt.AppendLine("- Reuse the existing step wordings listed above; do not redefine them.");
            prompt.AppendLine("- Scenario steps read as behaviour, not as UI mechanics: prefer");
            prompt.AppendLine("  'When I sign in as a standard user' over 'When I click #login-btn'.");
        }
        else if (profile is { IsUsable: true })
        {
            prompt.AppendLine("6. Output one complete .cs file containing a single [TestFixture] class.");
            prompt.AppendLine();
            prompt.AppendLine("=== THIS IS THE PART MOST OFTEN GOT WRONG - CHECK IT ===");
            prompt.AppendLine($"- The file MUST declare: namespace {profile.TestNamespace};");

            if (profile.BaseClassName != null)
            {
                prompt.AppendLine($"- The class MUST be declared: [TestFixture] public class YourTestName : {profile.BaseClassName}");
                prompt.AppendLine("- Each test MUST be a [Test] public async Task method. There is no Run() method.");
                prompt.AppendLine("- There MUST be no Playwright.CreateAsync, no LaunchAsync, no NewPageAsync, no browser variable.");
                prompt.AppendLine("- Use the inherited Page property (capital P) and the inherited Expect(...) helper.");
                prompt.AppendLine("- Navigate with await GotoAsync(); - it already knows the base URL.");
            }
        }
        else
        {
            prompt.AppendLine("6. Output one complete .cs file.");
        }

        prompt.AppendLine();
        prompt.AppendLine("Return only file content. No explanation, no markdown fences.");
    }

    /// <summary>Suggests a file name for the generated fixture.</summary>
    public static string SuggestFileName(string generatedCode, string fallback = "GeneratedTest")
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            generatedCode, @"class\s+(?<name>\w+)\s*(?::|\r?\n|\{)");

        var name = match.Success ? match.Groups["name"].Value : fallback;
        return $"{name}.cs";
    }
}
