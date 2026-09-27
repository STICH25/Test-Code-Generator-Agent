namespace PlaywrightAgentAI.Services;

/// <summary>
/// What the app learned about a linked test-automation solution.
///
/// This is the "house style" that generated code has to fit: which base class tests derive
/// from, which helpers exist, where test files live, and a real example to imitate.
/// </summary>
public class SolutionProfile
{
    public required string RootPath { get; init; }

    public string? ProjectFile { get; set; }

    /// <summary>Namespace existing tests are declared in.</summary>
    public string TestNamespace { get; set; } = "PlaywrightTests";

    /// <summary>Absolute folder new test files should be written to.</summary>
    public string TestDirectory { get; set; } = "";

    /// <summary>Abstract base class tests derive from, e.g. BaseTest. Null if none found.</summary>
    public string? BaseClassName { get; set; }

    public string? BaseClassSource { get; set; }

    /// <summary>Static helper classes available to tests, e.g. ReportManager.</summary>
    public List<string> Helpers { get; } = [];

    public string? ExampleTestName { get; set; }

    public string? ExampleTestSource { get; set; }

    /// <summary>Playwright:BaseUrl from the solution's appsettings.json, if present.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Anything the user should know about what was or wasn't found.</summary>
    public List<string> Notes { get; } = [];

    // ---- Gherkin / Reqnroll ----

    /// <summary>True when Reqnroll (or SpecFlow) is referenced, so BDD output is expected.</summary>
    public bool SupportsGherkin { get; set; }

    /// <summary>Folder holding .feature files. New features are written here.</summary>
    public string? FeaturesDirectory { get; set; }

    /// <summary>Folder holding [Binding] step classes. New step files are written here.</summary>
    public string? StepDefinitionsDirectory { get; set; }

    public List<FeatureFile> Features { get; } = [];

    /// <summary>Step patterns that already exist, so generation reuses instead of duplicating.</summary>
    public List<StepBinding> StepBindings { get; } = [];

    /// <summary>An existing [Binding] class, supplied as the shape to imitate.</summary>
    public string? StepClassSource { get; set; }

    public string? StepClassName { get; set; }

    /// <summary>Every [Binding] class found, offered as a target to append new steps to.</summary>
    public List<StepDefinitionFile> StepDefinitionFiles { get; } = [];

    // ---- Page objects ----

    /// <summary>Folder new page objects are written to.</summary>
    public string? PageObjectsDirectory { get; set; }

    /// <summary>Page objects already in the solution, so generation extends rather than duplicates.</summary>
    public List<PageObjectFile> PageObjects { get; } = [];

    /// <summary>An existing page object supplied as the shape to imitate.</summary>
    public PageObjectFile? ExamplePageObject { get; set; }

    /// <summary>Ready to produce feature + step files.</summary>
    public bool CanWriteGherkin =>
        SupportsGherkin &&
        !string.IsNullOrEmpty(FeaturesDirectory) &&
        !string.IsNullOrEmpty(StepDefinitionsDirectory);

    /// <summary>True when there is enough here to shape generation.</summary>
    public bool IsUsable => !string.IsNullOrEmpty(TestDirectory) && Directory.Exists(TestDirectory);

    public string Describe()
    {
        var name = Path.GetFileName(RootPath);

        if (CanWriteGherkin)
            return $"{name} - Gherkin, {Features.Count} feature file(s), {StepBindings.Count} existing step(s), " +
                   $"{PageObjects.Count} page object(s)";

        return BaseClassName == null
            ? $"{name} - namespace {TestNamespace}, no base class detected"
            : $"{name} - {BaseClassName} in namespace {TestNamespace}";
    }
}
