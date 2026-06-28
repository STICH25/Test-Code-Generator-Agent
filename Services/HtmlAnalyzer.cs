using AngleSharp;
using AngleSharp.Dom;
using PlaywrightAgentAI.Models;
using System;
using System.Linq;
using System.Collections.Generic;

namespace PlaywrightAgentAI.Services;

public class HtmlAnalyzer
{
    private const int MaxSectionHtmlLength = 5000;
    private const int MaxContainerTraversalLevels = 6;
    private const int MinSectionContentLength = 50; // Minimum chars to consider as valid section

    public async Task<DomSnapshot> Analyze(string html, string? target = null)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            Console.Error.WriteLine("Warning: HTML content is empty. Returning empty snapshot.");
            return new DomSnapshot();
        }

        try
        {
            var config = Configuration.Default;
            var context = BrowsingContext.New(config);
            var document = await context.OpenAsync(req => req.Content(html));

            var snapshot = new DomSnapshot();

            // Extract basic elements
            ExtractElements(document, snapshot);

            // If the user provided a target (from TestObjective), try to find that section first
            if (!string.IsNullOrWhiteSpace(target))
            {
                TryExtractTargetSection(document, snapshot, target);
            }

            // If no target section was discovered, fallback to dynamic section extraction
            if (snapshot.SectionHtml.Count == 0)
            {
                ExtractSectionsDynamically(document, snapshot);
            }

            return snapshot;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to parse HTML: {ex.GetType().Name}");
            Console.Error.WriteLine($"Details: {ex.Message}");
            return new DomSnapshot();
        }
    }

    private void ExtractElements(IDocument document, DomSnapshot snapshot)
    {
        ExtractElementsBySelector(document, "input", snapshot.Inputs, el =>
            el.Id ?? el.GetAttribute("name") ?? "");

        ExtractElementsBySelector(document, "button", snapshot.Buttons, el =>
            el.TextContent?.Trim() ?? "");

        ExtractElementsBySelector(document, "a", snapshot.Links, el =>
            el.TextContent?.Trim() ?? "");

        ExtractElementsBySelector(document, "h1, h2, h3, h4, h5, h6", snapshot.Headings, el =>
            el.TextContent?.Trim() ?? "");

        ExtractElementsBySelector(document, "div[data-testid], div[class*='card'], div[class*='section'], div[class*='skill']",
            snapshot.Elements, el => el.ClassName ?? "");
    }

    private void ExtractElementsBySelector(IDocument document, string selector, ICollection<string> collection, Func<IElement, string> extractor)
    {
        try
        {
            foreach (var element in document.QuerySelectorAll(selector))
            {
                try
                {
                    var value = extractor(element as IElement ?? element);
                    if (!string.IsNullOrWhiteSpace(value))
                        collection.Add(value);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to extract element for selector '{selector}': {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Failed to query selector '{selector}': {ex.Message}");
        }
    }

    private void ExtractSectionsDynamically(IDocument document, DomSnapshot snapshot)
    {
        try
        {
            // Strategy 1: Extract semantic sections (<section>, <article> tags)
            ExtractSemanticSections(document, snapshot);

            // Strategy 2: Extract heading-based sections (any heading with following content)
            ExtractHeadingBasedSections(document, snapshot);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Error in ExtractSectionsDynamically: {ex.Message}");
        }
    }

    private void ExtractSemanticSections(IDocument document, DomSnapshot snapshot)
    {
        try
        {
            foreach (var element in document.QuerySelectorAll("section, article, [role='region']"))
            {
                try
                {
                    var container = element as IElement;
                    if (container == null || string.IsNullOrWhiteSpace(container.TextContent))
                        continue;

                    // Get section identifier (from heading inside or from aria-label/data attributes)
                    var sectionName = ExtractSectionIdentifier(container);
                    if (string.IsNullOrWhiteSpace(sectionName))
                        continue;

                    if (snapshot.SectionHtml.ContainsKey(sectionName))
                        continue; // Avoid duplicates

                    PopulateSectionSnapshot(snapshot, container, sectionName);
                    Console.WriteLine($"Extracted semantic section '{sectionName}' with {(snapshot.SectionItems?.GetValueOrDefault(sectionName, new()).Count ?? 0)} items.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Error extracting semantic section: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Error in ExtractSemanticSections: {ex.Message}");
        }
    }

    private void ExtractHeadingBasedSections(IDocument document, DomSnapshot snapshot)
    {
        try
        {
            var headings = document.QuerySelectorAll("h1, h2, h3, h4, h5, h6").ToList();

            foreach (var heading in headings)
            {
                try
                {
                    var headingText = heading.TextContent?.Trim();
                    if (string.IsNullOrWhiteSpace(headingText) || headingText.Length > 100)
                        continue; // Skip empty or unusually long headings

                    var sectionName = headingText.ToLowerInvariant();
                    if (snapshot.SectionHtml.ContainsKey(sectionName))
                        continue; // Avoid duplicates

                    // Find container for this heading
                    var container = FindContainer(heading as IElement);
                    if (container == null || container.TextContent?.Length < MinSectionContentLength)
                        continue;

                    PopulateSectionSnapshot(snapshot, container, sectionName);
                    Console.WriteLine($"Extracted heading-based section '{sectionName}' with {(snapshot.SectionItems?.GetValueOrDefault(sectionName, new()).Count ?? 0)} items.");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning: Error extracting heading-based section: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Error in ExtractHeadingBasedSections: {ex.Message}");
        }
    }

    private void TryExtractTargetSection(IDocument document, DomSnapshot snapshot, string targetPhrase)
    {
        try
        {
            var phrase = targetPhrase.Trim().ToLowerInvariant();

            // Prefer exact heading matches first
            var headingMatch = document.QuerySelectorAll("h1, h2, h3, h4, h5, h6")
                .FirstOrDefault(h => h.TextContent?.Trim().ToLowerInvariant().Contains(phrase) == true);

            var found = headingMatch as IElement;

            // If no heading, search other textual elements
            found ??= document.All
                .OfType<IElement>()
                .FirstOrDefault(e => e.TextContent?.Trim().ToLowerInvariant().Contains(phrase) == true);

            if (found != null)
            {
                var container = FindContainer(found);
                if (container != null)
                {
                    PopulateSectionSnapshot(snapshot, container, phrase);
                    Console.WriteLine($"Extracted target section '{phrase}' with selector '{snapshot.SectionSelectors?.GetValueOrDefault(phrase) ?? "N/A"}' and {(snapshot.SectionItems?.GetValueOrDefault(phrase, new()).Count ?? 0)} items.");
                }
            }
            else
            {
                Console.WriteLine($"No DOM element found matching target phrase '{targetPhrase}'.");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: TryExtractTargetSection failed: {ex.Message}");
        }
    }

    private string ExtractSectionIdentifier(IElement container)
    {
        // Try to get from heading inside the container
        var heading = container.QuerySelectorAll("h1, h2, h3, h4, h5, h6").FirstOrDefault();
        if (heading != null && !string.IsNullOrWhiteSpace(heading.TextContent))
            return heading.TextContent.Trim().ToLowerInvariant();

        // Try aria-label
        var ariaLabel = container.GetAttribute("aria-label");
        if (!string.IsNullOrWhiteSpace(ariaLabel))
            return ariaLabel.Trim().ToLowerInvariant();

        // Try data-section-name or similar
        var dataSection = container.GetAttribute("data-section") ?? container.GetAttribute("data-name");
        if (!string.IsNullOrWhiteSpace(dataSection))
            return dataSection.Trim().ToLowerInvariant();

        // Use first significant text content
        var text = container.TextContent?.Trim() ?? "";
        if (text.Length > 0)
            return text.Substring(0, Math.Min(50, text.Length)).ToLowerInvariant();

        return "";
    }

    private void PopulateSectionSnapshot(DomSnapshot snapshot, IElement container, string key)
    {
        try
        {
            var sectionHtml = container.OuterHtml;
            if (sectionHtml.Length > MaxSectionHtmlLength)
                sectionHtml = sectionHtml[..MaxSectionHtmlLength] + "... [truncated]";

            snapshot.SectionHtml[key] = sectionHtml;
            snapshot.SectionSelectors[key] = BuildSelector(container);

            var items = container.QuerySelectorAll("ul > li, ol > li")
                .Select(li => li.TextContent?.Trim() ?? "")
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (items.Count > 0)
                snapshot.SectionItems[key] = items;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Warning: Error populating section snapshot: {ex.Message}");
        }
    }

    private string BuildSelector(IElement container)
    {
        var classes = (container.ClassName ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        var selector = container.TagName.ToLower();
        if (classes.Length > 0)
            selector += $".{classes[0]}";
        return selector;
    }

    private IElement? FindContainer(IElement element)
    {
        var current = element;
        for (int i = 0; i < MaxContainerTraversalLevels && current.ParentElement != null; i++)
        {
            current = current.ParentElement;
            var tag = current.TagName.ToLower();
            if (tag is "div" or "section" or "article")
                return current;
        }
        return element.ParentElement;
    }
}