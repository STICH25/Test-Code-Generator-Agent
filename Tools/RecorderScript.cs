namespace PlaywrightAgentAI.Tools;

/// <summary>
/// The page-side recorder, shared by both hosts.
///
/// Identical JavaScript runs in the Playwright browser (via AddInitScript) and in the
/// WebView2 preview (via AddScriptToExecuteOnDocumentCreatedAsync). Keeping one copy is
/// what makes the two recording surfaces produce the same locators; the only difference is
/// how the payload gets back to the app, which the transport line below abstracts.
///
/// Listeners are registered in the capture phase so a site that calls stopPropagation in
/// its own handlers cannot hide interactions from the recorder.
/// </summary>
internal static class RecorderScript
{
    /// <summary>Playwright: an exposed binding is a function on window.</summary>
    public const string PlaywrightTransport = "window.__paaRecord(payload);";

    /// <summary>WebView2: messages go through the host object channel instead.</summary>
    public const string WebViewTransport = "window.chrome.webview.postMessage(payload);";

    public static string For(string transport) => Template.Replace("__TRANSPORT__", transport);

    private const string Template = """
        (() => {
          if (window.__paaRecorderInstalled) return;
          window.__paaRecorderInstalled = true;

          const send = (action) => {
            try {
              const payload = JSON.stringify(action);
              __TRANSPORT__
            } catch (e) {
              /* the channel can disappear mid-navigation; dropping the event is fine */
            }
          };

          const testIdOf = (el) =>
            el.getAttribute('data-testid') ||
            el.getAttribute('data-test-id') ||
            el.getAttribute('data-test') ||
            el.getAttribute('data-qa') ||
            null;

          const accessibleName = (el) => {
            const label = el.getAttribute('aria-label');
            if (label) return label.trim();

            const labelledBy = el.getAttribute('aria-labelledby');
            if (labelledBy) {
              const target = document.getElementById(labelledBy);
              if (target) return (target.innerText || '').trim();
            }

            if (el.id) {
              const forLabel = document.querySelector(`label[for="${CSS.escape(el.id)}"]`);
              if (forLabel) return (forLabel.innerText || '').trim();
            }

            const text = (el.innerText || el.value || '').trim();
            return text.length > 0 && text.length <= 100 ? text : '';
          };

          // Mirrors the subset of ARIA roles Playwright's GetByRole covers most often.
          const roleOf = (el) => {
            const explicit = el.getAttribute('role');
            if (explicit) return explicit;

            const tag = el.tagName.toLowerCase();
            if (tag === 'a' && el.hasAttribute('href')) return 'link';
            if (tag === 'button') return 'button';
            if (tag === 'select') return 'combobox';
            if (tag === 'textarea') return 'textbox';
            if (/^h[1-6]$/.test(tag)) return 'heading';
            if (tag === 'img') return 'img';
            if (tag === 'input') {
              const type = (el.getAttribute('type') || 'text').toLowerCase();
              if (type === 'submit' || type === 'button' || type === 'reset') return 'button';
              if (type === 'checkbox') return 'checkbox';
              if (type === 'radio') return 'radio';
              return 'textbox';
            }
            return '';
          };

          // Last resort: a short, reasonably stable CSS path.
          const cssPath = (el) => {
            const parts = [];
            let node = el;
            while (node && node.nodeType === 1 && parts.length < 5) {
              let part = node.tagName.toLowerCase();
              if (node.id) {
                parts.unshift(`#${CSS.escape(node.id)}`);
                break;
              }
              const cls = (node.getAttribute('class') || '')
                .split(/\s+/)
                .filter((c) => c && !/^(ng-|css-|sc-)/.test(c))[0];
              if (cls) part += `.${CSS.escape(cls)}`;
              const parent = node.parentElement;
              if (parent) {
                const siblings = Array.from(parent.children).filter(
                  (c) => c.tagName === node.tagName);
                if (siblings.length > 1) {
                  part += `:nth-of-type(${siblings.indexOf(node) + 1})`;
                }
              }
              parts.unshift(part);
              node = node.parentElement;
            }
            return parts.join(' > ');
          };

          const describe = (el, kind, value) => ({
            kind,
            selector: cssPath(el),
            role: roleOf(el),
            name: accessibleName(el),
            text: (el.innerText || '').trim().slice(0, 100),
            testId: testIdOf(el),
            tag: el.tagName.toLowerCase(),
            value: value === undefined ? null : String(value),
            url: location.href
          });

          document.addEventListener('click', (event) => {
            const el = event.target instanceof Element
              ? event.target.closest('a,button,input,select,textarea,[role],[onclick],label,li,td,th,summary') || event.target
              : null;
            if (el) send(describe(el, 'click'));
          }, true);

          document.addEventListener('change', (event) => {
            const el = event.target;
            if (!(el instanceof Element)) return;

            const tag = el.tagName.toLowerCase();
            if (tag === 'select') {
              const selected = el.options[el.selectedIndex];
              send(describe(el, 'select', selected ? selected.text : el.value));
              return;
            }

            const type = (el.getAttribute('type') || '').toLowerCase();
            if (type === 'checkbox' || type === 'radio') {
              send(describe(el, el.checked ? 'check' : 'uncheck'));
              return;
            }

            if (tag === 'input' || tag === 'textarea') {
              // Never record what was typed into a password field.
              send(describe(el, 'fill', type === 'password' ? '<redacted>' : el.value));
            }
          }, true);

          document.addEventListener('keydown', (event) => {
            if (event.key !== 'Enter') return;
            const el = event.target;
            if (el instanceof Element && /^(input|textarea)$/i.test(el.tagName)) {
              send(describe(el, 'press', 'Enter'));
            }
          }, true);
        })();
        """;
}
